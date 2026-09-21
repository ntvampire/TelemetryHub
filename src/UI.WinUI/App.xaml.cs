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

        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            Services.WorkerServiceManager.StopWorkerIfStandalone();
        };
    }

    public static void Log(string message)
    {
        try
        {
            var logFile = Path.Combine(AppContext.BaseDirectory, "startup.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            var tempLog = Path.Combine(Path.GetTempPath(), "telemetry_startup.log");
            File.AppendAllText(tempLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public static void LogError(string source, Exception? ex)
    {
        try
        {
            var logFile = Path.Combine(AppContext.BaseDirectory, "startup.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] {source}: {ex?.ToString() ?? "null"}{Environment.NewLine}");
            var tempLog = Path.Combine(Path.GetTempPath(), "telemetry_startup.log");
            File.AppendAllText(tempLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] {source}: {ex?.ToString() ?? "null"}{Environment.NewLine}");
        }
        catch { }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log("OnLaunched entered");
        try
        {
            // Рабочая копия БД всегда строго в папке с установленным приложением
            var appDir = System.AppContext.BaseDirectory;
            DatabasePath = Path.Combine(appDir, "telemetry.db");

            // Гарантируем актуальность структуры и таблиц БД
            Storage.Sqlite.AppDbContext.EnsureDatabaseUpdated(DatabasePath);

            // Ежедневное резервное копирование БД с ротацией (до 10 копий)
            Services.BackupService.CheckAndPerformDailyBackup();

            Log("Creating MainWindow...");
            MainWindowInstance = new MainWindow();
            Log("MainWindow created. Activating...");
            MainWindowInstance.Activate();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindowInstance);
            Log($"MainWindow activated successfully. HWND: 0x{hwnd:X}");
            ShowWindow(hwnd, 9); // SW_RESTORE
            SetForegroundWindow(hwnd);
        }
        catch (Exception ex)
        {
            LogError("OnLaunched", ex);
            throw;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
