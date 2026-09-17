using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Storage.Sqlite;

namespace KsitalTelemetryHub.UI.WinUI.Models;

public class ObjectDisplayItem
{
    public static readonly SolidColorBrush RedBrush = new(Color.FromArgb(255, 0xE7, 0x4C, 0x3C));
    public static readonly SolidColorBrush YellowBrush = new(Color.FromArgb(255, 0xF3, 0x9C, 0x12));
    public static readonly SolidColorBrush GreenBrush = new(Color.FromArgb(255, 0x2E, 0xCC, 0x71));

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string District { get; set; } = "Основной участок";
    public DeviceType DeviceType { get; set; }
    public string DeviceTypeName => DeviceType switch
    {
        DeviceType.Ksital => "КСИТАЛ GSM",
        DeviceType.Ccu825 => "CCU-825",
        DeviceType.OwenPlc => "ОВЕН ПЛК",
        _ => "Контроллер"
    };

    public DateTime? LastSeen { get; set; }
    public PowerState MainPower { get; set; } = PowerState.Unknown;
    public double? BatteryVoltage { get; set; }
    public double? SimBalance { get; set; }
    public Dictionary<string, double> Temperatures { get; set; } = new();
    public string TemperaturesFormatted => Temperatures.Count > 0 
        ? string.Join("  ", Temperatures.Select(t => $"{t.Key}: {(t.Value > 0 ? "+" : "")}{t.Value:F1}°C"))
        : "Нет данных";

    public bool HasActiveAlarm { get; set; }
    public string? AlarmDescription { get; set; }

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
}

public class AlarmDisplayItem
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
