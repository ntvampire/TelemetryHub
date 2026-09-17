using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace KsitalTelemetryHub.UI.WinUI;

public static class Program
{
    private static readonly string BaseLog = Path.Combine(AppContext.BaseDirectory, "startup.log");
    private static readonly string TempLog = Path.Combine(Path.GetTempPath(), "telemetry_startup.log");

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    private const int ATTACH_PARENT_PROCESS = -1;
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_ERROR_HANDLE = -12;

    public static void WriteLog(string msg)
    {
        Console.WriteLine(msg);
        try { File.AppendAllText(BaseLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}{Environment.NewLine}"); } catch { }
        try { File.AppendAllText(TempLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}{Environment.NewLine}"); } catch { }
    }

    [DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            if (AttachConsole(ATTACH_PARENT_PROCESS))
            {
                var stdOut = GetStdHandle(STD_OUTPUT_HANDLE);
                if (stdOut != IntPtr.Zero && stdOut != new IntPtr(-1))
                {
                    var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdOut, ownsHandle: false);
                    var writer = new StreamWriter(new FileStream(safeHandle, FileAccess.Write)) { AutoFlush = true };
                    Console.SetOut(writer);
                    Console.SetError(writer);
                }
            }
        }
        catch { }

        WriteLog(">>> Telemetry Hub Program.Main entered! <<<");
        WriteLog($"Starting with args: {string.Join(" ", args)}");

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

