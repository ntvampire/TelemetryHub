using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Storage.Sqlite;
using KsitalTelemetryHub.UI.Avalonia.Services;
using KsitalTelemetryHub.UI.Avalonia.ViewModels;

namespace KsitalTelemetryHub.UI.Avalonia.Views;

public partial class SettingsView : UserControl
{
    public MainViewModel? ViewModel => DataContext as MainViewModel;

    public SettingsView()
    {
        InitializeComponent();
        AttachedToVisualTree += (s, e) =>
        {
            TxtBackupDir.Text = BackupService.GetBackupDirectory();
            TxtCurrentVersion.Text = $"Текущая версия: v{UpdateService.GetCurrentVersionString()}";
            LoadAvailableComPorts();
        };
    }

    private void LoadAvailableComPorts()
    {
        try
        {
            CmbComPorts.Items.Clear();
            var ports = SerialPort.GetPortNames().OrderBy(p => p).ToList();

            foreach (var port in ports)
            {
                CmbComPorts.Items.Add(port);
            }

            string? preferredPort = null;
            if (!string.IsNullOrWhiteSpace(ViewModel?.ComPortStatus) && ViewModel.ComPortStatus.StartsWith("COM: "))
            {
                preferredPort = ViewModel.ComPortStatus.Substring(5).Trim();
            }
            else
            {
                preferredPort = BackupService.LoadConfig().PreferredComPort;
            }

            if (!string.IsNullOrWhiteSpace(preferredPort) && CmbComPorts.Items.Contains(preferredPort))
            {
                CmbComPorts.SelectedItem = preferredPort;
            }
            else if (CmbComPorts.SelectedItem == null && CmbComPorts.Items.Count > 0)
            {
                CmbComPorts.SelectedIndex = 0;
            }

            TxtPortStatus.Text = $"Обнаружено портов в системе: {ports.Count}";
        }
        catch (Exception ex)
        {
            TxtPortStatus.Text = $"Ошибка сканирования портов: {ex.Message}";
        }
    }

    private void BtnRefreshPorts_Click(object? sender, RoutedEventArgs e)
    {
        LoadAvailableComPorts();
    }

    private async void BtnApplyPort_Click(object? sender, RoutedEventArgs e)
    {
        var selectedPort = CmbComPorts.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selectedPort))
        {
            TxtPortStatus.Text = "Выберите COM-порт из списка.";
            return;
        }

        try
        {
            var cfg = BackupService.LoadConfig();
            cfg.PreferredComPort = selectedPort;
            BackupService.SaveConfig(cfg);

            using var db = new AppDbContext(App.DatabasePath);
            var status = await db.SystemStatus.FirstOrDefaultAsync(s => s.Id == 1);
            if (status == null)
            {
                status = new SystemStatus
                {
                    Id = 1,
                    PortName = selectedPort,
                    RequestedPortName = selectedPort,
                    IsWorkerAlive = false,
                    IsModemConnected = false,
                    LastHeartbeat = DateTime.MinValue
                };
                db.SystemStatus.Add(status);
            }
            else
            {
                status.RequestedPortName = selectedPort;
            }

            await db.SaveChangesAsync();
            TxtPortStatus.Text = $"Команда на переключение на {selectedPort} передана службе сбора.";
            if (ViewModel != null) await ViewModel.RefreshDataAsync();
        }
        catch (Exception ex)
        {
            TxtPortStatus.Text = $"Ошибка применения порта: {ex.Message}";
        }
    }

    private async void BtnSelectBackupFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Выберите папку для резервных копий",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                var folderPath = folders[0].Path.LocalPath;
                BackupService.SetBackupDirectory(folderPath);
                TxtBackupDir.Text = folderPath;
                TxtBackupStatus.Text = $"Папка резервных копий изменена на: {folderPath}";
            }
        }
        catch (Exception ex)
        {
            TxtBackupStatus.Text = $"Ошибка выбора папки: {ex.Message}";
        }
    }

    private void BtnBackup_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var (filePath, totalCopies) = BackupService.CreateBackup(isDaily: false);
            TxtBackupStatus.Text = $"Резервная копия создана: {Path.GetFileName(filePath)} (в папке {totalCopies}/{BackupService.MaxBackupCopies} копий)";
        }
        catch (Exception ex)
        {
            TxtBackupStatus.Text = $"Ошибка создания копии: {ex.Message}";
        }
    }

    private async void BtnRestore_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите файл резервной копии базы данных",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("SQLite Backup (*.db, *.bak)")
                    {
                        Patterns = new[] { "*.db", "*.bak" }
                    }
                }
            });

            if (files.Count == 0) return;
            var filePath = files[0].Path.LocalPath;

            if (topLevel is Window ownerWindow)
            {
                bool confirmed = await ConfirmDialogWindow.ShowAsync(
                    ownerWindow,
                    "Восстановление базы данных",
                    $"Восстановить рабочую БД из «{Path.GetFileName(filePath)}»? Служба сбора данных будет кратковременно перезапущена.",
                    confirmText: "Восстановить",
                    isDangerous: true);
                if (!confirmed) return;
            }

            TxtBackupStatus.Text = "Проверка копии и остановка службы сбора данных...";
            await BackupService.RestoreBackupAsync(filePath);
            TxtBackupStatus.Text = $"База данных успешно восстановлена из: {Path.GetFileName(filePath)}";
            if (ViewModel != null) await ViewModel.RefreshDataAsync();
        }
        catch (Exception ex)
        {
            TxtBackupStatus.Text = $"Ошибка восстановления: {ex.Message}";
        }
    }

    private async void BtnCheckUpdates_Click(object? sender, RoutedEventArgs e)
    {
        BtnCheckUpdates.IsEnabled = false;
        TxtUpdateStatus.Text = "Проверка наличия обновлений на GitHub...";

        try
        {
            var update = await UpdateService.CheckForUpdatesAsync();

            if (update.HasUpdate && !string.IsNullOrWhiteSpace(update.DownloadUrl))
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    TxtUpdateStatus.Text = $"Доступен новый релиз {update.RemoteVersion}! Скачивание архива для Linux...";
                    var progress = new Progress<int>(percent =>
                    {
                        TxtUpdateStatus.Text = $"Загрузка {update.AssetName} ({update.RemoteVersion}): {percent}%...";
                    });
                    var archivePath = await UpdateService.DownloadInstallerAsync(update.DownloadUrl, update.AssetName, update.ChecksumsUrl, progress);
                    TxtUpdateStatus.Text = $"Архив обновления сохранён и проверен: {archivePath}. Распакуйте его для обновления.";
                    return;
                }

                TxtUpdateStatus.Text = $"Обнаружен новый релиз {update.RemoteVersion}! Скачивание...";
                var winProgress = new Progress<int>(percent =>
                {
                    TxtUpdateStatus.Text = $"Загрузка обновления {update.RemoteVersion}: {percent}%...";
                });

                var installerPath = await UpdateService.DownloadInstallerAsync(update.DownloadUrl, update.AssetName, update.ChecksumsUrl, winProgress);
                TxtUpdateStatus.Text = "Контрольная сумма проверена. Запуск установщика...";
                UpdateService.LaunchInstallerAndExit(installerPath);
            }
            else if (update.HasUpdate && !string.IsNullOrWhiteSpace(update.ReleasePageUrl))
            {
                TxtUpdateStatus.Text = $"Доступен релиз {update.RemoteVersion}. Страница загрузки: {update.ReleasePageUrl}";
                try
                {
                    Process.Start(new ProcessStartInfo(update.ReleasePageUrl) { UseShellExecute = true });
                }
                catch { }
            }
            else
            {
                TxtUpdateStatus.Text = $"У вас установлена актуальная версия Telemetry Hub (v{update.CurrentVersion}). Обновлений не найдено.";
            }
        }
        catch (Exception ex)
        {
            TxtUpdateStatus.Text = $"Ошибка проверки обновлений: {ex.Message}";
        }
        finally
        {
            BtnCheckUpdates.IsEnabled = true;
        }
    }

    private async void BtnImportExcel_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Импорт объектов из таблицы Excel / CSV",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Excel & CSV files (*.xlsx, *.csv)")
                    {
                        Patterns = new[] { "*.xlsx", "*.csv" }
                    }
                }
            });

            if (files.Count == 0) return;
            var filePath = files[0].Path.LocalPath;

            var imported = ImportExportService.ImportFromFile(filePath);
            if (imported.Count == 0)
            {
                TxtBackupStatus.Text = "В выбранном файле не найдено записей для импорта.";
                return;
            }

            var (added, updated) = await SaveImportedObjectsAsync(imported, "Основной участок");
            TxtBackupStatus.Text = $"Импорт завершен: добавлено {added}, обновлено {updated} (всего в файле: {imported.Count}).";
            if (ViewModel != null) await ViewModel.RefreshDataAsync();
        }
        catch (Exception ex)
        {
            TxtBackupStatus.Text = $"Ошибка импорта: {ex.Message}";
        }
    }

    private async void BtnImportGsmGuard_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Импорт объектов из файла GSMGuard",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("GSMGuard export file (*.txt)")
                    {
                        Patterns = new[] { "*.txt" }
                    }
                }
            });

            if (files.Count == 0) return;
            var filePath = files[0].Path.LocalPath;

            var imported = ImportExportService.ImportFromGsmGuard(filePath);
            if (imported.Count == 0)
            {
                TxtBackupStatus.Text = "В выбранном файле GSMGuard не найдено объектов.";
                return;
            }

            var (added, updated) = await SaveImportedObjectsAsync(imported, "Основной район");
            TxtBackupStatus.Text = $"Импорт GSMGuard завершен: добавлено {added}, обновлено {updated} (всего объектов в файле: {imported.Count}).";
            if (ViewModel != null) await ViewModel.RefreshDataAsync();
        }
        catch (Exception ex)
        {
            TxtBackupStatus.Text = $"Ошибка импорта GSMGuard: {ex.Message}";
        }
    }

    private static async Task<(int Added, int Updated)> SaveImportedObjectsAsync(List<ImportExportItem> imported, string defaultDistrict)
    {
        using var db = new AppDbContext(App.DatabasePath);
        int added = 0;
        int updated = 0;

        foreach (var target in imported)
        {
            string cleanPhone = PhoneNumber.Normalize(target.PhoneNumber);
            if (string.IsNullOrEmpty(cleanPhone)) continue;

            string rawPass = string.IsNullOrWhiteSpace(target.Password)
                ? DeviceCommandBuilder.GetDefaultPassword(target.DeviceType)
                : target.Password.Trim();
            string protectedPass = DevicePasswordProtector.Protect(rawPass);

            var existing = await db.Objects.FirstOrDefaultAsync(o => o.PhoneNumber == cleanPhone);
            if (existing != null)
            {
                if (!string.IsNullOrWhiteSpace(target.Name)) existing.Name = target.Name;
                if (!string.IsNullOrWhiteSpace(target.District)) existing.District = target.District;
                existing.DeviceType = target.DeviceType;
                if (!string.IsNullOrWhiteSpace(target.Password)) existing.DevicePassword = protectedPass;
                updated++;
            }
            else
            {
                db.Objects.Add(new MonitoredObject
                {
                    Name = string.IsNullOrWhiteSpace(target.Name) ? $"Объект {cleanPhone}" : target.Name,
                    PhoneNumber = cleanPhone,
                    District = string.IsNullOrWhiteSpace(target.District) ? defaultDistrict : target.District,
                    DeviceType = target.DeviceType,
                    DevicePassword = protectedPass
                });
                added++;
            }
        }

        await db.SaveChangesAsync();
        return (added, updated);
    }

    private async void BtnExportExcel_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Экспорт объектов в Excel",
                SuggestedFileName = $"Объекты_Телеметрия_{DateTime.Now:yyyyMMdd}",
                DefaultExtension = "xlsx",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Книга Excel (*.xlsx)")
                    {
                        Patterns = new[] { "*.xlsx" }
                    }
                }
            });

            if (file == null) return;
            var filePath = file.Path.LocalPath;

            using var db = new AppDbContext(App.DatabasePath);
            var objects = await db.Objects.AsNoTracking().ToListAsync();
            var items = objects.Select(o => new ImportExportItem
            {
                Id = o.Id.ToString(),
                District = o.District,
                Name = o.Name,
                PhoneNumber = o.PhoneNumber,
                DeviceType = o.DeviceType,
                Password = DevicePasswordProtector.Unprotect(o.DevicePassword)
            }).ToList();

            ImportExportService.ExportToExcel(filePath, items);
            TxtBackupStatus.Text = $"Экспорт завершен: сохранено {items.Count} объектов в {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            TxtBackupStatus.Text = $"Ошибка экспорта: {ex.Message}";
        }
    }
}
