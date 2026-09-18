using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KsitalTelemetryHub.UI.WinUI.Models;
using KsitalTelemetryHub.UI.WinUI.ViewModels;

namespace KsitalTelemetryHub.UI.WinUI.Views;

public partial class DashboardPage : Page
{
    public MainViewModel ViewModel { get; set; } = null!;

    public DashboardPage()
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

    private void AutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.SearchText = sender.Text;
        }
    }

    private async void BtnManageObject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ObjectDisplayItem item)
        {
            var dialog = new ObjectDetailsDialog(item, ViewModel);
            dialog.XamlRoot = this.XamlRoot;
            await dialog.ShowAsync();
            await ViewModel.RefreshDataAsync();
        }
    }

    private async void BtnAddObject_Click(object sender, RoutedEventArgs e)
    {
        var newItem = new ObjectDisplayItem
        {
            Id = 0,
            Name = "",
            PhoneNumber = "+7",
            District = "Основной участок",
            DeviceType = Core.DeviceType.Ksital
        };
        var dialog = new ObjectDetailsDialog(newItem, ViewModel, isNew: true);
        dialog.XamlRoot = this.XamlRoot;
        await dialog.ShowAsync();
        await ViewModel.RefreshDataAsync();
    }
}
