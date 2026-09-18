using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Storage.Sqlite;

Console.WriteLine("=== Ksital Telemetry Hub — Генератор тестовых аварий ===");

var candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

// 1. Аргумент командной строки (если передан путь к БД)
if (args.Length > 0 && File.Exists(args[0]))
{
    candidatePaths.Add(Path.GetFullPath(args[0]));
}

// 2. Автопоиск всех баз данных telemetry.db в репозитории
string baseDir = AppDomain.CurrentDomain.BaseDirectory;
string solutionRoot = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\"));

if (Directory.Exists(solutionRoot))
{
    try
    {
        var foundDbs = Directory.EnumerateFiles(solutionRoot, "telemetry.db", SearchOption.AllDirectories)
            .Where(f => !f.Contains("Backup", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase));
        foreach (var f in foundDbs)
        {
            candidatePaths.Add(Path.GetFullPath(f));
        }
    }
    catch { }
}

if (candidatePaths.Count == 0)
{
    candidatePaths.Add(Path.GetFullPath("telemetry.db"));
}

int createdCount = 0;

foreach (var dbPath in candidatePaths)
{
    try
    {
        Console.WriteLine($"[ИНФО] Обработка базы данных: {dbPath}");
        AppDbContext.EnsureDatabaseUpdated(dbPath);
        using var db = new AppDbContext(dbPath);

        var targetObj = await db.Objects.FirstOrDefaultAsync();
        if (targetObj == null)
        {
            targetObj = new MonitoredObject
            {
                Name = "Котельная №1 (Тестовая)",
                PhoneNumber = "+79991112233",
                District = "Северный участок",
                DeviceType = DeviceType.Ksital,
                DevicePassword = "00000"
            };
            db.Objects.Add(targetObj);
            await db.SaveChangesAsync();
        }

        var testAlarm = new AlarmEvent
        {
            MonitoredObjectId = targetObj.Id,
            Timestamp = DateTime.UtcNow,
            Description = $"ТЕСТОВАЯ ТРЕВОГА: Падение давления в контуре №1 ниже 0.8 бар! [{DateTime.Now:HH:mm:ss}]",
            EventType = "Alarm",
            IsAcknowledged = false
        };

        db.Alarms.Add(testAlarm);
        await db.SaveChangesAsync();
        createdCount++;

        Console.WriteLine($"[УСПЕХ] Тревога ID #{testAlarm.Id} создана для объекта '{targetObj.Name}' ({targetObj.District})");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ПРЕДУПРЕЖДЕНИЕ] Не удалось записать в {dbPath}: {ex.Message}");
    }
}

Console.WriteLine();
if (createdCount > 0)
{
    Console.WriteLine("[РЕЗУЛЬТАТ] Тестовая тревога успешно добавлена в базу данных!");
    Console.WriteLine("Если приложение запущено, окно тревоги со звуковым сигналом появится в течение 3 секунд.");
}
else
{
    Console.WriteLine("[ОШИБКА] Не удалось создать запись ни в одной базе данных.");
}