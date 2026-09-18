using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Parser.Ccu825;
using KsitalTelemetryHub.Parser.Owen;
using KsitalTelemetryHub.Storage.Sqlite;
using Xunit;

namespace KsitalTelemetryHub.Tests;

public class EndToEndIntegrationTests : IDisposable
{
    private readonly string _dbPath;

    public EndToEndIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"e2e_test_{Guid.NewGuid():N}.db");
        using var db = new AppDbContext(_dbPath);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public async Task FullCycle_Ccu825_CommandQueue_And_IncomingTelemetry_SavesCorrectly()
    {
        using var db = new AppDbContext(_dbPath);

        // 1. Создаем объект CCU-825
        var ccuObj = new MonitoredObject
        {
            Name = "Котельная №4 (CCU-825)",
            PhoneNumber = "+79110001122",
            District = "Северный участок",
            DeviceType = DeviceType.Ccu825,
            DevicePassword = "pass"
        };
        db.Objects.Add(ccuObj);
        await db.SaveChangesAsync();

        // 2. Диспетчер выбирает первый шаблон команды для CCU-825
        var templates = DeviceCommandBuilder.GetTemplates(ccuObj.DeviceType);
        Assert.NotEmpty(templates);

        var firstTemplate = templates[0];
        string payload = DeviceCommandBuilder.BuildPayload(firstTemplate.Pattern, ccuObj.DevicePassword);

        var outgoingCmd = new OutgoingCommand
        {
            MonitoredObjectId = ccuObj.Id,
            PhoneNumber = ccuObj.PhoneNumber,
            RawPayload = payload,
            Description = firstTemplate.Title,
            CreatedAt = DateTime.UtcNow,
            Status = CommandStatus.Pending
        };
        db.OutgoingCommands.Add(outgoingCmd);
        await db.SaveChangesAsync();

        Assert.False(string.IsNullOrWhiteSpace(outgoingCmd.RawPayload));
        Assert.StartsWith("pass", outgoingCmd.RawPayload);

        // 3. Эмуляция отправки модемом
        outgoingCmd.Status = CommandStatus.Sent;
        outgoingCmd.SentAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // 4. Эмуляция получения входящего SMS-ответа от CCU-825
        string incomingSms = "State: Out1=0 Out2=1 In1=NORM In2=ALARM T1=+23.5C T2=+68.0C T3=+19.0C Vbat=4.12V 220V=NORM";

        var parser = new Ccu825MessageParser();
        var report = parser.Parse(incomingSms, DateTime.UtcNow);

        // 5. Сохранение телеметрии через контекст
        await db.SaveReportAsync(ccuObj.PhoneNumber, report);

        // 6. Проверки результатов в БД
        var savedObj = await db.Objects
            .Include(o => o.TelemetryRecords).ThenInclude(t => t.Temperatures)
            .FirstAsync(o => o.Id == ccuObj.Id);

        var lastTelemetry = savedObj.TelemetryRecords.OrderByDescending(t => t.Timestamp).FirstOrDefault();
        Assert.NotNull(lastTelemetry);
        Assert.Equal(PowerState.Normal, lastTelemetry.MainPower);
        Assert.Equal(4.12, lastTelemetry.BatteryVoltage);

        var t2 = lastTelemetry.Temperatures.FirstOrDefault(t => t.SensorCode == "T2");
        Assert.NotNull(t2);
        Assert.Equal(68.0, t2.Value);

        // Проверка фиксации тревоги по шлейфу In2
        Assert.True(report.IsAlarm);
        Assert.Contains("In2", report.AlarmDescription);
    }

    [Fact]
    public async Task FullCycle_OwenPlc_AlarmResponse_RegistersAlarmInDb()
    {
        using var db = new AppDbContext(_dbPath);

        // 1. Создаем объект ОВЕН ПЛК
        var owenObj = new MonitoredObject
        {
            Name = "ЦТП-2 (ОВЕН)",
            PhoneNumber = "+79229998877",
            District = "Западный участок",
            DeviceType = DeviceType.OwenPlc,
            DevicePassword = "123"
        };
        db.Objects.Add(owenObj);
        await db.SaveChangesAsync();

        // 2. Диспетчер выбирает первый шаблон команды для ОВЕН ПЛК
        var templates = DeviceCommandBuilder.GetTemplates(owenObj.DeviceType);
        Assert.NotEmpty(templates);

        var firstTemplate = templates[0];
        string payload = DeviceCommandBuilder.BuildPayload(firstTemplate.Pattern, owenObj.DevicePassword);

        var outgoingCmd = new OutgoingCommand
        {
            MonitoredObjectId = owenObj.Id,
            PhoneNumber = owenObj.PhoneNumber,
            RawPayload = payload,
            Description = firstTemplate.Title,
            CreatedAt = DateTime.UtcNow,
            Status = CommandStatus.Pending
        };
        db.OutgoingCommands.Add(outgoingCmd);
        await db.SaveChangesAsync();

        Assert.False(string.IsNullOrWhiteSpace(outgoingCmd.RawPayload));

        // 3. Эмуляция аварийного ответа от модема ПМ01 (пропадание 220V и перегрев)
        string incomingSms = "OWEN ALARM: T1=95.0C T2=72.5C 220V=0 VBAT=11.9V ERR=ПЕРЕГРЕВ КОТЛА";

        var parser = new OwenMessageParser();
        var report = parser.Parse(incomingSms, DateTime.UtcNow);

        await db.SaveReportAsync(owenObj.PhoneNumber, report);

        // 4. Проверка записи аварии
        var savedObj = await db.Objects
            .Include(o => o.TelemetryRecords)
            .FirstAsync(o => o.Id == owenObj.Id);

        var lastTelemetry = savedObj.TelemetryRecords.Last();
        Assert.Equal(PowerState.Off, lastTelemetry.MainPower);

        var alarms = await db.Alarms.Where(a => a.MonitoredObjectId == owenObj.Id).ToListAsync();
        Assert.NotEmpty(alarms);
        Assert.Contains(alarms, a => a.Description.Contains("220V"));
    }

    [Fact]
    public async Task EventJournal_RecordsEvents_And_RotatesTo100Entries()
    {
        using var db = new AppDbContext(_dbPath);

        var obj = new MonitoredObject
        {
            Name = "Тестовый объект Журнала",
            PhoneNumber = "+79998887766",
            District = "Центральный участок",
            DeviceType = DeviceType.Ksital,
            DevicePassword = "00000"
        };
        db.Objects.Add(obj);
        await db.SaveChangesAsync();

        // Добавляем 120 событий разного типа
        for (int i = 1; i <= 120; i++)
        {
            db.Alarms.Add(new AlarmEvent
            {
                MonitoredObjectId = obj.Id,
                Timestamp = DateTime.UtcNow.AddMinutes(i),
                Description = $"Событие #{i}",
                EventType = (i % 3 == 0) ? "Alarm" : ((i % 3 == 1) ? "Report" : "Command"),
                IsAcknowledged = (i % 3 != 0)
            });
        }
        await db.SaveChangesAsync();

        // Ротация до 100 записей
        await db.RotateJournalEventsAsync(100);

        var totalEvents = await db.Alarms.CountAsync();
        Assert.Equal(100, totalEvents);

        // Проверяем, что сохранились самые свежие 100 событий (с #21 по #120)
        var oldestRemaining = await db.Alarms.OrderBy(a => a.Timestamp).FirstAsync();
        Assert.Equal("Событие #21", oldestRemaining.Description);

        var newestRemaining = await db.Alarms.OrderByDescending(a => a.Timestamp).FirstAsync();
        Assert.Equal("Событие #120", newestRemaining.Description);
    }

    [Fact]
    public async Task MonitoredObject_WithCreatedAtConstraint_InsertsSuccessfully()
    {
        string testDb = Path.Combine(Path.GetTempPath(), $"createdat_test_{Guid.NewGuid():N}.db");
        try
        {
            AppDbContext.EnsureDatabaseUpdated(testDb);

            using var db = new AppDbContext(testDb);
            var obj = new MonitoredObject
            {
                Name = "Объект Проверки CreatedAt",
                PhoneNumber = "+79198765432",
                District = "Центральный",
                DeviceType = DeviceType.Ksital,
                DevicePassword = "12345",
                CreatedAt = DateTime.UtcNow
            };
            db.Objects.Add(obj);
            await db.SaveChangesAsync();

            var retrieved = await db.Objects.FirstOrDefaultAsync(o => o.PhoneNumber == "+79198765432");
            Assert.NotNull(retrieved);
            Assert.Equal("Объект Проверки CreatedAt", retrieved.Name);
            Assert.True(retrieved.CreatedAt > DateTime.MinValue);
        }
        finally
        {
            if (File.Exists(testDb))
            {
                try { File.Delete(testDb); } catch { }
            }
        }
    }

    [Fact]
    public async Task Objects_GroupedByDistrict_CorrectlyOrganizesAndOrders()
    {
        string testDb = Path.Combine(Path.GetTempPath(), $"grouping_test_{Guid.NewGuid():N}.db");
        try
        {
            AppDbContext.EnsureDatabaseUpdated(testDb);

            using var db = new AppDbContext(testDb);
            db.Objects.Add(new MonitoredObject { Name = "Котельная Б", PhoneNumber = "+79001110001", District = "Северный участок" });
            db.Objects.Add(new MonitoredObject { Name = "Котельная А", PhoneNumber = "+79001110002", District = "Северный участок" });
            db.Objects.Add(new MonitoredObject { Name = "Насосная 1", PhoneNumber = "+79001110003", District = "Южный участок" });
            db.Objects.Add(new MonitoredObject { Name = "Без района", PhoneNumber = "+79001110004", District = "" });
            await db.SaveChangesAsync();

            var dbObjects = await db.Objects.ToListAsync();
            var grouped = dbObjects
                .GroupBy(o => string.IsNullOrWhiteSpace(o.District) ? "Основной участок" : o.District.Trim())
                .OrderBy(g => g.Key)
                .ToList();

            Assert.Equal(3, grouped.Count);
            Assert.Equal("Основной участок", grouped[0].Key);
            Assert.Single(grouped[0]);
            Assert.Equal("Северный участок", grouped[1].Key);
            Assert.Equal(2, grouped[1].Count());
            Assert.Equal("Южный участок", grouped[2].Key);
            Assert.Single(grouped[2]);
        }
        finally
        {
            if (File.Exists(testDb))
            {
                try { File.Delete(testDb); } catch { }
            }
        }
    }

    [Fact]
    public async Task AlarmEvents_SnoozeAndAcknowledge_LogsToJournalAndRotates()
    {
        string testDb = Path.Combine(Path.GetTempPath(), $"alarm_snooze_{Guid.NewGuid():N}.db");
        try
        {
            AppDbContext.EnsureDatabaseUpdated(testDb);

            using var db = new AppDbContext(testDb);
            var obj = new MonitoredObject { Name = "Котельная №1", PhoneNumber = "+79998887766", District = "Северный участок" };
            db.Objects.Add(obj);
            await db.SaveChangesAsync();

            // 1. Создаем аварию
            var alarm = new AlarmEvent
            {
                MonitoredObjectId = obj.Id,
                Timestamp = DateTime.UtcNow,
                Description = "Авария: Падение давления",
                EventType = "Alarm",
                IsAcknowledged = false
            };
            db.Alarms.Add(alarm);
            await db.SaveChangesAsync();

            // 2. Откладывание на 5 минут - логируется в журнал
            var snoozeEvent = new AlarmEvent
            {
                MonitoredObjectId = obj.Id,
                Timestamp = DateTime.UtcNow,
                Description = $"Тревога по объекту '{obj.Name}' отложена на 5 мин (по кнопке «Отложить»): {alarm.Description}",
                EventType = "Service",
                IsAcknowledged = true
            };
            db.Alarms.Add(snoozeEvent);
            await db.SaveChangesAsync();

            var events = await db.Alarms.Where(a => a.MonitoredObjectId == obj.Id).ToListAsync();
            Assert.Equal(2, events.Count);
            Assert.Contains(events, e => e.EventType == "Service" && e.Description.Contains("отложена на 5 мин"));

            // 3. Подтверждение тревоги оператором
            alarm.IsAcknowledged = true;
            alarm.AcknowledgedAt = DateTime.UtcNow;

            var ackEvent = new AlarmEvent
            {
                MonitoredObjectId = obj.Id,
                Timestamp = DateTime.UtcNow,
                Description = $"Тревога подтверждена оператором: {obj.Name} ({alarm.Description})",
                EventType = "Service",
                IsAcknowledged = true
            };
            db.Alarms.Add(ackEvent);
            await db.SaveChangesAsync();
            await db.RotateJournalEventsAsync(100);

            var updatedEvents = await db.Alarms.Where(a => a.MonitoredObjectId == obj.Id).ToListAsync();
            Assert.Equal(3, updatedEvents.Count);
            var confirmedAlarm = updatedEvents.First(e => e.Id == alarm.Id);
            Assert.True(confirmedAlarm.IsAcknowledged);
            Assert.NotNull(confirmedAlarm.AcknowledgedAt);
        }
        finally
        {
            if (File.Exists(testDb))
            {
                try { File.Delete(testDb); } catch { }
            }
        }
    }

    [Fact]
    public async Task UnknownSenderSms_LogsToJournal_DoesNotCreateMonitoredObject()
    {
        string testDb = $"test_unknown_sms_{Guid.NewGuid():N}.db";
        try
        {
            AppDbContext.EnsureDatabaseUpdated(testDb);
            using var db = new AppDbContext(testDb);

            // Создаем 1 известный объект
            var knownObj = new MonitoredObject
            {
                PhoneNumber = "+79990001122",
                Name = "Котельная №1",
                District = "Северный участок",
                DeviceType = DeviceType.Ksital,
                DevicePassword = "00000"
            };
            db.Objects.Add(knownObj);
            await db.SaveChangesAsync();

            // Имитируем поступление SMS от неизвестного номера (например, баланс оператора или сторонний номер)
            string spamPhone = "+79998887766";
            string spamText = "Уважаемый абонент! Ваш баланс менее 50 руб.";

            var existingObj = await db.Objects.FirstOrDefaultAsync(o => o.PhoneNumber == spamPhone);
            Assert.Null(existingObj);

            // При получении от неизвестного номера: объект НЕ создается, пишется в журнал Alarms
            db.Alarms.Add(new AlarmEvent
            {
                MonitoredObjectId = null,
                Timestamp = DateTime.UtcNow,
                Description = $"Служебное SMS от {spamPhone}: {spamText}",
                EventType = "Service",
                IsAcknowledged = true,
                AcknowledgedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            await db.RotateJournalEventsAsync(100);

            // Проверяем: количество объектов в системе не изменилось (только 1 исходный)
            var allObjects = await db.Objects.ToListAsync();
            Assert.Single(allObjects);
            Assert.Equal("+79990001122", allObjects[0].PhoneNumber);

            // Проверяем: в журнале событий появилась служебная запись
            var journalEvents = await db.Alarms.ToListAsync();
            Assert.Single(journalEvents);
            Assert.Equal("Service", journalEvents[0].EventType);
            Assert.True(journalEvents[0].IsAcknowledged);
            Assert.Contains("баланс менее 50 руб", journalEvents[0].Description, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(testDb))
            {
                try { File.Delete(testDb); } catch { }
            }
        }
    }
}