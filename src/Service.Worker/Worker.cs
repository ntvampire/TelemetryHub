using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Modem.Engine;
using KsitalTelemetryHub.Parser.Ksital;
using KsitalTelemetryHub.Parser.Ccu825;
using KsitalTelemetryHub.Parser.Owen;
using KsitalTelemetryHub.Storage.Sqlite;

namespace KsitalTelemetryHub.Service.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private string _currentPortName;
    private readonly int _baudRate;
    private readonly int _pollIntervalSec;
    private readonly string _dbPath;
    private readonly TelemetryParserRegistry _parserRegistry;

    public Worker(ILogger<Worker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        _currentPortName = _configuration.GetValue<string>("ModemSettings:PortName") ?? "COM3";
        _baudRate = _configuration.GetValue<int>("ModemSettings:BaudRate", 9600);
        _pollIntervalSec = _configuration.GetValue<int>("ModemSettings:PollIntervalSeconds", 10);

        string? configuredDb = _configuration.GetValue<string>("DatabaseSettings:DbPath");
        _dbPath = AppPaths.GetDatabasePath(configuredDb);

        _parserRegistry = new TelemetryParserRegistry(new ITelemetryParser[]
        {
            new KsitalMessageParser(),
            new Ccu825MessageParser(),
            new OwenMessageParser()
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string mutexName = BuildWorkerMutexName(_dbPath);
        using var singleInstanceMutex = new Mutex(false, mutexName);
        bool hasHandle = false;

        try
        {
            try
            {
                hasHandle = singleInstanceMutex.WaitOne(TimeSpan.FromSeconds(2), false);
            }
            catch (AbandonedMutexException)
            {
                hasHandle = true;
            }

            if (!hasHandle)
            {
                _logger.LogWarning("Другой экземпляр Service.Worker уже обслуживает базу {DbPath}. Завершение дубликата.", _dbPath);
                return;
            }

            _logger.LogInformation("Инициализация базы данных SQLite ({DbPath})...", _dbPath);
            AppDbContext.EnsureDatabaseUpdated(_dbPath);

            // Если в БД ранее был сохранён выбранный порт, подхватываем его при старте
            using (var initDb = new AppDbContext(_dbPath))
            {
                var initStatus = await initDb.SystemStatus.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, stoppingToken);
                if (initStatus != null)
                {
                    if (!string.IsNullOrWhiteSpace(initStatus.RequestedPortName))
                        _currentPortName = initStatus.RequestedPortName;
                    else if (!string.IsNullOrWhiteSpace(initStatus.PortName))
                        _currentPortName = initStatus.PortName;
                }
            }

            _logger.LogInformation("Запуск сервиса мониторинга телеметрии. Порт: {Port}, Базовая скорость: {Baud}", _currentPortName, _baudRate);

            GsmModemClient? modem = null;
            string? cachedOperatorName = null;
            int lastSignalCsq = 0;
            DateTime lastSignalCheckTime = DateTime.MinValue;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var db = new AppDbContext(_dbPath);

                    // 0. Проверка запроса на смену COM-порта из UI
                    var currentStatus = await db.SystemStatus.FirstOrDefaultAsync(s => s.Id == 1, stoppingToken);
                    if (currentStatus != null && !string.IsNullOrWhiteSpace(currentStatus.RequestedPortName) && currentStatus.RequestedPortName != _currentPortName)
                    {
                        _logger.LogInformation("Получен запрос на смену COM-порта на {NewPort}...", currentStatus.RequestedPortName);
                        modem?.Dispose();
                        modem = null;
                        cachedOperatorName = null;
                        lastSignalCheckTime = DateTime.MinValue;
                        _currentPortName = currentStatus.RequestedPortName;
                        currentStatus.PortName = _currentPortName;
                        currentStatus.RequestedPortName = null;
                        await db.SaveChangesAsync(stoppingToken);
                    }

                    // 1. Контроль подключения к аппаратному модему
                    if (modem == null || !modem.IsConnected)
                    {
                        _logger.LogInformation("Подключение к модему на порту {Port}...", _currentPortName);
                        modem?.Dispose();
                        modem = new GsmModemClient(_currentPortName, _baudRate);
                        modem.Connect();
                        _logger.LogInformation("Модем успешно подключен. Порт: {Port}, Скорость: {Baud} бод.", _currentPortName, modem.CurrentBaudRate);

                        cachedOperatorName = modem.GetOperatorName();
                        _logger.LogInformation("Оператор SIM-карты модема: {Op}", cachedOperatorName ?? "не определен");

                        lastSignalCsq = modem.GetSignalStrengthCsq();
                        lastSignalCheckTime = DateTime.UtcNow;
                        _logger.LogInformation("Уровень сигнала модема: CSQ {Csq}", lastSignalCsq);
                    }

                    // 1.1. Проверка уровня сигнала: раз в 5 минут либо по требованию (клик в UI)
                    bool isManualSignalRequest = currentStatus != null && currentStatus.RequestSignalCheck;
                    bool isTimeToPollSignal = DateTime.UtcNow - lastSignalCheckTime >= TimeSpan.FromMinutes(5);

                    if ((isManualSignalRequest || isTimeToPollSignal) && modem != null && modem.IsConnected)
                    {
                        if (isManualSignalRequest)
                        {
                            _logger.LogInformation("Получен запрос оператора на внеочередной замер сигнала модема.");
                        }

                        lastSignalCsq = modem.GetSignalStrengthCsq();
                        lastSignalCheckTime = DateTime.UtcNow;
                        _logger.LogInformation("Обновлен уровень сигнала: CSQ {Csq}", lastSignalCsq);

                        if (string.IsNullOrWhiteSpace(cachedOperatorName))
                        {
                            cachedOperatorName = modem.GetOperatorName();
                        }

                        if (isManualSignalRequest && currentStatus != null)
                        {
                            currentStatus.RequestSignalCheck = false;
                            currentStatus.SignalStrengthCsq = lastSignalCsq;
                            if (!string.IsNullOrEmpty(cachedOperatorName))
                            {
                                currentStatus.OperatorName = cachedOperatorName;
                            }
                            await db.SaveChangesAsync(stoppingToken);
                        }
                    }
                    else if (isManualSignalRequest && currentStatus != null)
                    {
                        currentStatus.RequestSignalCheck = false;
                        await db.SaveChangesAsync(stoppingToken);
                    }

                    // 2. Отправка очереди исходящих команд оператора
                    if (modem != null && modem.IsConnected)
                    {
                        await ProcessOutgoingCommandsAsync(modem, db, stoppingToken);
                    }

                    // 3. Вычитка входящих SMS
                    var messages = (modem != null && modem.IsConnected)
                        ? modem.FetchAndPurgeSms()
                        : new List<DecodedSms>();

                    if (messages.Count > 0)
                    {
                        _logger.LogInformation("Получено новых SMS: {Count}", messages.Count);

                        foreach (var sms in messages)
                        {
                            string cleanPhone = PhoneNumber.Normalize(sms.SenderNumber);
                            _logger.LogInformation("Обработка SMS от {Phone} (длина={Len})", string.IsNullOrEmpty(cleanPhone) ? sms.SenderNumber : cleanPhone, sms.Text?.Length ?? 0);

                            var obj = !string.IsNullOrEmpty(cleanPhone)
                                ? await db.Objects.FirstOrDefaultAsync(o => o.PhoneNumber == cleanPhone, stoppingToken)
                                : null;

                            if (obj != null)
                            {
                                var parser = _parserRegistry.Resolve(sms.Text ?? string.Empty, obj.DeviceType);
                                var snapshot = parser.Parse(sms.Text ?? string.Empty, sms.Timestamp, cleanPhone);
                                snapshot.DeviceName = obj.Name;

                                await db.SaveReportAsync(cleanPhone, snapshot, stoppingToken);

                                if (snapshot.IsAlarm)
                                {
                                    _logger.LogWarning("!!! ТРЕВОГА по объекту {Obj} ({Phone}): {Desc}",
                                        snapshot.DeviceName, cleanPhone, snapshot.AlarmDescription);
                                }
                                else
                                {
                                    _logger.LogInformation("Телеметрия сохранена: [{Dev}] Объект={Obj}, 220V={Pwr}",
                                        parser.SupportedDeviceType, snapshot.DeviceName, snapshot.MainPower);
                                }
                            }
                            else
                            {
                                _logger.LogInformation("SMS от неизвестного отправителя/оператора ({Phone}). Запись в Журнал событий.", sms.SenderNumber);

                                db.Alarms.Add(new AlarmEvent
                                {
                                    MonitoredObjectId = null,
                                    Timestamp = sms.Timestamp != default ? sms.Timestamp : DateTime.UtcNow,
                                    Description = $"Служебное SMS от {sms.SenderNumber}: {sms.Text}",
                                    EventType = "Service",
                                    IsAcknowledged = true,
                                    AcknowledgedAt = DateTime.UtcNow
                                });

                                await db.SaveChangesAsync(stoppingToken);
                                await db.RotateJournalEventsAsync(100, stoppingToken);
                            }
                        }
                    }

                    // 4. Обновление системного Heartbeat для UI
                    await db.UpdateWorkerHeartbeatAsync(
                        portName: _currentPortName,
                        isModemConnected: true,
                        baudRate: modem?.CurrentBaudRate ?? _baudRate,
                        signalCsq: lastSignalCsq,
                        operatorName: cachedOperatorName,
                        lastError: null,
                        newSmsProcessed: messages.Count,
                        cancellationToken: stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка цикла опроса модема. Порт {Port}. Повтор через {Sec} сек...", _currentPortName, _pollIntervalSec);
                    modem?.Dispose();
                    modem = null;

                    try
                    {
                        using var db = new AppDbContext(_dbPath);
                        await db.UpdateWorkerHeartbeatAsync(
                            portName: _currentPortName,
                            isModemConnected: false,
                            baudRate: _baudRate,
                            lastError: ex.Message,
                            cancellationToken: stoppingToken);
                    }
                    catch { }
                }

                await Task.Delay(TimeSpan.FromSeconds(_pollIntervalSec), stoppingToken);
            }

            modem?.Dispose();
            _logger.LogInformation("Сервис мониторинга остановлен.");
        }
        finally
        {
            if (hasHandle)
            {
                try { singleInstanceMutex.ReleaseMutex(); } catch { }
            }
        }
    }

    private static string BuildWorkerMutexName(string dbPath)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(dbPath.ToLowerInvariant()));
        string shortHash = Convert.ToHexString(hash.AsSpan(0, 8));
        return $"Local\\KsitalTelemetryWorker_{shortHash}";
    }

    private async Task ProcessOutgoingCommandsAsync(GsmModemClient modem, AppDbContext db, CancellationToken ct)
    {
        try
        {
            var pendingCommands = await db.OutgoingCommands
                .Include(c => c.MonitoredObject)
                .Where(c => c.Status == CommandStatus.Pending)
                .OrderBy(c => c.CreatedAt)
                .Take(5)
                .ToListAsync(ct);

            foreach (var cmd in pendingCommands)
            {
                if (ct.IsCancellationRequested) break;

                string cleanPhone = PhoneNumber.Normalize(cmd.PhoneNumber);
                string plainPass = DevicePasswordProtector.Unprotect(cmd.MonitoredObject?.DevicePassword);
                string maskedPayload = DevicePasswordProtector.MaskPasswordInPayload(cmd.RawPayload, plainPass);

                _logger.LogInformation("Отправка SMS-команды #{Id} на {Phone}: \"{Payload}\"", cmd.Id, cleanPhone, maskedPayload);

                bool success = false;
                string? sendError = null;
                try
                {
                    success = modem.SendSms(cleanPhone, cmd.RawPayload);
                }
                catch (Exception ex)
                {
                    sendError = ex.Message;
                }

                if (success)
                {
                    cmd.Status = CommandStatus.Sent;
                    cmd.SentAt = DateTime.UtcNow;
                    cmd.ErrorMessage = null;
                    _logger.LogInformation("Команда #{Id} успешно отправлена", cmd.Id);

                    db.Alarms.Add(new AlarmEvent
                    {
                        MonitoredObjectId = cmd.MonitoredObjectId,
                        Timestamp = DateTime.UtcNow,
                        Description = $"Отправлена команда: {cmd.Description} [SMS: {maskedPayload}]",
                        EventType = "Command",
                        IsAcknowledged = true,
                        AcknowledgedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    cmd.Status = CommandStatus.Failed;
                    cmd.ErrorMessage = sendError ?? "Ошибка отправки через модем (таймаут или сбой сети)";
                    _logger.LogWarning("Сбой отправки команды #{Id}: {Reason}", cmd.Id, cmd.ErrorMessage);

                    db.Alarms.Add(new AlarmEvent
                    {
                        MonitoredObjectId = cmd.MonitoredObjectId,
                        Timestamp = DateTime.UtcNow,
                        Description = $"Ошибка отправки команды: {cmd.Description} [SMS: {maskedPayload}]",
                        EventType = "Command",
                        IsAcknowledged = true,
                        AcknowledgedAt = DateTime.UtcNow
                    });
                }

                await db.SaveChangesAsync(ct);
                await db.RotateJournalEventsAsync(100, ct);
                await Task.Delay(1000, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки очереди OutgoingCommands");
        }
    }
}
