using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Storage.Sqlite;
using KsitalTelemetryHub.UI.Avalonia.Models;
using KsitalTelemetryHub.UI.Avalonia.ViewModels;

namespace KsitalTelemetryHub.UI.Avalonia.Views;

public partial class ObjectDetailsWindow : Window
{
    private readonly ObjectDisplayItem _item;
    private readonly MainViewModel _vm;
    private readonly bool _isNew;

    public ObjectDetailsWindow() : this(new ObjectDisplayItem(), new MainViewModel(), false)
    {
    }

    public ObjectDetailsWindow(ObjectDisplayItem item, MainViewModel vm, bool isNew = false)
    {
        InitializeComponent();
        _item = item;
        _vm = vm;
        _isNew = isNew;

        Title = _isNew ? "Добавление объекта" : $"Параметры: {_item.Name}";
        TxtHeader.Text = Title;
        BtnDelete.IsVisible = !_isNew;

        CmbType.SelectedIndex = Math.Clamp((int)_item.DeviceType, 0, 2);

        TxtName.Text = _item.Name;
        TxtPhone.Text = _item.PhoneNumber;
        TxtDistrict.Text = string.IsNullOrWhiteSpace(_item.District) ? "Основной участок" : _item.District;
        TxtPassword.Text = string.IsNullOrWhiteSpace(_item.DevicePassword)
            ? DeviceCommandBuilder.GetDefaultPassword(_item.DeviceType)
            : _item.DevicePassword;

        CmbType.SelectionChanged += (s, e) =>
        {
            if (CmbType.SelectedIndex >= 0)
            {
                var selectedType = (DeviceType)CmbType.SelectedIndex;
                var defaultPass = DeviceCommandBuilder.GetDefaultPassword(selectedType);
                if (_isNew || string.IsNullOrWhiteSpace(TxtPassword.Text) || IsKnownDefaultPassword(TxtPassword.Text.Trim()))
                {
                    TxtPassword.Text = defaultPass;
                }
            }
        };
    }

    private async void BtnSave_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            BrdError.IsVisible = false;
            string rawName = TxtName.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(rawName))
            {
                ShowError("Введите название объекта.");
                return;
            }

            string rawPhone = TxtPhone.Text?.Trim() ?? "";
            string cleanPhone = PhoneNumber.Normalize(rawPhone);
            if (string.IsNullOrWhiteSpace(cleanPhone) || cleanPhone.Length < 10)
            {
                ShowError("Введите корректный номер телефона (например, +79991234567).");
                return;
            }

            await SaveObjectAsync(cleanPhone, rawName);
            Close();
        }
        catch (Exception ex)
        {
            App.LogError("ObjectDetailsWindow.Save", ex);
            string detail = ex.InnerException?.Message ?? ex.Message;
            ShowError($"Ошибка сохранения: {detail}");
        }
    }

    private async void BtnDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (_isNew) return;
        try
        {
            await _vm.DeleteObjectAsync(_item.Id);
            Close();
        }
        catch (Exception ex)
        {
            App.LogError("ObjectDetailsWindow.Delete", ex);
            ShowError($"Ошибка удаления: {ex.Message}");
        }
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ShowError(string message)
    {
        TxtError.Text = message;
        BrdError.IsVisible = true;
    }

    private async Task SaveObjectAsync(string cleanPhone, string name)
    {
        using var db = new AppDbContext(App.DatabasePath);
        var devType = (DeviceType)Math.Clamp(CmbType.SelectedIndex, 0, 2);
        string district = string.IsNullOrWhiteSpace(TxtDistrict.Text) ? "Основной участок" : TxtDistrict.Text.Trim();
        string password = string.IsNullOrWhiteSpace(TxtPassword.Text)
            ? DeviceCommandBuilder.GetDefaultPassword(devType)
            : TxtPassword.Text.Trim();

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

            _item.Name = name;
            _item.PhoneNumber = cleanPhone;
            _item.District = district;
            _item.DeviceType = devType;
            _item.DevicePassword = password;
        }

        await db.SaveChangesAsync();
        await _vm.RefreshDataAsync();
    }

    private static bool IsKnownDefaultPassword(string pass) =>
        pass == "00000" || pass == "pass" || pass == "0000" || pass == "1234";
}
