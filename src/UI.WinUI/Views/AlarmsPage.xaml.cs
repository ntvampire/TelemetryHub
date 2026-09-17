using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KsitalTelemetryHub.UI.WinUI.ViewModels;

namespace KsitalTelemetryHub.UI.WinUI.Views;

public partial class AlarmsPage : Page
{
    public MainViewModel ViewModel { get; set; } = null!;

    public AlarmsPage()
    {
        this.InitializeComponent();
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is MainViewModel vm)
        {
            ViewModel = vm;
            await ViewModel.RefreshDataAsync();
        }
    }

    private async void BtnAcknowledge_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int alarmId)
        {
            await ViewModel.AcknowledgeAlarmAsync(alarmId);
        }
    }
}
