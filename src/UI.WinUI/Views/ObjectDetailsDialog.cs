using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Storage.Sqlite;
using KsitalTelemetryHub.UI.WinUI.Models;
using KsitalTelemetryHub.UI.WinUI.ViewModels;

namespace KsitalTelemetryHub.UI.WinUI.Views;

public class ObjectDetailsDialog : ContentDialog
{
    private readonly ObjectDisplayItem _item;
    private readonly MainViewModel _vm;
    private readonly bool _isNew;

    private readonly TextBox _txtName = new() { Header = "Название объекта" };
    private readonly TextBox _txtPhone = new() { Header = "Номер телефона SIM-карты (+7...)" };
    private readonly TextBox _txtDistrict = new() { Header = "Район / Участок" };
    private readonly ComboBox _cmbType = new() { Header = "Тип контроллера" };

    public ObjectDetailsDialog(ObjectDisplayItem item, MainViewModel vm, bool isNew = false)
    {
        _item = item;
        _vm = vm;
        _isNew = isNew;

        Title = _isNew ? "Добавление объекта" : $"Параметры: {_item.Name}";
        PrimaryButtonText = "Сохранить";
        CloseButtonText = "Закрыть";
        DefaultButton = ContentDialogButton.Primary;

        _cmbType.Items.Add("КСИТАЛ GSM");
        _cmbType.Items.Add("CCU-825");
        _cmbType.Items.Add("ОВЕН ПЛК");
        _cmbType.SelectedIndex = (int)_item.DeviceType;

        _txtName.Text = _item.Name;
        _txtPhone.Text = _item.PhoneNumber;
        _txtDistrict.Text = _item.District;

        var stack = new StackPanel { Spacing = 12, Width = 380 };
        stack.Children.Add(_txtName);
        stack.Children.Add(_txtPhone);
        stack.Children.Add(_txtDistrict);
        stack.Children.Add(_cmbType);

        if (!_isNew)
        {
            var commandHeader = new TextBlock
            {
                Text = "Быстрые SMS-команды",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 0)
            };
            stack.Children.Add(commandHeader);

            var cmdPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var btnStatus = new Button { Content = "Запрос отчета" };
            btnStatus.Click += async (s, e) =>
            {
                await _vm.EnqueueCommandAsync(_item.Id, "?", "Запрос текущего состояния");
                this.Hide();
            };

            var btnRelay1 = new Button { Content = "Реле 1 Вкл" };
            btnRelay1.Click += async (s, e) =>
            {
                await _vm.EnqueueCommandAsync(_item.Id, "Relay 1 ON", "Включение реле 1");
                this.Hide();
            };

            cmdPanel.Children.Add(btnStatus);
            cmdPanel.Children.Add(btnRelay1);
            stack.Children.Add(cmdPanel);
        }

        Content = stack;
        PrimaryButtonClick += async (s, e) => await SaveObjectAsync();
    }

    private async Task SaveObjectAsync()
    {
        try
        {
            var phoneObj = new PhoneNumber(_txtPhone.Text.Trim());
            using var db = new AppDbContext(App.DatabasePath);

            if (_isNew)
            {
                var newObj = new MonitoredObject
                {
                    Name = _txtName.Text.Trim(),
                    PhoneNumber = phoneObj.Value,
                    District = _txtDistrict.Text.Trim(),
                    DeviceType = (DeviceType)_cmbType.SelectedIndex
                };
                db.Objects.Add(newObj);
            }
            else
            {
                var existing = await db.Objects.FirstOrDefaultAsync(o => o.Id == _item.Id);
                if (existing != null)
                {
                    existing.Name = _txtName.Text.Trim();
                    existing.PhoneNumber = phoneObj.Value;
                    existing.District = _txtDistrict.Text.Trim();
                    existing.DeviceType = (DeviceType)_cmbType.SelectedIndex;
                }
            }

            await db.SaveChangesAsync();
            await _vm.RefreshDataAsync();
        }
        catch (Exception ex)
        {
            // Показать ошибку валидации
            Title = $"Ошибка: {ex.Message}";
        }
    }
}
