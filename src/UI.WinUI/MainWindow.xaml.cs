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
        ViewModel = new MainViewModel();

        // Настройка периодического опроса состояния (раз в 3 секунды)
        _timer.Interval = TimeSpan.FromSeconds(3);
        _timer.Tick += async (s, e) => await ViewModel.RefreshDataAsync();
        _timer.Start();

        // Стартовая страница
        ContentFrame.Navigate(typeof(DashboardPage), ViewModel);
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
