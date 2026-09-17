using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Storage.Sqlite;
using KsitalTelemetryHub.UI.WinUI.Models;

namespace KsitalTelemetryHub.UI.WinUI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly string _dbPath;

    [ObservableProperty]
    private ObservableCollection<ObjectDisplayItem> _objects = new();

    [ObservableProperty]
    private ObservableCollection<AlarmDisplayItem> _activeAlarms = new();

    [ObservableProperty]
    private ObservableCollection<OutgoingCommandDisplayItem> _commandQueue = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _comPortStatus = "COM: Ожидание";

    [ObservableProperty]
    private bool _isComConnected;

    [ObservableProperty]
    private string _modemStatus = "Модем: Ожидание";

    [ObservableProperty]
    private bool _isModemConnected;

    [ObservableProperty]
    private int _unacknowledgedAlarmsCount;

    [ObservableProperty]
    private bool _isSoundAlarmEnabled = true;

    [ObservableProperty]
    private ObjectDisplayItem? _selectedObject;

    public MainViewModel()
    {
        _dbPath = App.DatabasePath;
    }

    public async Task InitializeAsync()
    {
        await RefreshDataAsync();
    }

    [RelayCommand]
    public async Task RefreshDataAsync()
    {
        try
        {
            using var db = new AppDbContext(_dbPath);

            // 1. Аппаратный статус службы (IPC без коллизий порта)
            var status = await db.SystemStatus.FirstOrDefaultAsync(s => s.Id == 1);
            if (status != null && (DateTime.UtcNow - status.LastHeartbeat).TotalSeconds < 30)
            {
                IsComConnected = true;
                ComPortStatus = $"COM: {status.PortName}";
                IsModemConnected = status.IsModemConnected;
                ModemStatus = status.IsModemConnected 
                    ? (status.SignalStrengthCsq > 0 ? $"Модем: CSQ {status.SignalStrengthCsq}" : "Модем: ОК")
                    : "Модем: Ошибка";
            }
            else
            {
                IsComConnected = false;
                ComPortStatus = "Служба сбора: Остановлена";
                IsModemConnected = false;
                ModemStatus = "Модем: Нет связи";
            }

            // 2. Список объектов и последние показатели телеметрии
            var dbObjects = await db.Objects.AsNoTracking().ToListAsync();
            var displayList = new List<ObjectDisplayItem>();

            foreach (var obj in dbObjects)
            {
                var lastRecord = await db.Telemetry
                    .Where(t => t.MonitoredObjectId == obj.Id)
                    .OrderByDescending(t => t.Timestamp)
                    .Include(t => t.Temperatures)
                    .FirstOrDefaultAsync();

                var activeAlarm = await db.Alarms
                    .Where(a => a.MonitoredObjectId == obj.Id && !a.IsAcknowledged)
                    .OrderByDescending(a => a.Timestamp)
                    .FirstOrDefaultAsync();

                var item = new ObjectDisplayItem
                {
                    Id = obj.Id,
                    Name = obj.Name,
                    PhoneNumber = obj.PhoneNumber,
                    District = string.IsNullOrWhiteSpace(obj.District) ? "Основной участок" : obj.District,
                    DeviceType = obj.DeviceType,
                    LastSeen = lastRecord?.Timestamp,
                    MainPower = lastRecord?.MainPower ?? PowerState.Unknown,
                    BatteryVoltage = lastRecord?.BatteryVoltage,
                    HasActiveAlarm = activeAlarm != null,
                    AlarmDescription = activeAlarm?.Description
                };

                if (lastRecord?.Temperatures != null)
                {
                    foreach (var temp in lastRecord.Temperatures)
                    {
                        item.Temperatures[temp.SensorCode] = temp.Value;
                    }
                }

                displayList.Add(item);
            }

            Objects = new ObservableCollection<ObjectDisplayItem>(
                string.IsNullOrWhiteSpace(SearchText)
                    ? displayList.OrderBy(o => o.District).ThenBy(o => o.Name)
                    : displayList.Where(o => o.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || 
                                             o.PhoneNumber.Contains(SearchText) || 
                                             o.District.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                                 .OrderBy(o => o.District).ThenBy(o => o.Name)
            );

            // 3. Активные тревоги
            var alarms = await db.Alarms
                .Where(a => !a.IsAcknowledged)
                .OrderByDescending(a => a.Timestamp)
                .Take(50)
                .ToListAsync();

            var alarmDisplays = new List<AlarmDisplayItem>();
            foreach (var a in alarms)
            {
                var relatedObj = dbObjects.FirstOrDefault(o => o.Id == a.MonitoredObjectId);
                alarmDisplays.Add(new AlarmDisplayItem
                {
                    Id = a.Id,
                    MonitoredObjectId = a.MonitoredObjectId,
                    ObjectName = relatedObj?.Name ?? $"Объект #{a.MonitoredObjectId}",
                    PhoneNumber = relatedObj?.PhoneNumber ?? "",
                    Timestamp = a.Timestamp,
                    Description = a.Description,
                    IsAcknowledged = a.IsAcknowledged
                });
            }

            ActiveAlarms = new ObservableCollection<AlarmDisplayItem>(alarmDisplays);
            UnacknowledgedAlarmsCount = alarmDisplays.Count;

            // 4. Очередь команд
            var commands = await db.OutgoingCommands
                .OrderByDescending(c => c.CreatedAt)
                .Take(25)
                .ToListAsync();

            var cmdDisplays = commands.Select(c =>
            {
                var relatedObj = dbObjects.FirstOrDefault(o => o.Id == c.MonitoredObjectId);
                return new OutgoingCommandDisplayItem
                {
                    Id = c.Id,
                    MonitoredObjectId = c.MonitoredObjectId,
                    ObjectName = relatedObj?.Name ?? $"Объект #{c.MonitoredObjectId}",
                    PhoneNumber = c.PhoneNumber,
                    RawPayload = c.RawPayload,
                    Description = c.Description,
                    CreatedAt = c.CreatedAt,
                    SentAt = c.SentAt,
                    Status = c.Status,
                    ErrorMessage = c.ErrorMessage
                };
            }).ToList();

            CommandQueue = new ObservableCollection<OutgoingCommandDisplayItem>(cmdDisplays);
        }
        catch (Exception)
        {
            // Устойчивость к транзиентным ошибкам доступа SQLite
        }
    }

    [RelayCommand]
    public async Task AcknowledgeAlarmAsync(long alarmId)
    {
        using var db = new AppDbContext(_dbPath);
        var alarm = await db.Alarms.FirstOrDefaultAsync(a => a.Id == alarmId);
        if (alarm != null)
        {
            alarm.IsAcknowledged = true;
            await db.SaveChangesAsync();
            await RefreshDataAsync();
        }
    }

    [RelayCommand]
    public async Task AcknowledgeAllAlarmsAsync()
    {
        using var db = new AppDbContext(_dbPath);
        var unackAlarms = await db.Alarms.Where(a => !a.IsAcknowledged).ToListAsync();
        foreach (var a in unackAlarms)
        {
            a.IsAcknowledged = true;
        }
        await db.SaveChangesAsync();
        await RefreshDataAsync();
    }

    public async Task EnqueueCommandAsync(int objectId, string rawPayload, string description)
    {
        using var db = new AppDbContext(_dbPath);
        var obj = await db.Objects.FirstOrDefaultAsync(o => o.Id == objectId);
        if (obj == null) return;

        var cmd = new OutgoingCommand
        {
            MonitoredObjectId = obj.Id,
            PhoneNumber = obj.PhoneNumber,
            RawPayload = rawPayload,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            Status = CommandStatus.Pending
        };

        db.OutgoingCommands.Add(cmd);
        await db.SaveChangesAsync();
        await RefreshDataAsync();
    }
}
