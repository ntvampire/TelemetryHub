using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using KsitalTelemetryHub.Core;

namespace KsitalTelemetryHub.Parser.Ksital;

public class KsitalMessageParser : ITelemetryParser
{
    private static readonly Regex TempRegex = new(@"[TТ](?<index>\d+)\s*=\s*(?<val>[+-]?\d+(?:[\.,]\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ZoneRegex = new(@"[ЗZ](?<index>\d+)\s*:\s*(?<status>Норма|Сработка|Обрыв|Замыкание)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Power220Regex = new(@"220V\s*:\s*(?<val>Есть|Нет)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BatteryRegex = new(@"(?:Асс|Acc|АКБ)\s*:\s*(?<val>\d+(?:[\.,]\d+)?)V?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BalanceRegex = new(@"Баланс\s*:\s*(?<val>[+-]?\d+(?:[\.,]\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RelayRegex = new(@"(?:Реле|Rele|N)(?<index>[1-3])\s*[:=]\s*(?<val>Вкл|Выкл|1|0|ON|OFF)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UprRegex = new(@"(?:УПР|Upr)\s*[:=]\s*(?<val>Вкл|Выкл|1|0|ON|OFF)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AlarmKeywordRegex = new(@"(Тревога!|Авария!)(?<desc>.*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DeviceType SupportedDeviceType => DeviceType.Ksital;

    public bool CanParse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return false;
        return Power220Regex.IsMatch(rawText) ||
               TempRegex.IsMatch(rawText) ||
               ZoneRegex.IsMatch(rawText) ||
               BalanceRegex.IsMatch(rawText) ||
               RelayRegex.IsMatch(rawText) ||
               rawText.Contains("220V:", StringComparison.OrdinalIgnoreCase) ||
               rawText.Contains("Кситал", StringComparison.OrdinalIgnoreCase) ||
               rawText.Contains("контрол", StringComparison.OrdinalIgnoreCase) ||
               rawText.Contains("kontrol", StringComparison.OrdinalIgnoreCase) ||
               rawText.Contains("Реле", StringComparison.OrdinalIgnoreCase) ||
               rawText.Contains("Upr", StringComparison.OrdinalIgnoreCase) ||
               rawText.Contains("УПР", StringComparison.OrdinalIgnoreCase) ||
               rawText.Contains("Kak dela", StringComparison.OrdinalIgnoreCase);
    }

    public TelemetrySnapshot Parse(string rawText, DateTime timestamp, string? senderPhone = null)
    {
        return ParseInternal(rawText, timestamp, senderPhone);
    }

    public static KsitalReport Parse(DecodedSms sms)
    {
        return (KsitalReport)ParseInternal(sms.Text, sms.Timestamp, sms.SenderNumber);
    }

    public static KsitalReport Parse(string rawText, DateTime? timestamp = null, string? senderPhone = null)
    {
        return (KsitalReport)ParseInternal(rawText, timestamp ?? DateTime.UtcNow, senderPhone);
    }

    private static KsitalReport ParseInternal(string rawText, DateTime timestamp, string? senderPhone)
    {
        var report = new KsitalReport
        {
            SenderPhone = PhoneNumber.Normalize(senderPhone),
            Timestamp = timestamp,
            RawText = rawText ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(rawText)) return report;

        string text = rawText.Replace("\r", " ").Trim();
        string[] lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length > 0 && !lines[0].Contains(":") && !lines[0].Contains("="))
        {
            report.DeviceName = lines[0].Trim();
        }

        // 1. Проверка на статус "Авария!" / "Тревога!"
        var alarmMatch = AlarmKeywordRegex.Match(text);
        if (alarmMatch.Success)
        {
            report.IsAlarm = true;
            report.AlarmDescription = alarmMatch.Value.Trim();
        }

        // 2. Парсинг температур T1..Tn (поддерживаются латинская T и кириллическая Т)
        foreach (Match match in TempRegex.Matches(text))
        {
            string key = $"T{match.Groups["index"].Value}";
            string rawVal = match.Groups["val"].Value.Replace(',', '.');
            if (double.TryParse(rawVal, NumberStyles.Float, CultureInfo.InvariantCulture, out double tempVal))
            {
                report.Temperatures[key] = tempVal;
            }
        }

        // 3. Парсинг зон контроля З1..Зn
        var zoneAlarms = new List<string>();
        foreach (Match match in ZoneRegex.Matches(text))
        {
            if (int.TryParse(match.Groups["index"].Value, out int zoneIdx))
            {
                string status = match.Groups["status"].Value.ToLowerInvariant();
                var zoneState = status switch
                {
                    "норма" => ZoneState.Normal,
                    "сработка" => ZoneState.Triggered,
                    "обрыв" => ZoneState.OpenCircuit,
                    "замыкание" => ZoneState.ShortCircuit,
                    _ => ZoneState.Normal
                };
                report.Zones[zoneIdx] = zoneState;

                if (zoneState != ZoneState.Normal)
                {
                    string label = zoneState switch
                    {
                        ZoneState.Triggered => "Сработка",
                        ZoneState.OpenCircuit => "Обрыв",
                        ZoneState.ShortCircuit => "Замыкание",
                        _ => "Тревога"
                    };
                    zoneAlarms.Add($"{label} шлейфа З{zoneIdx}");
                }
            }
        }

        if (zoneAlarms.Count > 0)
        {
            report.IsAlarm = true;
            string joined = string.Join(", ", zoneAlarms);
            if (string.IsNullOrWhiteSpace(report.AlarmDescription))
            {
                report.AlarmDescription = joined;
            }
            else if (!report.AlarmDescription.Contains(joined, StringComparison.OrdinalIgnoreCase))
            {
                report.AlarmDescription = $"{report.AlarmDescription}; {joined}";
            }
        }

        // 4. Парсинг сети 220V
        var pwrMatch = Power220Regex.Match(text);
        if (pwrMatch.Success)
        {
            bool isPowerOk = pwrMatch.Groups["val"].Value.Equals("Есть", StringComparison.OrdinalIgnoreCase);
            report.MainPower = isPowerOk ? PowerState.Normal : PowerState.Off;
            if (!isPowerOk)
            {
                report.IsAlarm = true;
                if (string.IsNullOrWhiteSpace(report.AlarmDescription))
                {
                    report.AlarmDescription = "Авария: Отсутствует основное питание 220V";
                }
            }
        }

        // 5. Напряжение резервного аккумулятора
        var batMatch = BatteryRegex.Match(text);
        if (batMatch.Success)
        {
            string rawVal = batMatch.Groups["val"].Value.Replace(',', '.');
            if (double.TryParse(rawVal, NumberStyles.Float, CultureInfo.InvariantCulture, out double batVal))
            {
                report.BatteryVoltage = batVal;
            }
        }

        // 6. Баланс
        var balMatch = BalanceRegex.Match(text);
        if (balMatch.Success)
        {
            string rawVal = balMatch.Groups["val"].Value.Replace(',', '.');
            if (decimal.TryParse(rawVal, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal balVal))
            {
                report.SimBalance = balVal;
            }
        }

        // 7. Состояние реле и выхода УПР
        foreach (Match match in RelayRegex.Matches(text))
        {
            string idx = match.Groups["index"].Value;
            string val = match.Groups["val"].Value.ToLowerInvariant();
            bool isOn = val is "вкл" or "1" or "on";
            report.Outputs[$"Relay{idx}"] = isOn;
        }

        var uprMatch = UprRegex.Match(text);
        if (uprMatch.Success)
        {
            string val = uprMatch.Groups["val"].Value.ToLowerInvariant();
            bool isOn = val is "вкл" or "1" or "on";
            report.Outputs["Upr"] = isOn;
        }

        return report;
    }
}
