using System;
using System.Collections.Generic;

namespace KsitalTelemetryHub.Core;

/// <summary>
/// Унифицированный снимок телеметрии контроллера (КСИТАЛ, RADS CCU-825, ОВЕН ПЛК).
/// </summary>
public class TelemetrySnapshot
{
    public string DeviceName { get; set; } = string.Empty;
    public string SenderPhone { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Питание
    public PowerState MainPower { get; set; } = PowerState.Unknown;
    public double? BatteryVoltage { get; set; }

    // Баланс SIM-карты
    public decimal? SimBalance { get; set; }

    // Температурные датчики (T1 -> +65.0, T2 -> +45.5 и т.д.)
    public Dictionary<string, double> Temperatures { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Зоны / шлейфы (1 -> Normal, 2 -> Triggered и т.д.)
    public Dictionary<int, ZoneState> Zones { get; set; } = new();

    // Реле и управляющие выходы (Relay1 -> true, Out1 -> false)
    public Dictionary<string, bool> Outputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Флаг аварийного/тревожного сообщения
    public bool IsAlarm { get; set; }
    public string AlarmDescription { get; set; } = string.Empty;

    // Исходный текст СМС
    public string RawText { get; set; } = string.Empty;
}
