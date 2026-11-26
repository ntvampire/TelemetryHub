using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Storage.Sqlite;

namespace KsitalTelemetryHub.UI.Avalonia.Services;

public class AppConfig
{
    public string BackupDirectory { get; set; } = string.Empty;
    public string LastDailyBackupDate { get; set; } = string.Empty;
    public bool IsSoundAlarmEnabled { get; set; } = true;
    public string? PreferredComPort { get; set; }
}

public static class BackupService
{
    public const int MaxBackupCopies = 10;

    private static string GetSettingsFilePath() => AppPaths.GetSettingsFilePath();

    public static string GetDefaultBackupDirectory() => AppPaths.GetDefaultBackupDirectory();

    public static AppConfig LoadConfig()
    {
        try
        {
            string settingsFile = GetSettingsFilePath();
            if (File.Exists(settingsFile))
            {
                var json = File.ReadAllText(settingsFile);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg != null)
                {
                    if (string.IsNullOrWhiteSpace(cfg.BackupDirectory))
                    {
                        cfg.BackupDirectory = GetDefaultBackupDirectory();
                    }
                    return cfg;
                }
            }
        }
        catch { }

        return new AppConfig { BackupDirectory = GetDefaultBackupDirectory() };
    }

    public static void SaveConfig(AppConfig config)
    {
        try
        {
            string settingsFile = GetSettingsFilePath();
            string? dir = Path.GetDirectoryName(settingsFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(settingsFile, json);
        }
        catch { }
    }

    public static string GetBackupDirectory()
    {
        var cfg = LoadConfig();
        if (string.IsNullOrWhiteSpace(cfg.BackupDirectory))
        {
            cfg.BackupDirectory = GetDefaultBackupDirectory();
            SaveConfig(cfg);
        }
        return cfg.BackupDirectory;
    }

    public static void SetBackupDirectory(string directoryPath)
    {
        var cfg = LoadConfig();
        cfg.BackupDirectory = directoryPath;
        SaveConfig(cfg);
    }

    /// <summary>
    /// Создает резервную копию базы данных SQLite и выполняет ротацию (не более 10 копий).
    /// </summary>
    public static (string FilePath, int TotalCopies) CreateBackup(bool isDaily = false)
    {
        string workingDb = App.DatabasePath;
        if (!File.Exists(workingDb))
        {
            throw new FileNotFoundException("Рабочая база данных не найдена:", workingDb);
        }

        string backupDir = GetBackupDirectory();
        Directory.CreateDirectory(backupDir);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string backupFile = Path.Combine(backupDir, $"telemetry_backup_{timestamp}.db");

        bool snapshotCreated = false;
        try
        {
            using var db = new AppDbContext(workingDb);
            using var conn = db.Database.GetDbConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"VACUUM INTO '{backupFile.Replace("'", "''")}';";
            cmd.ExecuteNonQuery();
            snapshotCreated = true;
        }
        catch
        {
            // Фоллбэк на прямое копирование файла
        }

        if (!snapshotCreated || !File.Exists(backupFile))
        {
            File.Copy(workingDb, backupFile, overwrite: true);
        }

        int totalCopies = RotateBackups(backupDir);

        if (isDaily)
        {
            var cfg = LoadConfig();
            cfg.LastDailyBackupDate = DateTime.Today.ToString("yyyy-MM-dd");
            SaveConfig(cfg);
        }

        return (backupFile, totalCopies);
    }

    public static int RotateBackups(string backupDir)
    {
        try
        {
            if (!Directory.Exists(backupDir)) return 0;

            var dirInfo = new DirectoryInfo(backupDir);
            var backupFiles = dirInfo.GetFiles("telemetry_backup_*.db")
                .OrderBy(f => f.CreationTimeUtc)
                .ToList();

            while (backupFiles.Count > MaxBackupCopies)
            {
                var oldest = backupFiles[0];
                try
                {
                    oldest.Delete();
                }
                catch { }
                backupFiles.RemoveAt(0);
            }

            return backupFiles.Count;
        }
        catch
        {
            return 0;
        }
    }

    public static void CheckAndPerformDailyBackup()
    {
        try
        {
            var cfg = LoadConfig();
            string todayStr = DateTime.Today.ToString("yyyy-MM-dd");
            if (cfg.LastDailyBackupDate != todayStr)
            {
                CreateBackup(isDaily: true);
            }
        }
        catch (Exception ex)
        {
            App.LogError("BackupService.CheckAndPerformDailyBackup", ex);
        }
    }

    /// <summary>
    /// Проверяет, что файл действительно является целостной базой данных SQLite TelemetryHub.
    /// </summary>
    public static void ValidateSqliteBackupFile(string backupFilePath)
    {
        if (!File.Exists(backupFilePath))
            throw new FileNotFoundException("Файл резервной копии не найден:", backupFilePath);

        var fi = new FileInfo(backupFilePath);
        if (fi.Length < 100)
            throw new InvalidDataException("Файл резервной копии слишком мал и не является базой SQLite.");

        byte[] header = new byte[16];
        using (var fs = new FileStream(backupFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            int read = fs.Read(header, 0, header.Length);
            if (read < 16 || Encoding.ASCII.GetString(header, 0, 15) != "SQLite format 3")
                throw new InvalidDataException("Выбранный файл не является корректной базой данных SQLite.");
        }

        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = backupFilePath,
            Mode = SqliteOpenMode.ReadOnly
        };
        using var conn = new SqliteConnection(csb.ToString());
        conn.Open();

        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='Objects';";
        long hasObjectsTable = (long)(checkCmd.ExecuteScalar() ?? 0L);
        if (hasObjectsTable == 0)
            throw new InvalidDataException("Выбранная база данных SQLite не содержит таблицы Objects.");
    }

    /// <summary>
    /// Восстановление базы данных из выбранной резервной копии с безопасной остановкой службы сбора.
    /// </summary>
    public static async Task RestoreBackupAsync(string backupFilePath)
    {
        ValidateSqliteBackupFile(backupFilePath);

        string workingDb = App.DatabasePath;
        bool wasServiceRunning = WorkerServiceManager.IsWindowsServiceRunning();

        // 1. Останавливаем службу / автономный воркер, чтобы освободить дескрипторы WAL/SHM
        if (wasServiceRunning)
        {
            await WorkerServiceManager.StopWindowsServiceAsync();
        }
        WorkerServiceManager.StopWorkerIfStandalone();
        await Task.Delay(500);

        try
        {
            // 2. Страховочная копия текущей БД перед накатом
            if (File.Exists(workingDb))
            {
                try
                {
                    string backupDir = GetBackupDirectory();
                    Directory.CreateDirectory(backupDir);
                    string safetyPath = Path.Combine(backupDir, $"telemetry_pre_restore_{DateTime.Now:yyyyMMdd_HHmmss}.bak");
                    File.Copy(workingDb, safetyPath, true);
                }
                catch { }
            }

            // 3. Сбрасываем пулы подключений SQLite
            SqliteConnection.ClearAllPools();

            // 4. Удаляем возможные остаточные файлы журнала WAL/SHM до копирования
            string walFile = workingDb + "-wal";
            string shmFile = workingDb + "-shm";
            if (File.Exists(walFile)) File.Delete(walFile);
            if (File.Exists(shmFile)) File.Delete(shmFile);

            // 5. Заменяем файл БД
            string? dbDir = Path.GetDirectoryName(workingDb);
            if (!string.IsNullOrEmpty(dbDir)) Directory.CreateDirectory(dbDir);
            File.Copy(backupFilePath, workingDb, overwrite: true);

            // 6. Инициализируем структуру и WAL-режим для восстановленной БД
            AppDbContext.EnsureDatabaseUpdated(workingDb);
        }
        finally
        {
            // 7. Запускаем службу/воркер обратно
            if (wasServiceRunning)
            {
                await WorkerServiceManager.TryStartWindowsServiceAsync();
            }
            else
            {
                await WorkerServiceManager.EnsureWorkerStartedAsync();
            }
        }
    }
}
