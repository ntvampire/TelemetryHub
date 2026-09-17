using System;
using System.Collections.Generic;
using KsitalTelemetryHub.Core;

namespace KsitalTelemetryHub.UI.WinUI.Models;

public class ObjectDisplayItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string District { get; set; } = "Основной участок";
    public DeviceType DeviceType { get; set; }
    public string DeviceTypeName => DeviceType switch
    {
        DeviceType.Ksital => "КСИТАЛ GSM",
        DeviceType.RadsCCU => "CCU-825",
        DeviceType.OvenPLC => "ОВЕН ПЛК",
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
}

public class AlarmDisplayItem
{
    public int Id { get; set; }
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
    public int Id { get; set; }
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
        CommandStatus.Sending => "Отправляется...",
        CommandStatus.Sent => "Отправлена",
        CommandStatus.Failed => "Ошибка отправки",
        _ => "Неизвестно"
    };
}
