using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Storage.Sqlite;

namespace KsitalTelemetryHub.UI.WinUI.Services;

public class AppConfig
{
    public string BackupDirectory { get; set; } = string.Empty;
    public string LastDailyBackupDate { get; set; } = string.Empty;
}

public static class BackupService
{
    private static readonly string SettingsFile = Path.Combine(AppContext.BaseDirectory, "settings.json");
    public const int MaxBackupCopies = 10;

    public static string GetDefaultBackupDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "backups");
    }

    public static AppConfig LoadConfig()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
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
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
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

        // 1. Попытка безопасного создания снапшота через VACUUM INTO (WAL-safe)
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

        // 2. Ротация: оставляем не более 10 самых свежих копий
        int totalCopies = RotateBackups(backupDir);

        // 3. Если это ежедневное копирование - фиксируем дату
        if (isDaily)
        {
            var cfg = LoadConfig();
            cfg.LastDailyBackupDate = DateTime.Today.ToString("yyyy-MM-dd");
            SaveConfig(cfg);
        }

        return (backupFile, totalCopies);
    }

    /// <summary>
    /// Ротация резервных копий: оставляет не более MaxBackupCopies (10) файлов.
    /// </summary>
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

    /// <summary>
    /// Автоматическая ежедневная проверка и создание резервной копии.
    /// </summary>
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
    /// Восстановление базы данных из выбранной резервной копии.
    /// </summary>
    public static void RestoreBackup(string backupFilePath)
    {
        if (!File.Exists(backupFilePath))
        {
            throw new FileNotFoundException("Файл резервной копии не найден:", backupFilePath);
        }

        string workingDb = App.DatabasePath;

        // 1. Создаем страховочную копию текущей БД перед накатом
        if (File.Exists(workingDb))
        {
            try
            {
                string safetyPath = Path.Combine(AppContext.BaseDirectory, $"telemetry_pre_restore_{DateTime.Now:yyyyMMdd_HHmmss}.bak");
                File.Copy(workingDb, safetyPath, true);
            }
            catch { }
        }

        // 2. Сбрасываем пулы подключений SQLite
        SqliteConnection.ClearAllPools();

        // 3. Заменяем файл БД
        File.Copy(backupFilePath, workingDb, overwrite: true);

        // 4. Удаляем возможные остаточные файлы журнала WAL/SHM
        string walFile = workingDb + "-wal";
        string shmFile = workingDb + "-shm";
        try { if (File.Exists(walFile)) File.Delete(walFile); } catch { }
        try { if (File.Exists(shmFile)) File.Delete(shmFile); } catch { }

        // 5. Инициализируем структуру и WAL-режим для восстановленной БД
        AppDbContext.EnsureDatabaseUpdated(workingDb);
    }
}
