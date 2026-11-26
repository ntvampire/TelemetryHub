using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using KsitalTelemetryHub.Core;

namespace KsitalTelemetryHub.Modem.Engine;

public class GsmModemClient : IDisposable
{
    private static readonly Regex ValidPhoneRegex = new(@"^\+?\d{5,15}$", RegexOptions.Compiled);
    private static readonly Regex CmglRegex = new(@"\+CMGL:\s*(?<idx>\d+),\s*\d+,\s*[^,\r\n]*,\s*\d+\r?\n(?<pdu>[0-9A-Fa-f]+)", RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly string _portName;
    private readonly int _baudRate;
    private SerialPort? _serialPort;
    private readonly object _lock = new();
    private readonly Dictionary<string, MultipartBuffer> _multipartBuffers = new(StringComparer.OrdinalIgnoreCase);

    private sealed class MultipartBuffer
    {
        public int TotalParts { get; init; }
        public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
        public Dictionary<int, DecodedSms> Parts { get; } = new();
    }

    public bool IsConnected => _serialPort != null && _serialPort.IsOpen;
    public int CurrentBaudRate { get; private set; } = 9600;
    public string PortName => _portName;

    public GsmModemClient(string portName, int baudRate = 9600)
    {
        _portName = portName;
        _baudRate = baudRate > 0 ? baudRate : 9600;
        CurrentBaudRate = _baudRate;
    }

    /// <summary>
    /// Проверяет и очищает номер телефона перед подстановкой в AT-команды (защита от AT-инъекций).
    /// </summary>
    public static string ValidateAndSanitizePhone(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new ArgumentException("Номер телефона получателя не может быть пустым.", nameof(phoneNumber));

        if (phoneNumber.IndexOfAny(new[] { '\r', '\n', '"', '\x1A', '\x1B', '\0' }) >= 0)
            throw new ArgumentException("Номер телефона содержит недопустимые управляющие символы.", nameof(phoneNumber));

        string normalized = PhoneNumber.Normalize(phoneNumber);
        if (!ValidPhoneRegex.IsMatch(normalized))
            throw new ArgumentException($"Некорректный формат номера телефона для AT-команды: '{phoneNumber}'.", nameof(phoneNumber));

        return normalized;
    }

    /// <summary>
    /// Удаляет управляющие байты Ctrl+Z / Esc / NUL из текста SMS (защита от обрыва AT+CMGS и инъекций).
    /// </summary>
    public static string SanitizeSmsText(string messageText)
    {
        if (string.IsNullOrEmpty(messageText))
            return string.Empty;

        var sb = new StringBuilder(messageText.Length);
        foreach (char c in messageText)
        {
            if (c == '\x1A' || c == '\x1B' || c == '\0')
                continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Проверяет отклик модема на команду AT на текущей скорости с пробуждающими импульсами.
    /// </summary>
    private bool ProbeAt(int timeoutMs = 1200)
    {
        if (_serialPort == null || !_serialPort.IsOpen) return false;

        try
        {
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();

            // Пробуждающий двойной импульс AT для синхронизации делителя частоты (Autobauding)
            _serialPort.Write("AT\r");
            Thread.Sleep(80);
            _serialPort.Write("AT\r");

            var sb = new StringBuilder();
            var startTime = DateTime.UtcNow;

            while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
            {
                if (_serialPort.BytesToRead > 0)
                {
                    sb.Append(_serialPort.ReadExisting());
                    string current = sb.ToString();
                    if (current.Contains("OK"))
                    {
                        return true;
                    }
                }
                Thread.Sleep(40);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Открывает COM-порт и инициализирует модем в PDU-режиме с автоматическим подбором скорости и контролем линий DTR/RTS.
    /// </summary>
    public void Connect()
    {
        lock (_lock)
        {
            if (IsConnected) return;

            var candidateBaudRates = new List<int>();
            if (_baudRate > 0) candidateBaudRates.Add(_baudRate);
            if (!candidateBaudRates.Contains(9600)) candidateBaudRates.Add(9600);
            if (!candidateBaudRates.Contains(115200)) candidateBaudRates.Add(115200);
            if (!candidateBaudRates.Contains(19200)) candidateBaudRates.Add(19200);
            if (!candidateBaudRates.Contains(57600)) candidateBaudRates.Add(57600);
            if (!candidateBaudRates.Contains(38400)) candidateBaudRates.Add(38400);

            int initialBaud = candidateBaudRates[0];

            _serialPort = new SerialPort(_portName, initialBaud, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 2500,
                WriteTimeout = 2500,
                NewLine = "\r",
                Encoding = Encoding.ASCII,
                DtrEnable = true, // Пробуждение приемопередатчика RS-232 и модема
                RtsEnable = true, // Готовность приема
                Handshake = Handshake.None
            };

            _serialPort.Open();

            _serialPort.DtrEnable = true;
            _serialPort.RtsEnable = true;
            Thread.Sleep(150);

            bool atResponded = false;
            int successfulBaud = initialBaud;

            foreach (var baud in candidateBaudRates)
            {
                try
                {
                    _serialPort.BaudRate = baud;
                    _serialPort.DtrEnable = true;
                    _serialPort.RtsEnable = true;
                    Thread.Sleep(50);

                    if (ProbeAt(timeoutMs: 1200))
                    {
                        atResponded = true;
                        successfulBaud = baud;
                        break;
                    }
                }
                catch
                {
                    // Игнорируем ошибку при смене скорости
                }
            }

            if (!atResponded)
            {
                _serialPort.Close();
                _serialPort.Dispose();
                _serialPort = null;
                throw new InvalidOperationException(
                    $"Модем на порту {_portName} не ответил на команду AT ни на одной из скоростей " +
                    $"({string.Join(", ", candidateBaudRates)}). Проверьте кабель RS-232, питание 12В и SIM-карту.");
            }

            CurrentBaudRate = successfulBaud;

            SendCommand("ATE0");

            string pduResponse = SendCommand("AT+CMGF=0");
            if (!pduResponse.Contains("OK"))
            {
                throw new InvalidOperationException($"Модем ответил на AT, но отказался перейти в PDU-режим (AT+CMGF=0): {pduResponse.Trim()}");
            }
        }
    }

    /// <summary>
    /// Извлекает пары (индекс, PDU) из ответа модема на AT+CMGL=4.
    /// </summary>
    public static List<(string Index, string Pdu)> ExtractCmglEntries(string response)
    {
        var entries = new List<(string Index, string Pdu)>();
        if (string.IsNullOrWhiteSpace(response)) return entries;

        foreach (Match match in CmglRegex.Matches(response))
        {
            entries.Add((match.Groups["idx"].Value, match.Groups["pdu"].Value.Trim()));
        }
        return entries;
    }

    /// <summary>
    /// Склеивает многосегментные SMS (concatenated UDH) и возвращает готовые сообщения.
    /// Неполные сегменты удерживаются в буфере до прихода остальных частей (до 2 минут).
    /// </summary>
    public List<DecodedSms> AssembleMessages(IEnumerable<PduDecodeResult> decodedParts, DateTime? nowUtc = null)
    {
        DateTime currentUtc = nowUtc ?? DateTime.UtcNow;
        var ready = new List<DecodedSms>();

        lock (_lock)
        {
            foreach (var item in decodedParts)
            {
                if (item.Concat == null)
                {
                    ready.Add(item.Sms);
                    continue;
                }

                string normSender = PhoneNumber.Normalize(item.Sms.SenderNumber);
                if (string.IsNullOrEmpty(normSender)) normSender = item.Sms.SenderNumber;
                string key = $"{normSender}|{item.Concat.ReferenceNumber}|{item.Concat.TotalParts}";

                if (!_multipartBuffers.TryGetValue(key, out var buffer))
                {
                    buffer = new MultipartBuffer
                    {
                        TotalParts = item.Concat.TotalParts,
                        CreatedAtUtc = currentUtc
                    };
                    _multipartBuffers[key] = buffer;
                }

                buffer.Parts[item.Concat.PartNumber] = item.Sms;

                if (buffer.Parts.Count >= buffer.TotalParts)
                {
                    ready.Add(MergeBuffer(buffer));
                    _multipartBuffers.Remove(key);
                }
            }

            // Выдаём просроченные неполные сегменты (> 2 минут), чтобы не терять данные при обрыве одного сегмента в сети
            var expiredKeys = _multipartBuffers
                .Where(kv => currentUtc - kv.Value.CreatedAtUtc >= TimeSpan.FromMinutes(2))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                ready.Add(MergeBuffer(_multipartBuffers[key]));
                _multipartBuffers.Remove(key);
            }
        }

        return ready;
    }

    private static DecodedSms MergeBuffer(MultipartBuffer buffer)
    {
        var ordered = buffer.Parts.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
        var first = ordered[0];
        return new DecodedSms
        {
            SenderNumber = first.SenderNumber,
            Timestamp = ordered.Min(p => p.Timestamp),
            Text = string.Concat(ordered.Select(p => p.Text)),
            RawPdu = string.Join(";", ordered.Select(p => p.RawPdu))
        };
    }

    /// <summary>
    /// Опрашивает модем на наличие входящих SMS, декодирует (включая склейку длинных SMS) и удаляет из памяти модема.
    /// </summary>
    public List<DecodedSms> FetchAndPurgeSms()
    {
        lock (_lock)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Модем не подключен. Сначала вызовите Connect().");

            // Гарантируем PDU-режим перед AT+CMGL=4
            string response = SendCommand("AT+CMGL=4", timeoutMs: 5000);
            if (response.Contains("ERROR"))
            {
                SendCommand("AT+CMGF=0");
                response = SendCommand("AT+CMGL=4", timeoutMs: 5000);
            }

            var decodedParts = new List<PduDecodeResult>();
            var entries = ExtractCmglEntries(response);

            foreach (var (idx, pdu) in entries)
            {
                try
                {
                    decodedParts.Add(PduDecoder.DecodeWithMetadata(pdu));
                }
                catch
                {
                    // При поврежденном PDU пропускаем декодирование, но удаляем из памяти в finally
                }
                finally
                {
                    try
                    {
                        SendCommand($"AT+CMGD={idx}");
                    }
                    catch { }
                }
            }

            return AssembleMessages(decodedParts);
        }
    }

    /// <summary>
    /// Отправляет низкоуровневую AT-команду и ждет завершения ответа (OK или ERROR).
    /// </summary>
    public string SendCommand(string command, int timeoutMs = 3000)
    {
        if (_serialPort == null || !_serialPort.IsOpen)
            throw new InvalidOperationException("COM-порт закрыт.");

        _serialPort.DiscardInBuffer();
        _serialPort.Write(command + "\r");

        var sb = new StringBuilder();
        var startTime = DateTime.UtcNow;

        while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
        {
            if (_serialPort.BytesToRead > 0)
            {
                string chunk = _serialPort.ReadExisting();
                sb.Append(chunk);

                string current = sb.ToString();
                if (current.Contains("OK\r") || current.Contains("OK\n") ||
                    current.Contains("ERROR\r") || current.Contains("ERROR\n") ||
                    current.Contains("+CMS ERROR"))
                {
                    break;
                }
            }
            Thread.Sleep(30);
        }

        return sb.ToString();
    }

    public static string[] GetAvailablePorts()
    {
        return SerialPort.GetPortNames();
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            if (_serialPort != null)
            {
                if (_serialPort.IsOpen)
                {
                    _serialPort.Close();
                }
                _serialPort.Dispose();
                _serialPort = null;
            }
        }
    }

    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }

    public bool SendSms(string phoneNumber, string messageText)
    {
        string safePhone = ValidateAndSanitizePhone(phoneNumber);
        string safeText = SanitizeSmsText(messageText);

        if (_serialPort == null || !_serialPort.IsOpen)
        {
            Connect();
        }

        if (_serialPort == null || !_serialPort.IsOpen)
        {
            return false;
        }

        lock (_lock)
        {
            bool switchedToTextMode = false;
            try
            {
                if (PduEncoder.RequiresUnicode(safeText))
                {
                    // Отправка не-ASCII/кириллицы прямо в PDU-режиме (UCS2)
                    SendCommand("AT+CMGF=0");
                    var pdus = PduEncoder.EncodeUcs2Submit(safePhone, safeText);
                    foreach (var pdu in pdus)
                    {
                        if (!SendCmgsPayload($"AT+CMGS={pdu.TpduLength}\r", pdu.Hex))
                            return false;
                    }
                    return true;
                }

                // Для стандартных ASCII-команд используем текстовый режим с гарантированным возвратом PDU в finally
                string textModeResp = SendCommand("AT+CMGF=1");
                if (!textModeResp.Contains("OK"))
                    return false;

                switchedToTextMode = true;
                Thread.Sleep(50);

                return SendCmgsPayload($"AT+CMGS=\"{safePhone}\"\r", safeText);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (switchedToTextMode && _serialPort != null && _serialPort.IsOpen)
                {
                    try
                    {
                        SendCommand("AT+CMGF=0");
                    }
                    catch { }
                }
            }
        }
    }

    private bool SendCmgsPayload(string cmgsHeaderCommand, string payload)
    {
        if (_serialPort == null || !_serialPort.IsOpen)
            return false;

        _serialPort.DiscardInBuffer();
        _serialPort.Write(cmgsHeaderCommand);

        var startWait = DateTime.UtcNow;
        bool promptReceived = false;
        while ((DateTime.UtcNow - startWait).TotalSeconds < 5)
        {
            if (_serialPort.BytesToRead > 0)
            {
                string chunk = _serialPort.ReadExisting();
                if (chunk.Contains('>'))
                {
                    promptReceived = true;
                    break;
                }
                if (chunk.Contains("ERROR"))
                    return false;
            }
            Thread.Sleep(50);
        }

        if (!promptReceived)
        {
            _serialPort.Write(new byte[] { 0x1B }, 0, 1); // Escape
            return false;
        }

        _serialPort.Write(payload + "\x1A");

        startWait = DateTime.UtcNow;
        var respSb = new StringBuilder();
        while ((DateTime.UtcNow - startWait).TotalSeconds < 15)
        {
            if (_serialPort.BytesToRead > 0)
            {
                respSb.Append(_serialPort.ReadExisting());
                string response = respSb.ToString();
                if (response.Contains("OK")) return true;
                if (response.Contains("ERROR")) return false;
            }
            Thread.Sleep(100);
        }

        return false;
    }

    public static int ParseSignalStrength(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return 0;
        var match = Regex.Match(response, @"\+CSQ:\s*(\d+)\s*,");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int csq))
        {
            return csq == 99 ? 0 : csq;
        }
        return 0;
    }

    public static string? ParseOperatorName(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return null;
        var match = Regex.Match(response, @"\+COPS:\s*\d+\s*,\s*\d+\s*,\s*""([^""]+)""");
        if (match.Success)
        {
            string op = match.Groups[1].Value.Trim();
            return op switch
            {
                "25001" => "MTS",
                "25002" => "MegaFon",
                "25099" => "Beeline",
                "25020" => "Tele2",
                _ => op
            };
        }
        return null;
    }

    public int GetSignalStrengthCsq()
    {
        lock (_lock)
        {
            if (!IsConnected) return 0;

            try
            {
                string res = SendCommand("AT+CSQ", timeoutMs: 2000);
                return ParseSignalStrength(res);
            }
            catch { }

            return 0;
        }
    }

    public string? GetOperatorName()
    {
        lock (_lock)
        {
            if (!IsConnected) return null;

            try
            {
                string res = SendCommand("AT+COPS?", timeoutMs: 3000);
                return ParseOperatorName(res);
            }
            catch { }

            return null;
        }
    }
}
