using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using KsitalTelemetryHub.UI.Avalonia.Models;
using KsitalTelemetryHub.UI.Avalonia.ViewModels;

namespace KsitalTelemetryHub.UI.Avalonia.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private Window? GetParentWindow()
    {
        return (Window?)this.GetVisualRoot() 
            ?? (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    }

    private async void BtnAddObject_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var parent = GetParentWindow();
            if (parent != null)
            {
                var dialog = new ObjectDetailsWindow(new ObjectDisplayItem(), vm, isNew: true);
                await dialog.ShowDialog(parent);
            }
        }
    }

    private async void BtnManageObject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ObjectDisplayItem item && DataContext is MainViewModel vm)
        {
            var parent = GetParentWindow();
            if (parent != null)
            {
                var dialog = new ObjectDetailsWindow(item, vm, isNew: false);
                await dialog.ShowDialog(parent);
            }
        }
    }

    private async void BtnCommandsObject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ObjectDisplayItem item && DataContext is MainViewModel vm)
        {
            var parent = GetParentWindow();
            if (parent != null)
            {
                var dialog = new ObjectCommandsWindow(item, vm);
                await dialog.ShowDialog(parent);
            }
        }
    }
}
