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
        this.InitializeComponent();

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        };

        this.UnhandledException += (s, e) =>
        {
            LogCrash("Application.UnhandledException", e.Exception);
        };
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var logFile = Path.Combine(AppContext.BaseDirectory, "winui_crash.log");
            File.AppendAllText(logFile, $"[{DateTime.Now}] {source}: {ex?.ToString() ?? "Unknown exception"}\n\n");
        }
        catch { }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
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
        }
        catch (Exception ex)
        {
            LogCrash("OnLaunched", ex);
            throw;
        }
    }
}
