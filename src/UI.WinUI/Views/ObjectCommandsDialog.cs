using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Storage.Sqlite;
using KsitalTelemetryHub.UI.WinUI.Models;
using KsitalTelemetryHub.UI.WinUI.ViewModels;

namespace KsitalTelemetryHub.UI.WinUI.Views;

public class ObjectCommandsDialog : ContentDialog
{
    private readonly ObjectDisplayItem _item;
    private readonly MainViewModel _vm;

    private readonly InfoBar _infoBar = new()
    {
        IsOpen = false,
        Severity = InfoBarSeverity.Informational,
        Margin = new Thickness(0, 0, 0, 8)
    };

    public ObjectCommandsDialog(ObjectDisplayItem item, MainViewModel vm)
    {
        _item = item;
        _vm = vm;

        Title = $"Команды управления: {_item.Name}";
        CloseButtonText = "Закрыть";
        DefaultButton = ContentDialogButton.Close;

        // Расширяем максимальную ширину карточки диалога в теме (не ограничивая внешнее окно), чтобы оно оставалось по центру
        this.Resources["ContentDialogMaxWidth"] = 600.0;

        string devicePassword = string.IsNullOrWhiteSpace(_item.DevicePassword)
            ? DeviceCommandBuilder.GetDefaultPassword(_item.DeviceType)
            : _item.DevicePassword.Trim();

        var rootStack = new StackPanel
        {
            Spacing = 12,
            Width = 490,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // 1. Информационная карточка объекта (2 строки)
        var infoPanel = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var row1 = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 24
        };

        var row2 = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 24
        };

        var phoneBlock = new TextBlock
        {
            Text = $"Номер: {_item.PhoneNumber}",
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
            FontSize = 12
        };

        var districtBlock = new TextBlock
        {
            Text = $"Участок: {_item.District}",
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
            FontSize = 12
        };

        var typeBlock = new TextBlock
        {
            Text = $"Контроллер: {_item.DeviceTypeName}",
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
            FontSize = 12
        };

        var passBlock = new TextBlock
        {
            Text = $"Пароль: {devicePassword}",
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
            FontSize = 12
        };

        row1.Children.Add(phoneBlock);
        row1.Children.Add(districtBlock);

        row2.Children.Add(typeBlock);
        row2.Children.Add(passBlock);

        infoPanel.Children.Add(row1);
        infoPanel.Children.Add(row2);

        rootStack.Children.Add(_infoBar);
        rootStack.Children.Add(infoPanel);

        // 2. Список команд по категориям
        var commandsStack = new StackPanel { Spacing = 14 };
        var templates = DeviceCommandBuilder.GetTemplates(_item.DeviceType);

        var groupedTemplates = templates
            .GroupBy(t => t.Category)
            .ToList();

        foreach (var group in groupedTemplates)
        {
            var categoryHeader = new TextBlock
            {
                Text = group.Key,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 2)
            };
            commandsStack.Children.Add(categoryHeader);

            var itemsGrid = new StackPanel { Spacing = 6 };

            foreach (var template in group)
            {
                var cardBorder = new Border
                {
                    BorderThickness = new Thickness(1),
                    BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                    Background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Brush,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 8, 12, 8)
                };

                var cardGrid = new Grid();
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var textStack = new StackPanel { Spacing = 2 };
                var titleText = new TextBlock
                {
                    Text = template.Title,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 13
                };

                var descText = new TextBlock
                {
                    Text = template.Description,
                    FontSize = 11,
                    Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
                    TextWrapping = TextWrapping.Wrap
                };

                var payloadPreview = new TextBlock
                {
                    FontSize = 11,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = Application.Current.Resources["AccentTextFillColorPrimaryBrush"] as Brush,
                    Margin = new Thickness(0, 2, 0, 0)
                };

                string p = DeviceCommandBuilder.BuildPayload(template.Pattern, devicePassword);
                payloadPreview.Text = $"SMS: \"{p}\"";

                textStack.Children.Add(titleText);
                textStack.Children.Add(descText);
                textStack.Children.Add(payloadPreview);

                Grid.SetColumn(textStack, 0);
                cardGrid.Children.Add(textStack);

                // Кнопка отправки
                var sendBtn = new Button
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };

                var btnContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                btnContent.Children.Add(new FontIcon { Glyph = "\uE756", FontSize = 13 });
                btnContent.Children.Add(new TextBlock { Text = "Отправить", FontSize = 12 });
                sendBtn.Content = btnContent;

                ToolTipService.SetToolTip(sendBtn, $"Отправить команду «{template.Title}» на номер {_item.PhoneNumber}");

                sendBtn.Click += async (s, e) =>
                {
                    try
                    {
                        sendBtn.IsEnabled = false;
                        string payload = DeviceCommandBuilder.BuildPayload(template.Pattern, devicePassword);

                        await _vm.EnqueueCommandAsync(_item.Id, payload, $"{template.Title}: {template.Description}");

                        _infoBar.Severity = InfoBarSeverity.Success;
                        _infoBar.Title = "Команда отправлена";
                        _infoBar.Message = $"SMS-команда «{template.Title}» добавлена в очередь исходящих: {payload}";
                        _infoBar.IsOpen = true;
                    }
                    catch (Exception ex)
                    {
                        App.LogError("ObjectCommandsDialog.Send", ex);
                        _infoBar.Severity = InfoBarSeverity.Error;
                        _infoBar.Title = "Ошибка отправки";
                        _infoBar.Message = ex.Message;
                        _infoBar.IsOpen = true;
                    }
                    finally
                    {
                        sendBtn.IsEnabled = true;
                    }
                };

                Grid.SetColumn(sendBtn, 1);
                cardGrid.Children.Add(sendBtn);

                cardBorder.Child = cardGrid;
                itemsGrid.Children.Add(cardBorder);
            }

            commandsStack.Children.Add(itemsGrid);
        }

        var scrollViewer = new ScrollViewer
        {
            Content = commandsStack,
            MaxHeight = 440,
            Padding = new Thickness(0, 0, 12, 0),
            Margin = new Thickness(0, 8, 0, 0)
        };

        rootStack.Children.Add(scrollViewer);

        Content = rootStack;
    }
}
