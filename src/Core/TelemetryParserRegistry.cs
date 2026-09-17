using System;
using System.Collections.Generic;
using System.Linq;

namespace KsitalTelemetryHub.Core;

/// <summary>
/// Реестр и фабрика парсеров телеметрии с автоопределением типа контроллера.
/// </summary>
public class TelemetryParserRegistry
{
    private readonly Dictionary<DeviceType, ITelemetryParser> _parsers = new();

    public TelemetryParserRegistry(IEnumerable<ITelemetryParser>? parsers = null)
    {
        if (parsers != null)
        {
            foreach (var parser in parsers)
            {
                Register(parser);
            }
        }
    }

    public void Register(ITelemetryParser parser)
    {
        _parsers[parser.SupportedDeviceType] = parser;
    }

    public ITelemetryParser? GetParser(DeviceType deviceType)
    {
        return _parsers.TryGetValue(deviceType, out var parser) ? parser : null;
    }

    /// <summary>
    /// Автоматическое разрешение парсера:
    /// Сначала проверяет известный DeviceType (если задан),
    /// иначе инспектирует структуру текста SMS через CanParse().
    /// Если ни один парсер не подошел, возвращает дефолтный (Ksital).
    /// </summary>
    public ITelemetryParser Resolve(string rawText, DeviceType? knownType = null)
    {
        if (knownType.HasValue && _parsers.TryGetValue(knownType.Value, out var specificParser))
        {
            return specificParser;
        }

        foreach (var parser in _parsers.Values)
        {
            if (parser.CanParse(rawText))
            {
                return parser;
            }
        }

        // Дефолтный fallback на Ksital, если зарегистрирован, иначе первый попавшийся
        if (_parsers.TryGetValue(DeviceType.Ksital, out var defaultParser))
        {
            return defaultParser;
        }

        return _parsers.Values.FirstOrDefault() 
            ?? throw new InvalidOperationException("В реестре не зарегистрировано ни одного парсера телеметрии.");
    }
}
