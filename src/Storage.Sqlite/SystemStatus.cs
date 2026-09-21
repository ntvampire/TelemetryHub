using System;

namespace KsitalTelemetryHub.Storage.Sqlite;

/// <summary>
/// Состояние аппаратного шлюза и фоновой службы.
/// Служит каналом IPC между Service.Worker (владельцем COM-порта) и диспетчерским интерфейсом.
/// Исключает коллизии параллельного открытия COM-порта.
/// </summary>
public class SystemStatus
{
    public int Id { get; set; } = 1;
    public string PortName { get; set; } = "COM3";
    public bool IsWorkerAlive { get; set; } = true;
    public bool IsModemConnected { get; set; }
    public int SignalStrengthCsq { get; set; } // 0..31
    public string? OperatorName { get; set; }
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public string? LastError { get; set; }
    public int TotalSmsProcessed { get; set; }
    public string? RequestedPortName { get; set; }
    public bool RequestSignalCheck { get; set; }
}
