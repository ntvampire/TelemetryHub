using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using KsitalTelemetryHub.UI.Avalonia.Models;
using KsitalTelemetryHub.UI.Avalonia.Services;
using KsitalTelemetryHub.UI.Avalonia.ViewModels;

namespace KsitalTelemetryHub.UI.Avalonia.Views;

public partial class AlarmWindow : Window
{
    private readonly List<AlarmDisplayItem> _alarms = new();
    private readonly MainViewModel _vm;
    private int _currentIndex;

    public AlarmWindow() : this(new AlarmDisplayItem(), new MainViewModel())
    {
    }

    public AlarmWindow(AlarmDisplayItem initialAlarm, MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _alarms.Add(initialAlarm);
        _currentIndex = 0;
        UpdateUI();
    }

    public void AddAlarm(AlarmDisplayItem alarm)
    {
        if (!_alarms.Exists(a => a.Id == alarm.Id))
        {
            _alarms.Add(alarm);
            _currentIndex = _alarms.Count - 1;
            UpdateUI();
        }
    }

    private void UpdateUI()
    {
        if (_alarms.Count == 0)
        {
            Close();
            return;
        }

        if (_currentIndex >= _alarms.Count) _currentIndex = _alarms.Count - 1;
        if (_currentIndex < 0) _currentIndex = 0;

        var alarm = _alarms[_currentIndex];
        TxtObjectName.Text = alarm.ObjectName;
        TxtDistrict.Text = alarm.District;
        TxtDeviceType.Text = alarm.DeviceTypeName;
        TxtPhoneNumber.Text = alarm.PhoneNumber;
        TxtAlarmDesc.Text = alarm.Description;
        TxtTimestamp.Text = alarm.TimestampFormatted;

        if (_alarms.Count > 1)
        {
            BrdAlarmCount.IsVisible = true;
            TxtAlarmCount.Text = $"{_currentIndex + 1} из {_alarms.Count}";
            GridPager.IsVisible = true;
            TxtPagerInfo.Text = $"Тревога {_currentIndex + 1} из {_alarms.Count}";
            BtnPrevAlarm.IsEnabled = _currentIndex > 0;
            BtnNextAlarm.IsEnabled = _currentIndex < _alarms.Count - 1;
        }
        else
        {
            BrdAlarmCount.IsVisible = false;
            GridPager.IsVisible = false;
        }
    }

    private void BtnPrevAlarm_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentIndex > 0)
        {
            _currentIndex--;
            UpdateUI();
        }
    }

    private void BtnNextAlarm_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentIndex < _alarms.Count - 1)
        {
            _currentIndex++;
            UpdateUI();
        }
    }

    private async void BtnAcknowledge_Click(object? sender, RoutedEventArgs e)
    {
        if (_alarms.Count == 0) return;
        var current = _alarms[_currentIndex];

        BtnAcknowledge.IsEnabled = false;
        BtnSnooze.IsEnabled = false;

        try
        {
            await AlarmManager.AcknowledgeAlarmAsync(current, _vm);
            _alarms.RemoveAt(_currentIndex);
            UpdateUI();
        }
        finally
        {
            BtnAcknowledge.IsEnabled = true;
            BtnSnooze.IsEnabled = true;
        }
    }

    private async void BtnSnooze_Click(object? sender, RoutedEventArgs e)
    {
        if (_alarms.Count == 0) return;
        var current = _alarms[_currentIndex];

        BtnAcknowledge.IsEnabled = false;
        BtnSnooze.IsEnabled = false;

        try
        {
            await AlarmManager.SnoozeAlarmAsync(current, 5);
            _alarms.RemoveAt(_currentIndex);
            UpdateUI();
        }
        finally
        {
            BtnAcknowledge.IsEnabled = true;
            BtnSnooze.IsEnabled = true;
        }
    }
}
