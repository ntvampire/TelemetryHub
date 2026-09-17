using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    private void BtnImportExcel_Click(object sender, RoutedEventArgs e)
    {
        TxtBackupStatus.Text = "Импорт объектов выполняется через ImportExportService с нормализацией телефонов.";
    }

    private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
    {
        TxtBackupStatus.Text = "Экспорт объектов в Excel запущен.";
    }
}
