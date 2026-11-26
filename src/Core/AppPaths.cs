using System;
using System.IO;
using System.Runtime.InteropServices;

namespace KsitalTelemetryHub.Core;

/// <summary>
/// Детерминированный резолвер путей к данным приложения (БД, конфиг, бэкапы, логи).
/// Гарантирует, что Service.Worker и UI.Avalonia используют один и тот же каталог данных
/// без требования прав записи в Program Files.
/// </summary>
public static class AppPaths
{
    private const string EnvDataDir = "TELEMETRY_HUB_DATA_DIR";
    private const string AppFolderName = "TelemetryHub";
    private const string DbFileName = "telemetry.db";

    public static string GetAppRootDirectory()
    {
        string baseDir = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string dirName = Path.GetFileName(baseDir);
        if (string.Equals(dirName, "WorkerService", StringComparison.OrdinalIgnoreCase))
        {
            string? parent = Directory.GetParent(baseDir)?.FullName;
            if (!string.IsNullOrEmpty(parent))
            {
                return parent;
            }
        }

        return baseDir;
    }

    public static string GetDataDirectory()
    {
        string? envDir = Environment.GetEnvironmentVariable(EnvDataDir);
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            string fullEnv = Path.GetFullPath(envDir);
            Directory.CreateDirectory(fullEnv);
            return fullEnv;
        }

        string appRoot = GetAppRootDirectory();
        string portableDb = Path.Combine(appRoot, DbFileName);

        // Обратная совместимость: если в корне программы уже есть база (dev/portable или апгрейд со старой версии)
        // и папка не лежит в защищённой системной директории (или БД уже там развернута), используем её.
        if (File.Exists(portableDb))
        {
            return appRoot;
        }

        if (!IsProtectedInstallDirectory(appRoot))
        {
            return appRoot;
        }

        string dataRoot = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppFolderName)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

        Directory.CreateDirectory(dataRoot);
        return dataRoot;
    }

    public static string GetDatabasePath(string? configuredDbPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredDbPath) && Path.IsPathRooted(configuredDbPath))
        {
            return Path.GetFullPath(configuredDbPath);
        }

        string fileName = string.IsNullOrWhiteSpace(configuredDbPath) ? DbFileName : configuredDbPath;
        return Path.Combine(GetDataDirectory(), fileName);
    }

    public static string GetSettingsFilePath() => Path.Combine(GetDataDirectory(), "settings.json");

    public static string GetDefaultBackupDirectory() => Path.Combine(GetDataDirectory(), "backups");

    public static string GetLogsDirectory() => Path.Combine(GetDataDirectory(), "logs");

    private static bool IsProtectedInstallDirectory(string path)
    {
        string full = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            return (!string.IsNullOrEmpty(pf) && full.StartsWith(pf, StringComparison.OrdinalIgnoreCase)) ||
                   (!string.IsNullOrEmpty(pf86) && full.StartsWith(pf86, StringComparison.OrdinalIgnoreCase));
        }

        return full.StartsWith("/usr/", StringComparison.Ordinal) ||
               full.StartsWith("/opt/", StringComparison.Ordinal);
    }
}
