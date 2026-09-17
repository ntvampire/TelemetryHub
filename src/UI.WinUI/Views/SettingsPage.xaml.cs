using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Storage.Sqlite;
using KsitalTelemetryHub.UI.WinUI.ViewModels;

namespace KsitalTelemetryHub.UI.WinUI.Views;

public partial class SettingsPage : Page
{
    public MainViewModel ViewModel { get; set; } = null!;

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
    }

    private void BtnBackup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dbPath = App.DatabasePath;
            if (File.Exists(dbPath))
            {
                var backupDir = Path.Combine(AppContext.BaseDirectory, "backups");
                Directory.CreateDirectory(backupDir);
                var destPath = Path.Combine(backupDir, $"telemetry_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db");
                File.Copy(dbPath, destPath, true);
                TxtBackupStatus.Text = $"Бэкап успешно сохранен в: {destPath}";
            }
            else
            {
                TxtBackupStatus.Text = "Файл базы данных не найден для бэкапа.";
            }
        }
        catch (Exception ex)
        {
            TxtBackupStatus.Text = $"Ошибка бэкапа: {ex.Message}";
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
                        DevicePassword = string.IsNullOrWhiteSpace(target.Password) ? "00000" : target.Password
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
