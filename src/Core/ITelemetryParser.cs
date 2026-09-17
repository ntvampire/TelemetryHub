using System;

namespace KsitalTelemetryHub.Core;

/// <summary>
/// Единый контракт парсера сообщений от объектовых контроллеров.
/// </summary>
public interface ITelemetryParser
{
    /// <summary>
    /// Тип поддерживаемого оборудования.
    /// </summary>
    DeviceType SupportedDeviceType { get; }

    /// <summary>
    /// Проверка, соответствует ли структура текста сигнатуре данного типа контроллера.
    /// Позволяет автоматически определять тип устройства даже без предварительной настройки в базе.
    /// </summary>
    bool CanParse(string rawText);

    /// <summary>
    /// Парсинг сырого текста SMS-сообщения в типизированный снимок телеметрии.
    /// </summary>
    TelemetrySnapshot Parse(string rawText, DateTime timestamp, string? senderPhone = null);
}
