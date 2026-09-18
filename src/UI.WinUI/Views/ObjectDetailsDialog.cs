using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
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

    private readonly InfoBar _infoBar = new()
    {
        IsOpen = false,
        Severity = InfoBarSeverity.Error,
        Margin = new Thickness(0, 0, 0, 8)
    };

    private readonly TextBox _txtName = new() { Header = "Название объекта" };
    private readonly TextBox _txtPhone = new() { Header = "Номер телефона SIM-карты (+7...)" };
    private readonly TextBox _txtDistrict = new() { Header = "Район / Участок" };
    private readonly TextBox _txtPassword = new() { Header = "Пароль устройства (GSM-код)" };
    private readonly ComboBox _cmbType = new() { Header = "Тип контроллера", HorizontalAlignment = HorizontalAlignment.Stretch };

    public ObjectDetailsDialog(ObjectDisplayItem item, MainViewModel vm, bool isNew = false)
    {
        _item = item;
        _vm = vm;
        _isNew = isNew;

        Title = _isNew ? "Добавление объекта" : $"Параметры: {_item.Name}";
        PrimaryButtonText = "Сохранить";
        SecondaryButtonText = _isNew ? "" : "Удалить";
        CloseButtonText = "Закрыть";
        DefaultButton = ContentDialogButton.Primary;

        _cmbType.Items.Add("КСИТАЛ GSM");
        _cmbType.Items.Add("CCU-825");
        _cmbType.Items.Add("ОВЕН ПЛК");
        _cmbType.SelectedIndex = Math.Clamp((int)_item.DeviceType, 0, 2);

        _txtName.Text = _item.Name;
        _txtPhone.Text = _item.PhoneNumber;
        _txtDistrict.Text = string.IsNullOrWhiteSpace(_item.District) ? "Основной участок" : _item.District;
        _txtPassword.Text = string.IsNullOrWhiteSpace(_item.DevicePassword) ? "00000" : _item.DevicePassword;

        var stack = new StackPanel { Spacing = 12, Width = 420 };
        stack.Children.Add(_infoBar);
        stack.Children.Add(_txtName);
        stack.Children.Add(_txtPhone);
        stack.Children.Add(_txtDistrict);
        stack.Children.Add(_txtPassword);
        stack.Children.Add(_cmbType);

        if (!_isNew)
        {
            var templates = DeviceCommandBuilder.GetTemplates(_item.DeviceType);
            if (templates.Count > 0)
            {
                var commandHeader = new TextBlock
                {
                    Text = $"Быстрые SMS-команды ({_item.DeviceTypeName})",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                stack.Children.Add(commandHeader);

                var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                for (int i = 0; i < templates.Count; i++)
                {
                    var template = templates[i];
                    var btn = new Button
                    {
                        Content = template.Title,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    ToolTipService.SetToolTip(btn, $"{template.Description}\nШаблон: {template.Pattern}");

                    btn.Click += async (s, e) =>
                    {
                        string password = _txtPassword.Text.Trim();
                        string payload = DeviceCommandBuilder.BuildPayload(template.Pattern, password);
                        await _vm.EnqueueCommandAsync(_item.Id, payload, template.Description);
                        this.Hide();
                    };

                    if (i % 2 == 0)
                    {
                        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    }
                    Grid.SetRow(btn, i / 2);
                    Grid.SetColumn(btn, i % 2);
                    grid.Children.Add(btn);
                }
                stack.Children.Add(grid);
            }
        }

        Content = new ScrollViewer { Content = stack, MaxHeight = 540 };

        PrimaryButtonClick += async (s, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                _infoBar.IsOpen = false;
                string rawName = _txtName.Text.Trim();
                if (string.IsNullOrWhiteSpace(rawName))
                {
                    _infoBar.Message = "Введите название объекта.";
                    _infoBar.IsOpen = true;
                    args.Cancel = true;
                    return;
                }

                string rawPhone = _txtPhone.Text.Trim();
                string cleanPhone = PhoneNumber.Normalize(rawPhone);
                if (string.IsNullOrWhiteSpace(cleanPhone) || cleanPhone.Length < 10)
                {
                    _infoBar.Message = "Введите корректный номер телефона (например, +79991234567).";
                    _infoBar.IsOpen = true;
                    args.Cancel = true;
                    return;
                }

                await SaveObjectAsync(cleanPhone, rawName);
            }
            catch (Exception ex)
            {
                App.LogError("ObjectDetailsDialog.Save", ex);
                string detail = ex.InnerException?.Message ?? ex.Message;
                _infoBar.Message = $"Ошибка сохранения: {detail}";
                _infoBar.IsOpen = true;
                args.Cancel = true;
            }
            finally
            {
                deferral.Complete();
            }
        };

        if (!_isNew)
        {
            SecondaryButtonClick += async (s, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    await _vm.DeleteObjectAsync(_item.Id);
                }
                finally
                {
                    deferral.Complete();
                }
            };
        }
    }

    private async Task SaveObjectAsync(string cleanPhone, string name)
    {
        using var db = new AppDbContext(App.DatabasePath);
        var devType = (DeviceType)Math.Clamp(_cmbType.SelectedIndex, 0, 2);
        string district = string.IsNullOrWhiteSpace(_txtDistrict.Text) ? "Основной участок" : _txtDistrict.Text.Trim();
        string password = string.IsNullOrWhiteSpace(_txtPassword.Text) ? "00000" : _txtPassword.Text.Trim();

        if (_isNew)
        {
            bool phoneExists = await db.Objects.AnyAsync(o => o.PhoneNumber == cleanPhone);
            if (phoneExists)
            {
                throw new InvalidOperationException($"Объект с номером {cleanPhone} уже существует в базе данных.");
            }

            var newObj = new MonitoredObject
            {
                Name = name,
                PhoneNumber = cleanPhone,
                District = district,
                DeviceType = devType,
                DevicePassword = password,
                CreatedAt = DateTime.UtcNow
            };
            db.Objects.Add(newObj);
        }
        else
        {
            bool phoneExists = await db.Objects.AnyAsync(o => o.PhoneNumber == cleanPhone && o.Id != _item.Id);
            if (phoneExists)
            {
                throw new InvalidOperationException($"Объект с номером {cleanPhone} уже существует в базе данных.");
            }

            var existing = await db.Objects.FirstOrDefaultAsync(o => o.Id == _item.Id);
            if (existing != null)
            {
                existing.Name = name;
                existing.PhoneNumber = cleanPhone;
                existing.District = district;
                existing.DeviceType = devType;
                existing.DevicePassword = password;
            }
        }

        await db.SaveChangesAsync();
        await _vm.RefreshDataAsync();
    }
}
