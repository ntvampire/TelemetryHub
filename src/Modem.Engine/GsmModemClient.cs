using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using KsitalTelemetryHub.Core;

namespace KsitalTelemetryHub.Modem.Engine;

public class GsmModemClient : IDisposable
{
    private readonly string _portName;
    private readonly int _baudRate;
    private SerialPort? _serialPort;
    private readonly object _lock = new();

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

            // Кандидаты скоростей: 9600 (промышленный стандарт RS-232 / Teleofis RX100),
            // заданная скорость и другие стандартные скорости.
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
                DtrEnable = true, // КРИТИЧЕСКИ ВАЖНО: пробуждение приемопередатчика RS-232 и модема
                RtsEnable = true, // КРИТИЧЕСКИ ВАЖНО: готовность приема для аппаратного контроля потока
                Handshake = Handshake.None
            };

            _serialPort.Open();

            // Гарантируем активные уровни DTR и RTS (+12В) после открытия порта
            _serialPort.DtrEnable = true;
            _serialPort.RtsEnable = true;
            Thread.Sleep(150); // Пауза для пробуждения процессора модема

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

            // Выключаем эхо команд для упрощения парсинга ответов
            SendCommand("ATE0");

            // Включаем режим PDU (0 - PDU, 1 - Text)
            string pduResponse = SendCommand("AT+CMGF=0");
            if (!pduResponse.Contains("OK"))
            {
                throw new InvalidOperationException($"Модем ответил на AT, но отказался перейти в PDU-режим (AT+CMGF=0): {pduResponse.Trim()}");
            }
        }
    }

    /// <summary>
    /// Опрашивает модем на наличие входящих SMS, декодирует их и удаляет из памяти модема.
    /// </summary>
    public List<DecodedSms> FetchAndPurgeSms()
    {
        lock (_lock)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Модем не подключен. Сначала вызовите Connect().");

            var messages = new List<DecodedSms>();

            // AT+CMGL=4 возвращает все сообщения в PDU-режиме
            string response = SendCommand("AT+CMGL=4", timeoutMs: 5000);

            // Регулярное выражение для поиска строк +CMGL: <index>,<status>,[<alpha>],<length>\r\n<PDU_HEX>
            var regex = new Regex(@"\+CMGL:\s*(?<idx>\d+),\s*\d+,\s*.*?,?\s*\d+\r?\n(?<pdu>[0-9A-Fa-f]+)", RegexOptions.Multiline);
            var matches = regex.Matches(response);

            foreach (Match match in matches)
            {
                string idx = match.Groups["idx"].Value;
                string pdu = match.Groups["pdu"].Value.Trim();

                try
                {
                    // Декодируем сообщение через написанный PduDecoder
                    var decoded = PduDecoder.Decode(pdu);
                    messages.Add(decoded);
                }
                catch
                {
                    // При поврежденном PDU пропускаем декодирование, но удаляем из памяти в finally
                }
                finally
                {
                    // Гарантированно удаляем SMS из памяти модема (даже если PDU был поврежден или это спам),
                    // чтобы память SIM-карты/модема никогда не переполнялась
                    try
                    {
                        SendCommand($"AT+CMGD={idx}");
                    }
                    catch { }
                }
            }

            return messages;
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
                    current.Contains("ERROR\r") || current.Contains("ERROR\n"))
                {
                    break;
                }
            }
            Thread.Sleep(30);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Возвращает список всех доступных в Windows COM-портов.
    /// </summary>
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
            try
            {
                // 1. Включаем текстовый режим SMS
                SendCommand("AT+CMGF=1");
                Thread.Sleep(100);

                // 2. Инициируем команду отправки на номер получателя
                _serialPort.DiscardInBuffer();
                _serialPort.Write($"AT+CMGS=\"{phoneNumber}\"\r");

                // Ожидаем приглашения ввода '>' от модема
                var startWait = DateTime.UtcNow;
                bool promptReceived = false;
                while ((DateTime.UtcNow - startWait).TotalSeconds < 5)
                {
                    if (_serialPort.BytesToRead > 0)
                    {
                        string chunk = _serialPort.ReadExisting();
                        if (chunk.Contains(">"))
                        {
                            promptReceived = true;
                            break;
                        }
                    }
                    Thread.Sleep(50);
                }

                if (!promptReceived)
                {
                    _serialPort.Write(new byte[] { 0x1B }, 0, 1); // Escape
                    return false;
                }

                // 3. Отправляем текст команды и символ завершения Ctrl+Z (0x1A)
                _serialPort.Write(messageText + "\x1A");

                // 4. Ожидаем подтверждения отправки (+CMGS: ... OK)
                startWait = DateTime.UtcNow;
                while ((DateTime.UtcNow - startWait).TotalSeconds < 15)
                {
                    if (_serialPort.BytesToRead > 0)
                    {
                        string response = _serialPort.ReadExisting();
                        if (response.Contains("OK")) return true;
                        if (response.Contains("ERROR")) return false;
                    }
                    Thread.Sleep(100);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
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

    /// <summary>
    /// Опрашивает уровень сигнала базовой станции GSM (AT+CSQ).
    /// Возвращает значение RSSI (0..31, либо 0 если нет связи или 99).
    /// </summary>
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
            catch
            {
            }

            return 0;
        }
    }

    /// <summary>
    /// Опрашивает имя сотового оператора, в сети которого зарегистрирована SIM-карта (AT+COPS?).
    /// Возвращает название оператора ("MTS", "MegaFon" и т.д.) либо null.
    /// </summary>
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
            catch
            {
            }

            return null;
        }
    }
}
