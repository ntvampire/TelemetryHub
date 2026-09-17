using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace KsitalTelemetryHub.UI.WinUI;

public static class Program
{
    private static readonly string BaseLog = Path.Combine(AppContext.BaseDirectory, "startup.log");
    private static readonly string TempLog = Path.Combine(Path.GetTempPath(), "ksital_startup.log");

    public static void WriteLog(string msg)
    {
        Console.WriteLine(msg);
        try { File.AppendAllText(BaseLog, msg + Environment.NewLine); } catch { }
        try { File.AppendAllText(TempLog, msg + Environment.NewLine); } catch { }
    }

    [DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    [STAThread]
    public static void Main(string[] args)
    {
        WriteLog(">>> KsitalTelemetryHub Program.Main entered! <<<");
        WriteLog($"[{DateTime.Now}] Starting with args: {string.Join(" ", args)}");

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            WriteLog($"[AppDomain.UnhandledException] {e.ExceptionObject}");
        };

        AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
        {
            // Log WinRT or XAML initialization failures
            if (e.Exception is not OperationCanceledException && e.Exception is not TaskCanceledException)
            {
                WriteLog($"[FirstChanceException] {e.Exception.GetType().Name}: {e.Exception.Message}");
            }
        };

        try
        {
            WriteLog($"[{DateTime.Now}] Invoking StartXamlApp...");
            StartXamlApp();
        }
        catch (Exception ex)
        {
            WriteLog($"[{DateTime.Now}] CRASH in Program.Main: {ex}");
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartXamlApp()
    {
        WriteLog($"[{DateTime.Now}] Checking XAML process requirements...");
        try
        {
            XamlCheckProcessRequirements();
            WriteLog($"[{DateTime.Now}] XamlCheckProcessRequirements OK");
        }
        catch (Exception ex)
        {
            WriteLog($"[{DateTime.Now}] XamlCheckProcessRequirements warning: {ex.Message}");
        }

        WriteLog($"[{DateTime.Now}] Initializing ComWrappers...");
        WinRT.ComWrappersSupport.InitializeComWrappers();
        WriteLog($"[{DateTime.Now}] ComWrappers initialized");

        Application.Start((p) =>
        {
            WriteLog($"[{DateTime.Now}] Application.Start callback entered");
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            WriteLog($"[{DateTime.Now}] Instantiating App...");
            _ = new App();
            WriteLog($"[{DateTime.Now}] App instantiated successfully");
        });
    }
}

