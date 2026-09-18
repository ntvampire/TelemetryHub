using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Xaml;
using WinUIEx;
using KsitalTelemetryHub.UI.WinUI.Models;
using KsitalTelemetryHub.UI.WinUI.Services;
using KsitalTelemetryHub.UI.WinUI.ViewModels;

namespace KsitalTelemetryHub.UI.WinUI.Views;

public partial class AlarmWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly List<AlarmDisplayItem> _alarms = new();
    private int _currentIndex = 0;
    private bool _isActionHandled = false;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public void BringToFront()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ShowWindow(hwnd, 9); // SW_RESTORE
            SetForegroundWindow(hwnd);
            SetWindowPos(hwnd, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, 0x0001 | 0x0002);
        }
        catch { }
    }

    public AlarmWindow(AlarmDisplayItem initialAlarm, MainViewModel vm)
    {
        this.InitializeComponent();
        _vm = vm;
        _alarms.Add(initialAlarm);

        this.Title = $"ВНИМАНИЕ: ТРЕВОГА! — {initialAlarm.ObjectName}";
        this.AppWindow.Resize(new Windows.Graphics.SizeInt32(580, 500));
        this.CenterOnScreen();
        this.SetIsAlwaysOnTop(true);

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
            {
                this.AppWindow.SetIcon(iconPath);
            }
        }
        catch { }

        this.Closed += async (s, e) =>
        {
            if (!_isActionHandled)
            {
                // Закрытие окна крестиком или Alt+F4 -> откладывание всех неподтвержденных на 5 минут
                foreach (var a in _alarms.ToArray())
                {
                    await AlarmManager.SnoozeAlarmAsync(a, 5, "при закрытии окна крестиком");
                }
                await _vm.RefreshDataAsync();
            }
        };

        UpdateDisplay();
    }

    public void AddAlarm(AlarmDisplayItem alarm)
    {
        if (!_alarms.Exists(a => a.Id == alarm.Id))
        {
            _alarms.Add(alarm);
            _currentIndex = _alarms.Count - 1; // Показываем свежую
            UpdateDisplay();
        }
    }

    private void UpdateDisplay()
    {
        if (_alarms.Count == 0)
        {
            _isActionHandled = true;
            this.Close();
            return;
        }

        if (_currentIndex < 0) _currentIndex = 0;
        if (_currentIndex >= _alarms.Count) _currentIndex = _alarms.Count - 1;

        var current = _alarms[_currentIndex];
        this.Title = $"ВНИМАНИЕ: ТРЕВОГА! — {current.ObjectName}";
        TxtObjectName.Text = current.ObjectName;
        TxtDistrict.Text = current.District;
        TxtPhoneNumber.Text = current.PhoneNumber;
        TxtDeviceType.Text = current.DeviceTypeName;
        TxtAlarmDesc.Text = current.Description;
        TxtTimestamp.Text = current.TimestampFormatted;

        if (_alarms.Count > 1)
        {
            BrdAlarmCount.Visibility = Visibility.Visible;
            TxtAlarmCount.Text = $"{_currentIndex + 1} из {_alarms.Count}";
            GridPager.Visibility = Visibility.Visible;
            TxtPagerInfo.Text = $"Тревога {_currentIndex + 1} из {_alarms.Count}";
            BtnPrevAlarm.IsEnabled = _currentIndex > 0;
            BtnNextAlarm.IsEnabled = _currentIndex < _alarms.Count - 1;
        }
        else
        {
            BrdAlarmCount.Visibility = Visibility.Collapsed;
            GridPager.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnPrevAlarm_Click(object sender, RoutedEventArgs e)
    {
        if (_currentIndex > 0)
        {
            _currentIndex--;
            UpdateDisplay();
        }
    }

    private void BtnNextAlarm_Click(object sender, RoutedEventArgs e)
    {
        if (_currentIndex < _alarms.Count - 1)
        {
            _currentIndex++;
            UpdateDisplay();
        }
    }

    private async void BtnAcknowledge_Click(object sender, RoutedEventArgs e)
    {
        if (_alarms.Count == 0) return;
        var current = _alarms[_currentIndex];
        _alarms.RemoveAt(_currentIndex);

        await AlarmManager.AcknowledgeAlarmAsync(current, _vm);

        if (_alarms.Count == 0)
        {
            _isActionHandled = true;
            this.Close();
        }
        else
        {
            UpdateDisplay();
        }
    }

    private async void BtnSnooze_Click(object sender, RoutedEventArgs e)
    {
        if (_alarms.Count == 0) return;
        var current = _alarms[_currentIndex];
        _alarms.RemoveAt(_currentIndex);

        await AlarmManager.SnoozeAlarmAsync(current, 5, "по кнопке «Отложить»");
        await _vm.RefreshDataAsync();

        if (_alarms.Count == 0)
        {
            _isActionHandled = true;
            this.Close();
        }
        else
        {
            UpdateDisplay();
        }
    }
}
