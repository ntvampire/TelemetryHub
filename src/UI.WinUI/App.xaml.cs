using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace KsitalTelemetryHub.UI.WinUI;

public partial class App : Application
{
    public static Window? MainWindowInstance { get; private set; }
    public static string DatabasePath { get; set; } = "telemetry.db";

    public App()
    {
        Log("App initializing...");
        this.InitializeComponent();

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogError("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        };

        this.UnhandledException += (s, e) =>
        {
            LogError("Application.UnhandledException", e.Exception);
        };
    }

    public static void Log(string message)
    {
        try
        {
            var logFile = Path.Combine(AppContext.BaseDirectory, "startup.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public static void LogError(string source, Exception? ex)
    {
        try
        {
            var logFile = Path.Combine(AppContext.BaseDirectory, "startup.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] {source}: {ex?.ToString() ?? "null"}{Environment.NewLine}");
        }
        catch { }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log("OnLaunched entered");
        try
        {
            // Поиск БД рядом с исполняемым файлом или в рабочем каталоге
            if (!File.Exists(DatabasePath))
            {
                var appDir = System.AppContext.BaseDirectory;
                var localDb = Path.Combine(appDir, "telemetry.db");
                if (File.Exists(localDb))
                {
                    DatabasePath = localDb;
                }
            }

            MainWindowInstance = new MainWindow();
            MainWindowInstance.Activate();
            Log("MainWindow activated successfully");
        }
        catch (Exception ex)
        {
            LogError("OnLaunched", ex);
            throw;
        }
    }
}
