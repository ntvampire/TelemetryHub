using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CornerRadius = global::Avalonia.CornerRadius;
using Thickness = global::Avalonia.Thickness;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.UI.Avalonia.Models;
using KsitalTelemetryHub.UI.Avalonia.ViewModels;

namespace KsitalTelemetryHub.UI.Avalonia.Views;

public partial class ObjectCommandsWindow : Window
{
    private readonly ObjectDisplayItem _item;
    private readonly MainViewModel _vm;

    public ObjectCommandsWindow() : this(new ObjectDisplayItem(), new MainViewModel())
    {
    }

    public ObjectCommandsWindow(ObjectDisplayItem item, MainViewModel vm)
    {
        InitializeComponent();
        _item = item;
        _vm = vm;

        Title = $"Команды управления: {_item.Name}";
        TxtTitle.Text = $"Команды управления: {_item.Name}";

        string devicePassword = string.IsNullOrWhiteSpace(_item.DevicePassword)
            ? DeviceCommandBuilder.GetDefaultPassword(_item.DeviceType)
            : _item.DevicePassword.Trim();

        TxtPhone.Text = $"Номер: {_item.PhoneNumber}";
        TxtDistrict.Text = $"Участок: {_item.District}";
        TxtDeviceType.Text = $"Контроллер: {_item.DeviceTypeName}";
        TxtPassword.Text = $"Пароль: {devicePassword}";

        BuildCommandsList(devicePassword);
    }

    private void BuildCommandsList(string devicePassword)
    {
        PnlCommands.Children.Clear();
        var templates = DeviceCommandBuilder.GetTemplates(_item.DeviceType);
        var grouped = templates.GroupBy(t => t.Category).ToList();

        foreach (var group in grouped)
        {
            var categoryHeader = new TextBlock
            {
                Text = group.Key,
                FontWeight = FontWeight.SemiBold,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.Parse("#64B5F6")),
                Margin = new Thickness(0, 6, 0, 2)
            };
            PnlCommands.Children.Add(categoryHeader);

            foreach (var template in group)
            {
                var cardBorder = new Border
                {
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.Parse("#383838")),
                    Background = new SolidColorBrush(Color.Parse("#252526")),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 10)
                };

                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto")
                };

                var textStack = new StackPanel { Spacing = 3 };
                var titleText = new TextBlock
                {
                    Text = template.Title,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 13
                };

                var descText = new TextBlock
                {
                    Text = template.Description,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.Parse("#9E9E9E")),
                    TextWrapping = TextWrapping.Wrap
                };

                string p = DeviceCommandBuilder.BuildPayload(template.Pattern, devicePassword);
                var payloadPreview = new TextBlock
                {
                    FontSize = 11,
                    FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                    Foreground = new SolidColorBrush(Color.Parse("#2ECC71")),
                    Text = $"SMS: \"{p}\"",
                    Margin = new Thickness(0, 2, 0, 0)
                };

                textStack.Children.Add(titleText);
                textStack.Children.Add(descText);
                textStack.Children.Add(payloadPreview);

                Grid.SetColumn(textStack, 0);
                grid.Children.Add(textStack);

                var sendBtn = new Button
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0),
                    Padding = new Thickness(12, 6)
                };

                var btnContent = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6
                };
                btnContent.Children.Add(new TextBlock { Text = "⚡", FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
                btnContent.Children.Add(new TextBlock { Text = "Отправить", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                sendBtn.Content = btnContent;

                sendBtn.Click += async (s, e) =>
                {
                    try
                    {
                        sendBtn.IsEnabled = false;
                        string payload = DeviceCommandBuilder.BuildPayload(template.Pattern, devicePassword);
                        await _vm.EnqueueCommandAsync(_item.Id, payload, $"{template.Title}: {template.Description}");

                        BrdStatus.Background = new SolidColorBrush(Color.Parse("#1B5E20"));
                        TxtStatus.Text = $"SMS-команда «{template.Title}» добавлена в очередь исходящих: {payload}";
                        BrdStatus.IsVisible = true;
                    }
                    catch (Exception ex)
                    {
                        BrdStatus.Background = new SolidColorBrush(Color.Parse("#B71C1C"));
                        TxtStatus.Text = $"Ошибка отправки: {ex.Message}";
                        BrdStatus.IsVisible = true;
                    }
                    finally
                    {
                        sendBtn.IsEnabled = true;
                    }
                };

                Grid.SetColumn(sendBtn, 1);
                grid.Children.Add(sendBtn);

                cardBorder.Child = grid;
                PnlCommands.Children.Add(cardBorder);
            }
        }
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
