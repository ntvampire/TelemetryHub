using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(PhoneNumber));
        OnPropertyChanged(nameof(District));
        OnPropertyChanged(nameof(DeviceTypeName));
        OnPropertyChanged(nameof(PowerStatusText));
        OnPropertyChanged(nameof(StatusBadgeBrush));
        OnPropertyChanged(nameof(StatusBadgeColor));
        OnPropertyChanged(nameof(TemperaturesFormatted));
    }
}

public partial class ObjectDistrictGroup : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private string _district = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private ObservableCollection<ObjectDisplayItem> _items = new();

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveAlarm))]
    [NotifyPropertyChangedFor(nameof(AlarmBadgeVisibility))]
    private int _activeAlarmCount;

    public bool HasActiveAlarm => ActiveAlarmCount > 0;

    public Microsoft.UI.Xaml.Visibility AlarmBadgeVisibility => HasActiveAlarm
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string CountText => $"{Items.Count} {GetPlural(Items.Count, "объект", "объекта", "объектов")}";

    public ObjectDistrictGroup(string district, IEnumerable<ObjectDisplayItem> items)
    {
        _district = district;
        _items = new ObservableCollection<ObjectDisplayItem>(items);
        _activeAlarmCount = _items.Count(i => i.HasActiveAlarm);
    }

    public void NotifyCountChanged()
    {
        OnPropertyChanged(nameof(CountText));
    }

    private static string GetPlural(int count, string one, string few, string many)
    {
        int n = Math.Abs(count) % 100;
        int n1 = n % 10;
        if (n is > 10 and < 20) return many;
        if (n1 is > 1 and < 5) return few;
        if (n1 == 1) return one;
        return many;
    }
}

public partial class AlarmDisplayItem : ObservableObject
{
    public static readonly SolidColorBrush AlarmBrush = new(Color.FromArgb(255, 0xE7, 0x4C, 0x3C));
    public static readonly SolidColorBrush ReportBrush = new(Color.FromArgb(255, 0x2E, 0xCC, 0x71));
    public static readonly SolidColorBrush CommandBrush = new(Color.FromArgb(255, 0x34, 0x98, 0xDB));
    public static readonly SolidColorBrush ServiceBrush = new(Color.FromArgb(255, 0x9B, 0x59, 0xB6));
    public static readonly SolidColorBrush AcknowledgedBrush = new(Color.FromArgb(255, 0x95, 0xA5, 0xA6));

    public long Id { get; set; }
    public int MonitoredObjectId { get; set; }
    public string ObjectName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string District { get; set; } = "Основной участок";
    public string DeviceTypeName { get; set; } = "Контроллер";
    public DateTime Timestamp { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsAcknowledged { get; set; }
    public string EventType { get; set; } = "Alarm";

    public string EventTypeTitle => EventType switch
    {
        "Alarm" => "ТРЕВОГА",
        "Report" => "Телеметрия",
        "Command" => "Команда",
        "Response" => "Ответ",
        "Service" => "Служебное",
        _ => EventType
    };

    public string EventIconGlyph => EventType switch
    {
        "Alarm" => "\uE7BA",
        "Command" => "\uE8BD",
        "Response" => "\uE8C4",
        "Service" => "\uE713",
        _ => "\uE9D9"
    };

    public SolidColorBrush EventBrush => EventType switch
    {
        "Alarm" => IsAcknowledged ? AcknowledgedBrush : AlarmBrush,
        "Command" => CommandBrush,
        "Response" => ReportBrush,
        "Service" => ServiceBrush,
        _ => ReportBrush
    };

    public string StatusText => EventType == "Alarm"
        ? (IsAcknowledged ? "Подтверждено" : "ТРЕВОГА")
        : EventTypeTitle;

    public Microsoft.UI.Xaml.Visibility AcknowledgeVisibility => (EventType == "Alarm" && !IsAcknowledged)
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility AcknowledgedBadgeVisibility => (EventType == "Alarm" && IsAcknowledged)
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string TimestampFormatted => Timestamp.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
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
