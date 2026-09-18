using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;

namespace KsitalTelemetryHub.Storage.Sqlite;

public class AppDbContext : DbContext
{
    private readonly string _dbPath;

    public DbSet<MonitoredObject> Objects => Set<MonitoredObject>();
    public DbSet<TelemetryRecord> Telemetry => Set<TelemetryRecord>();
    public DbSet<TemperatureRecord> Temperatures => Set<TemperatureRecord>();
    public DbSet<AlarmEvent> Alarms => Set<AlarmEvent>();
    public DbSet<OutgoingCommand> OutgoingCommands => Set<OutgoingCommand>();
    public DbSet<SystemStatus> SystemStatus => Set<SystemStatus>();

    public AppDbContext(string dbPath = "telemetry.db")
    {
        _dbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(_dbPath),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 5 // Ожидание снятия блокировки до 5 секунд против ошибок "database is locked"
        };
        optionsBuilder.UseSqlite(csb.ToString());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MonitoredObject>()
            .HasIndex(o => o.PhoneNumber)
            .IsUnique();

        modelBuilder.Entity<TelemetryRecord>()
            .HasIndex(t => t.Timestamp);

        modelBuilder.Entity<AlarmEvent>()
            .HasIndex(a => a.Timestamp);

        modelBuilder.Entity<OutgoingCommand>()
            .HasIndex(c => c.CreatedAt);

        modelBuilder.Entity<SystemStatus>()
            .HasKey(s => s.Id);
    }

    public async Task SaveReportAsync(string? senderPhone, TelemetrySnapshot report, CancellationToken cancellationToken = default)
    {
        string rawPhone = !string.IsNullOrWhiteSpace(senderPhone) ? senderPhone : report.SenderPhone;
        string cleanPhone = PhoneNumber.Normalize(rawPhone);
        if (string.IsNullOrEmpty(cleanPhone))
        {
            cleanPhone = "+79000000000";
        }

        var obj = await Objects.FirstOrDefaultAsync(o => o.PhoneNumber == cleanPhone, cancellationToken);
        if (obj == null)
        {
            obj = new MonitoredObject
            {
                PhoneNumber = cleanPhone,
                Name = string.IsNullOrWhiteSpace(report.DeviceName) ? $"Объект {cleanPhone}" : report.DeviceName,
                District = "Основной участок",
                DeviceType = DeviceType.Ksital,
                DevicePassword = "00000"
            };
            Objects.Add(obj);
            await SaveChangesAsync(cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(report.DeviceName) && (string.IsNullOrWhiteSpace(obj.Name) || obj.Name.StartsWith("Объект ")))
        {
            obj.Name = report.DeviceName;
        }

        var record = new TelemetryRecord
        {
            MonitoredObjectId = obj.Id,
            Timestamp = report.Timestamp != default ? report.Timestamp : DateTime.UtcNow,
            MainPower = report.MainPower,
            BatteryVoltage = report.BatteryVoltage
        };

        if (report.Temperatures != null)
        {
            foreach (var t in report.Temperatures)
            {
                record.Temperatures.Add(new TemperatureRecord
                {
                    SensorCode = t.Key,
                    Value = t.Value
                });
            }
        }

        Telemetry.Add(record);

        // Фиксация аварии основного питания 220V
        if (report.MainPower == PowerState.Off)
        {
            Alarms.Add(new AlarmEvent
            {
                MonitoredObjectId = obj.Id,
                Timestamp = record.Timestamp,
                Description = "Авария: Отсутствует основное питание 220V",
                EventType = "Alarm",
                IsAcknowledged = false
            });
        }

        // Фиксация технологической аварии из отчета
        if (report.IsAlarm && !string.IsNullOrWhiteSpace(report.AlarmDescription))
        {
            Alarms.Add(new AlarmEvent
            {
                MonitoredObjectId = obj.Id,
                Timestamp = record.Timestamp,
                Description = report.AlarmDescription,
                EventType = "Alarm",
                IsAcknowledged = false
            });
        }

        // Фиксация регулярного отчета телеметрии, ответа или служебного сообщения в журнале событий
        if (!report.IsAlarm && report.MainPower != PowerState.Off)
        {
            string pwrText = report.MainPower switch
            {
                PowerState.Normal => "220V: Есть",
                PowerState.Off => "220V: Нет",
                _ => ""
            };

            string batText = report.BatteryVoltage.HasValue ? $"АКБ: {report.BatteryVoltage:F1}В" : "";
            string tempText = report.Temperatures != null && report.Temperatures.Count > 0
                ? string.Join(", ", report.Temperatures.Select(t => $"{t.Key}={(t.Value > 0 ? "+" : "")}{t.Value:F1}°C"))
                : "";

            bool hasTelemetryData = !string.IsNullOrWhiteSpace(pwrText) || !string.IsNullOrWhiteSpace(batText) || !string.IsNullOrWhiteSpace(tempText);

            string eventType = hasTelemetryData ? "Report" : "Service";
            string summary = hasTelemetryData
                ? string.Join("; ", new[] { pwrText, batText, tempText }.Where(s => !string.IsNullOrWhiteSpace(s)))
                : (!string.IsNullOrWhiteSpace(report.RawText) ? report.RawText.Trim() : "Сообщение от оборудования");

            Alarms.Add(new AlarmEvent
            {
                MonitoredObjectId = obj.Id,
                Timestamp = record.Timestamp,
                Description = summary,
                EventType = eventType,
                IsAcknowledged = true,
                AcknowledgedAt = record.Timestamp
            });
        }

        await SaveChangesAsync(cancellationToken);
        await RotateJournalEventsAsync(100, cancellationToken);
    }

    public async Task RotateJournalEventsAsync(int maxCount = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            var excessIds = await Alarms
                .OrderByDescending(a => a.Timestamp)
                .Skip(maxCount)
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);

            if (excessIds.Count > 0)
            {
                var itemsToDelete = await Alarms
                    .Where(a => excessIds.Contains(a.Id))
                    .ToListAsync(cancellationToken);

                Alarms.RemoveRange(itemsToDelete);
                await SaveChangesAsync(cancellationToken);
            }
        }
        catch
        {
            // Игнорируем временные конфликты транзакций
        }
    }

    public Task SaveReportAsync(TelemetrySnapshot report, CancellationToken cancellationToken = default)
    {
        return SaveReportAsync(report.SenderPhone, report, cancellationToken);
    }

    public async Task UpdateWorkerHeartbeatAsync(
        string portName, 
        bool isModemConnected, 
        int signalCsq = 0, 
        string? operatorName = null, 
        string? lastError = null, 
        int newSmsProcessed = 0,
        CancellationToken cancellationToken = default)
    {
        var status = await SystemStatus.FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);
        if (status == null)
        {
            status = new SystemStatus
            {
                Id = 1,
                PortName = portName,
                IsModemConnected = isModemConnected,
                SignalStrengthCsq = signalCsq,
                OperatorName = operatorName,
                LastError = lastError,
                LastHeartbeat = DateTime.UtcNow,
                TotalSmsProcessed = newSmsProcessed
            };
            SystemStatus.Add(status);
        }
        else
        {
            status.PortName = portName;
            status.IsWorkerAlive = true;
            status.IsModemConnected = isModemConnected;
            if (signalCsq > 0) status.SignalStrengthCsq = signalCsq;
            if (!string.IsNullOrEmpty(operatorName)) status.OperatorName = operatorName;
            status.LastError = lastError;
            status.LastHeartbeat = DateTime.UtcNow;
            status.TotalSmsProcessed += newSmsProcessed;
        }

        await SaveChangesAsync(cancellationToken);
    }

    public static void EnsureDatabaseUpdated(string dbPath)
    {
        using var db = new AppDbContext(dbPath);
        db.Database.EnsureCreated();

        using var conn = db.Database.GetDbConnection();
        conn.Open();

        // 1. Включаем Write-Ahead Logging (WAL) и синхронизацию NORMAL для защиты данных 24/7
        using (var walCmd = conn.CreateCommand())
        {
            walCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
            walCmd.ExecuteNonQuery();
        }

        // 2. Детерминированная проверка схемы таблицы Objects
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var colCmd = conn.CreateCommand())
        {
            colCmd.CommandText = "PRAGMA table_info(Objects);";
            using var reader = colCmd.ExecuteReader();
            while (reader.Read())
            {
                existingColumns.Add(reader.GetString(1));
            }
        }

        if (!existingColumns.Contains("District"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE Objects ADD COLUMN District TEXT NOT NULL DEFAULT 'Основной участок';";
            cmd.ExecuteNonQuery();
        }

        if (!existingColumns.Contains("DeviceType"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE Objects ADD COLUMN DeviceType INTEGER NOT NULL DEFAULT 0;";
            cmd.ExecuteNonQuery();
        }

        if (!existingColumns.Contains("DevicePassword"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE Objects ADD COLUMN DevicePassword TEXT NOT NULL DEFAULT '00000';";
            cmd.ExecuteNonQuery();
        }

        // 3. Создание вспомогательных таблиц, если их нет
        using (var ddlCmd = conn.CreateCommand())
        {
            ddlCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS OutgoingCommands (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    MonitoredObjectId INTEGER NOT NULL,
                    PhoneNumber TEXT NOT NULL,
                    RawPayload TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    SentAt TEXT NULL,
                    Status INTEGER NOT NULL DEFAULT 0,
                    ErrorMessage TEXT NULL,
                    FOREIGN KEY (MonitoredObjectId) REFERENCES Objects(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS SystemStatus (
                    Id INTEGER PRIMARY KEY,
                    PortName TEXT NOT NULL,
                    IsWorkerAlive INTEGER NOT NULL DEFAULT 1,
                    IsModemConnected INTEGER NOT NULL DEFAULT 0,
                    SignalStrengthCsq INTEGER NOT NULL DEFAULT 0,
                    OperatorName TEXT NULL,
                    LastHeartbeat TEXT NOT NULL,
                    LastError TEXT NULL,
                    TotalSmsProcessed INTEGER NOT NULL DEFAULT 0,
                    RequestedPortName TEXT NULL
                );";
            ddlCmd.ExecuteNonQuery();
        }

        // 4. Проверка схемы таблицы SystemStatus на наличие RequestedPortName
        var statusColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var statusColCmd = conn.CreateCommand())
        {
            statusColCmd.CommandText = "PRAGMA table_info(SystemStatus);";
            using var reader = statusColCmd.ExecuteReader();
            while (reader.Read())
            {
                statusColumns.Add(reader.GetString(1));
            }
        }

        if (!statusColumns.Contains("RequestedPortName"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE SystemStatus ADD COLUMN RequestedPortName TEXT NULL;";
            cmd.ExecuteNonQuery();
        }

        // 5. Проверка схемы таблицы Alarms на наличие EventType
        var alarmColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var alarmColCmd = conn.CreateCommand())
        {
            alarmColCmd.CommandText = "PRAGMA table_info(Alarms);";
            using var reader = alarmColCmd.ExecuteReader();
            while (reader.Read())
            {
                alarmColumns.Add(reader.GetString(1));
            }
        }

        if (!alarmColumns.Contains("EventType"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE Alarms ADD COLUMN EventType TEXT NOT NULL DEFAULT 'Alarm';";
            cmd.ExecuteNonQuery();
        }
    }
}
