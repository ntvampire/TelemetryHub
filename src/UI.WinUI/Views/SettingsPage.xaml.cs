using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Storage.Sqlite;
using KsitalTelemetryHub.UI.WinUI.Services;
using KsitalTelemetryHub.UI.WinUI.ViewModels;

namespace KsitalTelemetryHub.UI.WinUI.Views;

public partial class SettingsPage : Page
{
    public MainViewModel ViewModel { get; set; } = null!;
    public string WorkingDbPath => App.DatabasePath;

    public SettingsPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is MainViewModel vm)
        {
            ViewModel = vm;
        }

        TxtBackupDir.Text = BackupService.GetBackupDirectory();
        TxtCurrentVersion.Text = $"Текущая версия: v{UpdateService.GetCurrentVersionString()}";

        LoadAvailableComPorts();
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

            // Попытка выделить текущий порт из статуса
            if (!string.IsNullOrWhiteSpace(ViewModel?.ComPortStatus) && ViewModel.ComPortStatus.StartsWith("COM: "))
            {
                var activePort = ViewModel.ComPortStatus.Substring(5).Trim();
                if (CmbComPorts.Items.Contains(activePort))
                {
                    CmbComPorts.SelectedItem = activePort;
                }
            }

            if (CmbComPorts.SelectedItem == null && CmbComPorts.Items.Count > 0)
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

    private void BtnRefreshPorts_Click(object sender, RoutedEventArgs e)
    {
        LoadAvailableComPorts();
    }

    private async void BtnApplyPort_Click(object sender, RoutedEventArgs e)
    {
        var selectedPort = CmbComPorts.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selectedPort))
        {
            TxtPortStatus.Text = "Выберите COM-порт из списка.";
            return;
        }

        try
        {
            using var db = new AppDbContext(App.DatabasePath);
            var status = await db.SystemStatus.FirstOrDefaultAsync(s => s.Id == 1);
            if (status != null)
            {
                status.RequestedPortName = selectedPort;
                await db.SaveChangesAsync();
                TxtPortStatus.Text = $"Команда на переключение на {selectedPort} передана службе сбора.";
                if (ViewModel != null) await ViewModel.RefreshDataAsync();
            }
            else
            {
                TxtPortStatus.Text = "Статус службы не найден в базе данных.";
            }
        }
        catch (Exception ex)
        {
            TxtPortStatus.Text = $"Ошибка применения порта: {ex.Message}";
        }
    }

    private async void BtnSelectBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                BackupService.SetBackupDirectory(folder.Path);
                TxtBackupDir.Text = folder.Path;
                TxtBackupStatus.Text = $"Папка резервных копий изменена на: {folder.Path}";
            }
        }
        catch (Exception ex)
        {
            TxtBackupStatus.Text = $"Ошибка выбора папки: {ex.Message}";
        }
    }

    private void BtnBackup_Click(object sender, RoutedEventArgs e)
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

    private async void BtnRestore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeFilter.Add(".db");
            picker.FileTypeFilter.Add(".bak");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var dialog = new ContentDialog
            {
                Title = "Подтверждение восстановления базы",
                Content = $"Восстановление из файла \"{file.Name}\" перезапишет текущую рабочую базу данных.\nПеред заменой автоматически создается страховочная копия.\n\nПродолжить восстановление?",
                PrimaryButtonText = "Восстановить",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var res = await dialog.ShowAsync();
            if (res == ContentDialogResult.Primary)
            {
                BackupService.RestoreBackup(file.Path);
                TxtBackupStatus.Text = $"База данных успешно восстановлена из: {file.Name}";
                if (ViewModel != null) await ViewModel.RefreshDataAsync();
            }
        }
        catch (Exception ex)
        {
            TxtBackupStatus.Text = $"Ошибка восстановления: {ex.Message}";
        }
    }

    private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        BtnCheckUpdates.IsEnabled = false;
        PrgUpdate.Visibility = Visibility.Visible;
        PrgUpdate.IsActive = true;
        TxtUpdateStatus.Text = "Проверка наличия обновлений на GitHub...";

        try
        {
            var (hasUpdate, currentVer, remoteVer, notes, downloadUrl) = await UpdateService.CheckForUpdatesAsync();

            if (hasUpdate && !string.IsNullOrWhiteSpace(downloadUrl))
            {
                TxtUpdateStatus.Text = $"Обнаружен новый релиз {remoteVer}!";

                var dialog = new ContentDialog
                {
                    Title = $"Доступно обновление Telemetry Hub {remoteVer}",
                    Content = new ScrollViewer
                    {
                        MaxHeight = 320,
                        Content = new TextBlock
                        {
                            Text = $"Установлена версия: v{currentVer}\nНовая версия: {remoteVer}\n\nИзменения в релизе:\n{notes ?? "Плановое обновление стабильности и функций."}\n\nХотите скачать и установить обновление сейчас?",
                            TextWrapping = TextWrapping.Wrap
                        }
                    },
                    PrimaryButtonText = "Обновить сейчас",
                    CloseButtonText = "Отложить",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    TxtUpdateStatus.Text = "Загрузка установочного пакета обновления...";
                    var progress = new Progress<int>(percent =>
                    {
                        TxtUpdateStatus.Text = $"Загрузка обновления: {percent}%...";
                    });

                    var installerPath = await UpdateService.DownloadInstallerAsync(downloadUrl, progress);
                    TxtUpdateStatus.Text = "Запуск установщика...";
                    UpdateService.LaunchInstallerAndExit(installerPath);
                }
            }
            else
            {
                TxtUpdateStatus.Text = $"У вас установлена актуальная версия Telemetry Hub (v{currentVer}). Обновлений не найдено.";
            }
        }
        catch (Exception ex)
        {
            TxtUpdateStatus.Text = $"Ошибка проверки обновлений: {ex.Message}";
        }
        finally
        {
            PrgUpdate.IsActive = false;
            PrgUpdate.Visibility = Visibility.Collapsed;
            BtnCheckUpdates.IsEnabled = true;
        }
    }

    private async void BtnImportExcel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeFilter.Add(".xlsx");
            picker.FileTypeFilter.Add(".csv");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var imported = ImportExportService.ImportFromFile(file.Path);
            if (imported.Count == 0)
            {
                TxtBackupStatus.Text = "В выбранном файле не найдено записей для импорта.";
                return;
            }

            using var db = new AppDbContext(App.DatabasePath);
            int added = 0;
            int updated = 0;

            foreach (var target in imported)
            {
                string cleanPhone = PhoneNumber.Normalize(target.PhoneNumber);
                if (string.IsNullOrEmpty(cleanPhone)) continue;

                var existing = await db.Objects.FirstOrDefaultAsync(o => o.PhoneNumber == cleanPhone);
                if (existing != null)
                {
                    if (!string.IsNullOrWhiteSpace(target.Name)) existing.Name = target.Name;
                    if (!string.IsNullOrWhiteSpace(target.District)) existing.District = target.District;
                    existing.DeviceType = target.DeviceType;
                    if (!string.IsNullOrWhiteSpace(target.Password)) existing.DevicePassword = target.Password;
                    updated++;
                }
                else
                {
                    db.Objects.Add(new MonitoredObject
                    {
                        Name = string.IsNullOrWhiteSpace(target.Name) ? $"Объект {cleanPhone}" : target.Name,
                        PhoneNumber = cleanPhone,
                        District = string.IsNullOrWhiteSpace(target.District) ? "Основной участок" : target.District,
                        DeviceType = target.DeviceType,
                        DevicePassword = string.IsNullOrWhiteSpace(target.Password) ? DeviceCommandBuilder.GetDefaultPassword(target.DeviceType) : target.Password
                    });
                    added++;
                }
            }

            await db.SaveChangesAsync();
            TxtBackupStatus.Text = $"Импорт завершен: добавлено {added}, обновлено {updated} (всего в файле: {imported.Count}).";
            if (ViewModel != null) await ViewModel.RefreshDataAsync();
        }
        catch (Exception ex)
        {
            TxtBackupStatus.Text = $"Ошибка импорта: {ex.Message}";
        }
    }

    private async void BtnImportGsmGuard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeFilter.Add(".txt");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var imported = ImportExportService.ImportFromGsmGuard(file.Path);
            if (imported.Count == 0)
            {
                TxtBackupStatus.Text = "В выбранном файле GSMGuard не найдено объектов.";
                return;
            }

            using var db = new AppDbContext(App.DatabasePath);
            int added = 0;
            int updated = 0;

            foreach (var target in imported)
            {
                string cleanPhone = PhoneNumber.Normalize(target.PhoneNumber);
                if (string.IsNullOrEmpty(cleanPhone)) continue;

                var existing = await db.Objects.FirstOrDefaultAsync(o => o.PhoneNumber == cleanPhone);
                if (existing != null)
                {
                    if (!string.IsNullOrWhiteSpace(target.Name)) existing.Name = target.Name;
                    if (!string.IsNullOrWhiteSpace(target.District)) existing.District = target.District;
                    existing.DeviceType = target.DeviceType;
                    if (!string.IsNullOrWhiteSpace(target.Password)) existing.DevicePassword = target.Password;
                    updated++;
                }
                else
                {
                    db.Objects.Add(new MonitoredObject
                    {
                        Name = string.IsNullOrWhiteSpace(target.Name) ? $"Объект {cleanPhone}" : target.Name,
                        PhoneNumber = cleanPhone,
                        District = string.IsNullOrWhiteSpace(target.District) ? "Основной район" : target.District,
                        DeviceType = target.DeviceType,
                        DevicePassword = string.IsNullOrWhiteSpace(target.Password) ? DeviceCommandBuilder.GetDefaultPassword(target.DeviceType) : target.Password
                    });
                    added++;
                }
            }

            await db.SaveChangesAsync();
            TxtBackupStatus.Text = $"Импорт GSMGuard завершен: добавлено {added}, обновлено {updated} (всего объектов в файле: {imported.Count}).";
            if (ViewModel != null) await ViewModel.RefreshDataAsync();
        }
        catch (Exception ex)
        {
            TxtBackupStatus.Text = $"Ошибка импорта GSMGuard: {ex.Message}";
        }
    }

    private async void BtnExportExcel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Книга Excel", new List<string> { ".xlsx" });
            picker.SuggestedFileName = $"Объекты_Телеметрия_{DateTime.Now:yyyyMMdd}";

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            using var db = new AppDbContext(App.DatabasePath);
            var objects = await db.Objects.AsNoTracking().ToListAsync();
            var items = objects.Select(o => new ImportExportItem
            {
                Id = o.Id.ToString(),
                District = o.District,
                Name = o.Name,
                PhoneNumber = o.PhoneNumber,
                DeviceType = o.DeviceType,
                Password = o.DevicePassword
            }).ToList();

            ImportExportService.ExportToExcel(file.Path, items);
            TxtBackupStatus.Text = $"Экспорт завершен: сохранено {items.Count} объектов в {file.Name}";
        }
        catch (Exception ex)
        {
            TxtBackupStatus.Text = $"Ошибка экспорта: {ex.Message}";
        }
    }
}
