using System;
using System.IO;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace KsitalTelemetryHub.UI.WinUI;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Console.WriteLine(">>> KsitalTelemetryHub Program.Main entered! <<<");
        var baseLog = Path.Combine(AppContext.BaseDirectory, "startup.log");
        var tempLog = Path.Combine(Path.GetTempPath(), "ksital_startup.log");
        void WriteLog(string msg)
        {
            Console.WriteLine(msg);
            try { File.AppendAllText(baseLog, msg + Environment.NewLine); } catch { }
            try { File.AppendAllText(tempLog, msg + Environment.NewLine); } catch { }
        }

        try
        {
            WriteLog($"[{DateTime.Now}] Program.Main started with args: {string.Join(" ", args)}");

            WinRT.ComWrappersSupport.InitializeComWrappers();
            WriteLog($"[{DateTime.Now}] ComWrappers initialized");

            Application.Start((p) =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                WriteLog($"[{DateTime.Now}] Starting App instance...");
                _ = new App();
            });
        }
        catch (Exception ex)
        {
            WriteLog($"[{DateTime.Now}] CRASH in Program.Main: {ex}");
            throw;
        }
    }
}
