using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Storage.Sqlite;
using KsitalTelemetryHub.UI.Avalonia.Services;
using KsitalTelemetryHub.UI.Avalonia.Views;

namespace KsitalTelemetryHub.UI.Avalonia;

public partial class App : Application
{
    public static string DatabasePath { get; private set; } = "telemetry.db";

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogError("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        };

        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            WorkerServiceManager.StopWorkerIfStandalone();
        };
    }

    public static void Log(string message)
    {
        try
        {
            string logsDir = AppPaths.GetLogsDirectory();
            Directory.CreateDirectory(logsDir);
            var logFile = Path.Combine(logsDir, "startup.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public static void LogError(string source, Exception? ex)
    {
        try
        {
            string logsDir = AppPaths.GetLogsDirectory();
            Directory.CreateDirectory(logsDir);
            var logFile = Path.Combine(logsDir, "startup.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] {source}: {ex?.ToString() ?? "null"}{Environment.NewLine}");
        }
        catch { }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        DatabasePath = AppPaths.GetDatabasePath();
        Log($"App.OnFrameworkInitializationCompleted entered. DB={DatabasePath}");

        try
        {
            AppDbContext.EnsureDatabaseUpdated(DatabasePath);
            BackupService.CheckAndPerformDailyBackup();
        }
        catch (Exception ex)
        {
            LogError("DatabaseInitialization", ex);
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
