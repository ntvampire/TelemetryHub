using Microsoft.EntityFrameworkCore;
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
        _baudRate = _configuration.GetValue<int>("ModemSettings:BaudRate", 115200);
        _pollIntervalSec = _configuration.GetValue<int>("ModemSettings:PollIntervalSeconds", 10);
        _dbPath = _configuration.GetValue<string>("DatabaseSettings:DbPath") ?? "telemetry.db";

        // Единый реестр парсеров для всех типов объектовых контроллеров
        _parserRegistry = new TelemetryParserRegistry(new ITelemetryParser[]
        {
            new KsitalMessageParser(),
            new Ccu825MessageParser(),
            new OwenMessageParser()
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Инициализация базы данных SQLite ({DbPath})...", _dbPath);
        AppDbContext.EnsureDatabaseUpdated(_dbPath);

        _logger.LogInformation("Запуск сервиса мониторинга телеметрии. Порт: {Port}, Скорость: {Baud}", _currentPortName, _baudRate);

        GsmModemClient? modem = null;

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
                    _logger.LogInformation("Модем успешно подключен.");
                }

                // 2. Отправка очереди исходящих команд оператора
                await ProcessOutgoingCommandsAsync(modem, db, stoppingToken);

                // 3. Вычитка входящих SMS
                var messages = modem.FetchAndPurgeSms();

                if (messages.Count > 0)
                {
                    _logger.LogInformation("Получено новых SMS: {Count}", messages.Count);

                    foreach (var sms in messages)
                    {
                        string cleanPhone = PhoneNumber.Normalize(sms.SenderNumber);
                        _logger.LogInformation("Обработка SMS от {Phone}: \"{Text}\"", cleanPhone, sms.Text);

                        // Поиск объекта в базе по нормализованному номеру телефона
                        var obj = await db.Objects.FirstOrDefaultAsync(o => o.PhoneNumber == cleanPhone, stoppingToken);

                        // Разрешение парсера (по типу объекта из БД либо по структуре текста)
                        var parser = _parserRegistry.Resolve(sms.Text, obj?.DeviceType);
                        var snapshot = parser.Parse(sms.Text, sms.Timestamp, cleanPhone);

                        if (obj != null)
                        {
                            snapshot.DeviceName = obj.Name;
                        }

                        // Сохранение снимка телеметрии и фиксация тревог
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
                }

                // 4. Обновление системного Heartbeat для UI (без коллизий COM-порта)
                await db.UpdateWorkerHeartbeatAsync(
                    portName: _currentPortName,
                    isModemConnected: true,
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

    private async Task ProcessOutgoingCommandsAsync(GsmModemClient modem, AppDbContext db, CancellationToken ct)
    {
        try
        {
            var pendingCommands = await db.OutgoingCommands
                .Where(c => c.Status == CommandStatus.Pending)
                .OrderBy(c => c.CreatedAt)
                .Take(5)
                .ToListAsync(ct);

            foreach (var cmd in pendingCommands)
            {
                if (ct.IsCancellationRequested) break;

                string cleanPhone = PhoneNumber.Normalize(cmd.PhoneNumber);
                _logger.LogInformation("Отправка SMS-команды #{Id} на {Phone}: \"{Payload}\"", cmd.Id, cleanPhone, cmd.RawPayload);

                bool success = modem.SendSms(cleanPhone, cmd.RawPayload);

                if (success)
                {
                    cmd.Status = CommandStatus.Sent;
                    cmd.SentAt = DateTime.UtcNow;
                    cmd.ErrorMessage = null;
                    _logger.LogInformation("Команда #{Id} успешно отправлена", cmd.Id);
                }
                else
                {
                    cmd.Status = CommandStatus.Failed;
                    cmd.ErrorMessage = "Ошибка отправки через модем (таймаут или сбой сети)";
                    _logger.LogWarning("Сбой отправки команды #{Id}", cmd.Id);
                }

                await db.SaveChangesAsync(ct);
                await Task.Delay(1000, ct); // Пауза для стабилизации GSM-тракта
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки очереди OutgoingCommands");
        }
    }
}
