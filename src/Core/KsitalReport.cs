using System;
using System.Collections.Generic;

namespace KsitalTelemetryHub.Core;

public enum PowerState
{
    Unknown,
    Normal,     // "220V: Есть"
    Off         // "220V: Нет" или "Авария сети 220V"
}

public enum ZoneState
{
    Normal,      // "Норма"
    Triggered,   // "Сработка" / "Тревога"
    OpenCircuit, // "Обрыв"
    ShortCircuit // "Замыкание"
}

/// <summary>
/// Для обратной совместимости: псевдоним TelemetrySnapshot.
/// </summary>
public class KsitalReport : TelemetrySnapshot
{
}
