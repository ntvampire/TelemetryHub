using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using KsitalTelemetryHub.UI.Avalonia.Services;
using KsitalTelemetryHub.UI.Avalonia.ViewModels;

namespace KsitalTelemetryHub.UI.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private readonly DispatcherTimer _timer = new();
    private readonly AlarmsView _alarmsView;
    private readonly DashboardView _dashboardView;
    private readonly SettingsView _settingsView;

    private static readonly IBrush ActiveNavBrush = new SolidColorBrush(Color.Parse("#2D2D2D"));
    private static readonly IBrush InactiveNavBrush = Brushes.Transparent;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel();
        DataContext = ViewModel;

        _alarmsView = new AlarmsView { DataContext = ViewModel };
        _dashboardView = new DashboardView { DataContext = ViewModel };
        _settingsView = new SettingsView { DataContext = ViewModel };

        // Стартовая страница — Журнал событий
        MainContent.Content = _alarmsView;

        // Периодический опрос состояния (раз в 3 секунды)
        _timer.Interval = TimeSpan.FromSeconds(3);
        _timer.Tick += async (s, e) => await ViewModel.RefreshDataAsync();
        _timer.Start();

        // Автоматический запуск службы сбора
        _ = WorkerServiceManager.EnsureWorkerStartedAsync();

        // Завершение автономного процесса службы при закрытии окна
        Closing += (s, e) => WorkerServiceManager.StopWorkerIfStandalone();

        // Подписка на появление новых аварий
        ViewModel.NewAlarmArrived += alarm =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                AlarmManager.ShowAlarm(alarm, ViewModel);
            });
        };

        Opened += async (s, e) =>
        {
            await ViewModel.InitializeAsync();
        };
    }

    private void BtnNavAlarms_Click(object? sender, RoutedEventArgs e)
    {
        MainContent.Content = _alarmsView;
        BtnNavAlarms.Background = ActiveNavBrush;
        BtnNavDashboard.Background = InactiveNavBrush;
        BtnNavSettings.Background = InactiveNavBrush;
    }

    private void BtnNavDashboard_Click(object? sender, RoutedEventArgs e)
    {
        MainContent.Content = _dashboardView;
        BtnNavAlarms.Background = InactiveNavBrush;
        BtnNavDashboard.Background = ActiveNavBrush;
        BtnNavSettings.Background = InactiveNavBrush;
    }

    private void BtnNavSettings_Click(object? sender, RoutedEventArgs e)
    {
        MainContent.Content = _settingsView;
        BtnNavAlarms.Background = InactiveNavBrush;
        BtnNavDashboard.Background = InactiveNavBrush;
        BtnNavSettings.Background = ActiveNavBrush;
    }
}
