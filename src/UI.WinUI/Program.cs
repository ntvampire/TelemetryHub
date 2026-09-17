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
        var logPath = Path.Combine(AppContext.BaseDirectory, "startup.log");
        try
        {
            File.WriteAllText(logPath, $"[{DateTime.Now}] Program.Main started with args: {string.Join(" ", args)}\n");

            WinRT.ComWrappersSupport.InitializeComWrappers();
            File.AppendAllText(logPath, $"[{DateTime.Now}] ComWrappers initialized\n");

            Application.Start((p) =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                File.AppendAllText(logPath, $"[{DateTime.Now}] Starting App instance...\n");
                _ = new App();
            });
        }
        catch (Exception ex)
        {
            File.AppendAllText(logPath, $"[{DateTime.Now}] CRASH in Program.Main: {ex}\n");
            throw;
        }
    }
}
