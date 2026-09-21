using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace KsitalTelemetryHub.UI.WinUI.Services;

/// <summary>
/// Менеджер управления фоновой службой сбора данных:
/// Поддерживает как системную службу Windows (24/7), так и скрытый фоновый процесс-компаньон.
/// </summary>
public static class WorkerServiceManager
{
    private static Process? _standaloneWorkerProcess;
    private static readonly object _lock = new();

    public const string ServiceName = "KsitalTelemetryWorker";

    public static string? FindWorkerExePath()
    {
        string baseDir = AppContext.BaseDirectory;
        string[] candidates = new[]
        {
            Path.Combine(baseDir, "WorkerService", "Service.Worker.exe"),
            Path.Combine(baseDir, "Service.Worker.exe"),
            Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\Service.Worker\bin\Debug\net8.0\win-x64\Service.Worker.exe")),
            Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\Service.Worker\bin\Debug\net8.0\win-x64\Service.Worker.exe")),
            Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\..\src\Service.Worker\bin\Debug\net8.0\win-x64\Service.Worker.exe")),
            Path.GetFullPath(Path.Combine(baseDir, @"..\WorkerService\Service.Worker.exe"))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public static bool IsWindowsServiceInstalled()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query {ServiceName}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            });
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                return output.Contains($"SERVICE_NAME: {ServiceName}", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { }
        return false;
    }

    public static bool IsWindowsServiceRunning()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query {ServiceName}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            });
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { }
        return false;
    }

    public static async Task<bool> TryStartWindowsServiceAsync()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"start {ServiceName}",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            if (proc != null)
            {
                await proc.WaitForExitAsync();
            }

            for (int i = 0; i < 5; i++)
            {
                await Task.Delay(500);
                if (IsWindowsServiceRunning()) return true;
            }
        }
        catch { }
        return false;
    }

    public static async Task EnsureWorkerStartedAsync()
    {
        lock (_lock)
        {
            if (_standaloneWorkerProcess != null && !_standaloneWorkerProcess.HasExited)
            {
                return;
            }
        }

        // 1. Проверяем, запущена ли системная служба Windows
        if (IsWindowsServiceRunning())
        {
            return;
        }

        // 2. Если служба установлена, но не запущена, пробуем запустить ее
        if (IsWindowsServiceInstalled())
        {
            bool started = await TryStartWindowsServiceAsync();
            if (started) return;
        }

        // 3. Если служба Windows не установлена или не запустилась, проверяем, не запущен ли уже отдельный процесс
        var runningProcs = Process.GetProcessesByName("Service.Worker");
        if (runningProcs.Length > 0)
        {
            return;
        }

        // 4. Запускаем фоновый автономный процесс (скрыто, без консольного окна)
        string? workerExe = FindWorkerExePath();
        if (!string.IsNullOrEmpty(workerExe))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = workerExe,
                    WorkingDirectory = Path.GetDirectoryName(workerExe),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                lock (_lock)
                {
                    _standaloneWorkerProcess = Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                App.LogError("WorkerServiceManager.EnsureWorkerStartedAsync", ex);
            }
        }
    }

    public static void StopWorkerIfStandalone()
    {
        lock (_lock)
        {
            if (_standaloneWorkerProcess != null)
            {
                try
                {
                    if (!_standaloneWorkerProcess.HasExited)
                    {
                        _standaloneWorkerProcess.Kill(entireProcessTree: true);
                    }
                    _standaloneWorkerProcess.Dispose();
                }
                catch { }
                finally
                {
                    _standaloneWorkerProcess = null;
                }
            }
        }

        // Если служба Windows НЕ запущена, закрываем любые оставшиеся процессы Service.Worker,
        // чтобы они не висели в фоне после закрытия интерфейса
        if (!IsWindowsServiceRunning())
        {
            try
            {
                var procs = Process.GetProcessesByName("Service.Worker");
                foreach (var p in procs)
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }
        }
    }

    public static async Task RestartWorkerAsync()
    {
        if (IsWindowsServiceInstalled())
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c net stop {ServiceName} & net start {ServiceName}",
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                var p = Process.Start(psi);
                if (p != null) await p.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                App.LogError("WorkerServiceManager.RestartWorkerAsync(Service)", ex);
            }
        }
        else
        {
            StopWorkerIfStandalone();
            await Task.Delay(500);
            await EnsureWorkerStartedAsync();
        }
    }
}
