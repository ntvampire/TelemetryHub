namespace KsitalTelemetryHub.Storage.Sqlite;

public class AlarmEvent
{
    public long Id { get; set; }

    public int? MonitoredObjectId { get; set; }
    public MonitoredObject? MonitoredObject { get; set; }

    public string EventType { get; set; } = "Alarm"; // "Alarm", "Report", "Command", "Response", "Service"
    public DateTime Timestamp { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsAcknowledged { get; set; } // Флаг: подтверждено ли событие оператором
    public DateTime? AcknowledgedAt { get; set; }
}