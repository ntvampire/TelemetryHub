using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Storage.Sqlite;

namespace KsitalTelemetryHub.UI.WinUI.Models;

public partial class ObjectDisplayItem : ObservableObject
{
    public static readonly SolidColorBrush RedBrush = new(Color.FromArgb(255, 0xE7, 0x4C, 0x3C));
    public static readonly SolidColorBrush YellowBrush = new(Color.FromArgb(255, 0xF3, 0x9C, 0x12));
    public static readonly SolidColorBrush GreenBrush = new(Color.FromArgb(255, 0x2E, 0xCC, 0x71));

    public int Id { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _phoneNumber = string.Empty;

    [ObservableProperty]
    private string _district = "Основной участок";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeviceTypeName))]
    private DeviceType _deviceType;

    [ObservableProperty]
    private string _devicePassword = "00000";

    public string DeviceTypeName => DeviceType switch
    {
        DeviceType.Ksital => "КСИТАЛ GSM",
        DeviceType.Ccu825 => "CCU-825",
        DeviceType.OwenPlc => "ОВЕН ПЛК",
        _ => "Контроллер"
    };

    [ObservableProperty]
    private DateTime? _lastSeen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PowerStatusText))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeColor))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeBrush))]
    private PowerState _mainPower = PowerState.Unknown;

    [ObservableProperty]
    private double? _batteryVoltage;

    [ObservableProperty]
    private double? _simBalance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemperaturesFormatted))]
    private Dictionary<string, double> _temperatures = new();

    public string TemperaturesFormatted => Temperatures.Count > 0 
        ? string.Join("  ", Temperatures.Select(t => $"{t.Key}: {(t.Value > 0 ? "+" : "")}{t.Value:F1}°C"))
        : "Нет данных";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBadgeColor))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeBrush))]
    private bool _hasActiveAlarm;

    [ObservableProperty]
    private string? _alarmDescription;

    public string PowerStatusText => MainPower switch
    {
        PowerState.Normal => "220V: Есть",
        PowerState.Off => "220V: НЕТ!",
        _ => "220V: ?"
    };

    public string StatusBadgeColor => HasActiveAlarm 
        ? "#E74C3C" 
        : (MainPower == PowerState.Off ? "#F39C12" : "#2ECC71");

    public SolidColorBrush StatusBadgeBrush => HasActiveAlarm 
        ? RedBrush 
        : (MainPower == PowerState.Off ? YellowBrush : GreenBrush);

    public void UpdateFrom(ObjectDisplayItem other)
    {
        Name = other.Name;
        PhoneNumber = other.PhoneNumber;
        District = other.District;
        DeviceType = other.DeviceType;
        DevicePassword = other.DevicePassword;
        LastSeen = other.LastSeen;
        MainPower = other.MainPower;
        BatteryVoltage = other.BatteryVoltage;
        SimBalance = other.SimBalance;
        HasActiveAlarm = other.HasActiveAlarm;
        AlarmDescription = other.AlarmDescription;
        Temperatures = new Dictionary<string, double>(other.Temperatures);
        OnPropertyChanged(nameof(TemperaturesFormatted));
    }
}

public partial class AlarmDisplayItem : ObservableObject
{
    public long Id { get; set; }
    public int MonitoredObjectId { get; set; }
    public string ObjectName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsAcknowledged { get; set; }
    public string StatusText => IsAcknowledged ? "Квитирована" : "АКТИВНАЯ АВАРИЯ";
}

public class OutgoingCommandDisplayItem
{
    public long Id { get; set; }
    public int MonitoredObjectId { get; set; }
    public string ObjectName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string RawPayload { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public CommandStatus Status { get; set; }
    public string? ErrorMessage { get; set; }

    public string StatusText => Status switch
    {
        CommandStatus.Pending => "В очереди",
        CommandStatus.Sent => "Отправлена",
        CommandStatus.Failed => "Ошибка отправки",
        _ => "Неизвестно"
    };
}
