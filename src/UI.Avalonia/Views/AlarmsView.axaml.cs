using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using KsitalTelemetryHub.UI.Avalonia.Models;
using KsitalTelemetryHub.UI.Avalonia.Services;
using KsitalTelemetryHub.UI.Avalonia.ViewModels;

namespace KsitalTelemetryHub.UI.Avalonia.Views;

public partial class AlarmsView : UserControl
{
    public AlarmsView()
    {
        InitializeComponent();
    }

    private async void BtnAcknowledge_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AlarmDisplayItem alarm && DataContext is MainViewModel vm)
        {
            await AlarmManager.AcknowledgeAlarmAsync(alarm, vm);
        }
    }
}
