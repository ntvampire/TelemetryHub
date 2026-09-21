using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KsitalTelemetryHub.UI.WinUI.ViewModels;
using KsitalTelemetryHub.UI.WinUI.Views;

namespace KsitalTelemetryHub.UI.WinUI;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private readonly DispatcherTimer _timer = new();

    public MainWindow()
    {
        this.InitializeComponent();
        this.Title = "Telemetry Hub";
        ViewModel = new MainViewModel();
        RootGrid.DataContext = ViewModel;

        // Настройка периодического опроса состояния (раз в 3 секунды)
        _timer.Interval = TimeSpan.FromSeconds(3);
        _timer.Tick += async (s, e) => await ViewModel.RefreshDataAsync();
        _timer.Start();

        // Автоматический запуск службы сбора (Windows Service 24/7 либо скрытый фоновый процесс)
        _ = Services.WorkerServiceManager.EnsureWorkerStartedAsync();

        // При закрытии главного окна завершаем автономный фоновый процесс
        this.Closed += (s, e) => Services.WorkerServiceManager.StopWorkerIfStandalone();

        // Установка иконки окна
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                this.AppWindow.SetIcon(iconPath);
            }
        }
        catch { }

        // Подписка на появление новых аварий / повторных оповещений
        ViewModel.NewAlarmArrived += alarm =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Services.AlarmManager.ShowAlarm(alarm, ViewModel);
            });
        };

        // Стартовая страница по умолчанию — Журнал событий
        ContentFrame.Navigate(typeof(AlarmsPage), ViewModel);
        if (NavView.MenuItems.Count > 0)
        {
            NavView.SelectedItem = NavView.MenuItems[0];
        }
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            ContentFrame.Navigate(typeof(SettingsPage), ViewModel);
            return;
        }

        if (args.InvokedItemContainer is NavigationViewItem item)
        {
            switch (item.Tag?.ToString())
            {
                case "dashboard":
                    ContentFrame.Navigate(typeof(DashboardPage), ViewModel);
                    break;
                case "alarms":
                    ContentFrame.Navigate(typeof(AlarmsPage), ViewModel);
                    break;
            }
        }
    }
}
