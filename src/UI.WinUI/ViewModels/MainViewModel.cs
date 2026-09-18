using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Media;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Storage.Sqlite;
using KsitalTelemetryHub.UI.WinUI.Models;

namespace KsitalTelemetryHub.UI.WinUI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly string _dbPath;
    private readonly List<ObjectDisplayItem> _allLoadedObjects = new();
    private readonly HashSet<long> _knownAlarmIds = new();
    private bool _isFirstLoad = true;

    public event Action<AlarmDisplayItem>? NewAlarmArrived;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    [ObservableProperty]
    private ObservableCollection<ObjectDisplayItem> _objects = new();

    [ObservableProperty]
    private ObservableCollection<ObjectDistrictGroup> _groupedObjects = new();

    [ObservableProperty]
    private ObservableCollection<AlarmDisplayItem> _activeAlarms = new();

    [ObservableProperty]
    private ObservableCollection<OutgoingCommandDisplayItem> _commandQueue = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    [ObservableProperty]
    private string _comPortStatus = "COM: Ожидание";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ComStatusColor))]
    [NotifyPropertyChangedFor(nameof(ComStatusBrush))]
    private bool _isComConnected;

    public string ComStatusColor => IsComConnected ? "#2ECC71" : "#E74C3C";
    public SolidColorBrush ComStatusBrush => IsComConnected ? ObjectDisplayItem.GreenBrush : ObjectDisplayItem.RedBrush;

    [ObservableProperty]
    private string _modemStatus = "Модем: Ожидание";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModemStatusColor))]
    [NotifyPropertyChangedFor(nameof(ModemStatusBrush))]
    private bool _isModemConnected;

    public string ModemStatusColor => IsModemConnected ? "#2ECC71" : "#E74C3C";
    public SolidColorBrush ModemStatusBrush => IsModemConnected ? ObjectDisplayItem.GreenBrush : ObjectDisplayItem.RedBrush;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnacknowledgedBadgeVisibility))]
    private int _unacknowledgedAlarmsCount;

    public Microsoft.UI.Xaml.Visibility UnacknowledgedBadgeVisibility => UnacknowledgedAlarmsCount > 0
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

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

            // 1. Аппаратный статус службы (IPC без коллизий COM-порта)
            var status = await db.SystemStatus.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);
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

            // 2. Список объектов и пакетная выборка последних данных телеметрии (без N+1 запросов)
            var dbObjects = await db.Objects.AsNoTracking().ToListAsync();
            var objectIds = dbObjects.Select(o => o.Id).ToList();

            // Пакетное получение ID последних записей телеметрии для каждого объекта
            var latestTelemetryIds = await db.Telemetry
                .Where(t => objectIds.Contains(t.MonitoredObjectId))
                .GroupBy(t => t.MonitoredObjectId)
                .Select(g => g.Max(t => t.Id))
                .ToListAsync();

            var latestRecords = await db.Telemetry
                .Where(t => latestTelemetryIds.Contains(t.Id))
                .Include(t => t.Temperatures)
                .AsNoTracking()
                .ToDictionaryAsync(t => t.MonitoredObjectId);

            // 3. Активные неподтвержденные тревоги для карточек объектов
            var unackAlarms = await db.Alarms
                .Where(a => !a.IsAcknowledged && a.EventType == "Alarm")
                .OrderByDescending(a => a.Timestamp)
                .AsNoTracking()
                .ToListAsync();

            var activeAlarmsByObj = unackAlarms
                .Where(a => a.MonitoredObjectId.HasValue)
                .GroupBy(a => a.MonitoredObjectId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            // 4. Синхронизация списка объектов в памяти
            var currentMasterList = new List<ObjectDisplayItem>();
            foreach (var obj in dbObjects)
            {
                latestRecords.TryGetValue(obj.Id, out var lastRecord);
                activeAlarmsByObj.TryGetValue(obj.Id, out var activeAlarm);

                var item = new ObjectDisplayItem
                {
                    Id = obj.Id,
                    Name = obj.Name,
                    PhoneNumber = obj.PhoneNumber,
                    District = string.IsNullOrWhiteSpace(obj.District) ? "Основной участок" : obj.District,
                    DeviceType = obj.DeviceType,
                    DevicePassword = obj.DevicePassword,
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

                currentMasterList.Add(item);
            }

            _allLoadedObjects.Clear();
            _allLoadedObjects.AddRange(currentMasterList);
            ApplyFilter();

            // 5. Загрузка Журнала событий (до 100 последних записей всех типов)
            var journalEvents = await db.Alarms
                .OrderByDescending(a => a.Timestamp)
                .Take(100)
                .AsNoTracking()
                .ToListAsync();

            var eventDisplays = new List<AlarmDisplayItem>();
            var newAlarms = new List<AlarmDisplayItem>();

            foreach (var a in journalEvents)
            {
                var relatedObj = dbObjects.FirstOrDefault(o => o.Id == a.MonitoredObjectId);
                string devTypeName = relatedObj?.DeviceType switch
                {
                    DeviceType.Ksital => "КСИТАЛ GSM",
                    DeviceType.Ccu825 => "CCU-825",
                    DeviceType.OwenPlc => "ОВЕН ПЛК",
                    _ => "Контроллер"
                };

                bool isServiceOrUnassigned = !a.MonitoredObjectId.HasValue || a.MonitoredObjectId.Value <= 0;
                var displayItem = new AlarmDisplayItem
                {
                    Id = a.Id,
                    MonitoredObjectId = a.MonitoredObjectId,
                    ObjectName = relatedObj?.Name ?? (isServiceOrUnassigned ? "Служебные сообщения" : $"Объект #{a.MonitoredObjectId}"),
                    PhoneNumber = relatedObj?.PhoneNumber ?? "",
                    District = string.IsNullOrWhiteSpace(relatedObj?.District) ? (isServiceOrUnassigned ? "Система" : "Основной участок") : relatedObj.District,
                    DeviceTypeName = isServiceOrUnassigned ? "Служба" : devTypeName,
                    Timestamp = a.Timestamp,
                    Description = a.Description,
                    IsAcknowledged = a.IsAcknowledged,
                    EventType = a.EventType
                };
                eventDisplays.Add(displayItem);

                if (!_knownAlarmIds.Contains(a.Id))
                {
                    _knownAlarmIds.Add(a.Id);
                    if (!_isFirstLoad && a.EventType == "Alarm" && !a.IsAcknowledged)
                    {
                        newAlarms.Add(displayItem);
                    }
                }
            }
            _isFirstLoad = false;

            ActiveAlarms = new ObservableCollection<AlarmDisplayItem>(eventDisplays);
            UnacknowledgedAlarmsCount = unackAlarms.Count;

            if (newAlarms.Count > 0)
            {
                foreach (var na in newAlarms)
                {
                    NewAlarmArrived?.Invoke(na);
                }
            }

            // 5.1. Проверка истечения 5-минутного периода отложения тревог
            await Services.AlarmManager.CheckSnoozedAlarmsAsync(this, na =>
            {
                NewAlarmArrived?.Invoke(na);
            });

            // 6. Очередь команд
            var commands = await db.OutgoingCommands
                .OrderByDescending(c => c.CreatedAt)
                .Take(25)
                .AsNoTracking()
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel.RefreshDataAsync] Ошибка обновления данных: {ex.Message}");
            App.LogError("MainViewModel.RefreshDataAsync", ex);
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<ObjectDisplayItem> query = _allLoadedObjects;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(o =>
                o.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                o.PhoneNumber.Contains(SearchText) ||
                o.District.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        var sorted = query.OrderBy(o => o.District).ThenBy(o => o.Name).ToList();
        SyncCollection(sorted);
        SyncGroupedCollection(sorted);
    }

    private void SyncGroupedCollection(List<ObjectDisplayItem> sortedItems)
    {
        var districtGroups = sortedItems
            .GroupBy(o => string.IsNullOrWhiteSpace(o.District) ? "Основной участок" : o.District.Trim())
            .OrderBy(g => g.Key)
            .ToList();

        // 1. Удаляем группы, которых больше нет
        for (int i = GroupedObjects.Count - 1; i >= 0; i--)
        {
            if (!districtGroups.Any(g => string.Equals(g.Key, GroupedObjects[i].District, StringComparison.OrdinalIgnoreCase)))
            {
                GroupedObjects.RemoveAt(i);
            }
        }

        // 2. Добавляем или обновляем группы с сохранением порядка
        for (int gIdx = 0; gIdx < districtGroups.Count; gIdx++)
        {
            var g = districtGroups[gIdx];
            var existingGroup = GroupedObjects.FirstOrDefault(dg => string.Equals(dg.District, g.Key, StringComparison.OrdinalIgnoreCase));

            if (existingGroup == null)
            {
                var newGroup = new ObjectDistrictGroup(g.Key, g);
                if (gIdx <= GroupedObjects.Count)
                {
                    GroupedObjects.Insert(gIdx, newGroup);
                }
                else
                {
                    GroupedObjects.Add(newGroup);
                }
            }
            else
            {
                int currentIndex = GroupedObjects.IndexOf(existingGroup);
                if (currentIndex != gIdx && gIdx < GroupedObjects.Count)
                {
                    GroupedObjects.Move(currentIndex, gIdx);
                }

                // Синхронизируем элементы внутри группы
                var targetGroupItems = g.ToList();
                SyncGroupItems(existingGroup.Items, targetGroupItems);
                existingGroup.ActiveAlarmCount = existingGroup.Items.Count(i => i.HasActiveAlarm);
                existingGroup.NotifyCountChanged();
            }
        }
    }

    private void SyncGroupItems(ObservableCollection<ObjectDisplayItem> currentItems, List<ObjectDisplayItem> targetItems)
    {
        for (int i = currentItems.Count - 1; i >= 0; i--)
        {
            if (!targetItems.Any(t => t.Id == currentItems[i].Id))
            {
                currentItems.RemoveAt(i);
            }
        }

        for (int i = 0; i < targetItems.Count; i++)
        {
            var target = targetItems[i];
            int existingIndex = -1;
            for (int j = 0; j < currentItems.Count; j++)
            {
                if (currentItems[j].Id == target.Id)
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                currentItems[existingIndex].UpdateFrom(target);
                if (existingIndex != i && i < currentItems.Count)
                {
                    currentItems.Move(existingIndex, i);
                }
            }
            else
            {
                if (i <= currentItems.Count)
                {
                    currentItems.Insert(i, target);
                }
                else
                {
                    currentItems.Add(target);
                }
            }
        }
    }

    private void SyncCollection(List<ObjectDisplayItem> targetItems)
    {
        // 1. Удаляем элементы, которых больше нет в целевом наборе
        for (int i = Objects.Count - 1; i >= 0; i--)
        {
            if (!targetItems.Any(t => t.Id == Objects[i].Id))
            {
                Objects.RemoveAt(i);
            }
        }

        // 2. Обновляем существующие элементы на месте или вставляем новые (без пересоздания списка)
        for (int i = 0; i < targetItems.Count; i++)
        {
            var target = targetItems[i];
            int existingIndex = -1;
            for (int j = 0; j < Objects.Count; j++)
            {
                if (Objects[j].Id == target.Id)
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                Objects[existingIndex].UpdateFrom(target);
                if (existingIndex != i && i < Objects.Count)
                {
                    Objects.Move(existingIndex, i);
                }
            }
            else
            {
                if (i <= Objects.Count)
                {
                    Objects.Insert(i, target);
                }
                else
                {
                    Objects.Add(target);
                }
            }
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
            alarm.AcknowledgedAt = DateTime.UtcNow;

            var relatedObj = await db.Objects.FirstOrDefaultAsync(o => o.Id == alarm.MonitoredObjectId);
            var ackEvent = new AlarmEvent
            {
                MonitoredObjectId = alarm.MonitoredObjectId,
                Timestamp = DateTime.UtcNow,
                Description = $"Тревога подтверждена оператором: {relatedObj?.Name ?? "Объект"} ({alarm.Description})",
                EventType = "Service",
                IsAcknowledged = true
            };
            db.Alarms.Add(ackEvent);

            await db.SaveChangesAsync();
            await db.RotateJournalEventsAsync(100);
            await RefreshDataAsync();
        }
    }

    [RelayCommand]
    public async Task AcknowledgeAllAlarmsAsync()
    {
        using var db = new AppDbContext(_dbPath);
        var unackAlarms = await db.Alarms.Where(a => !a.IsAcknowledged && a.EventType == "Alarm").ToListAsync();
        if (unackAlarms.Count > 0)
        {
            foreach (var a in unackAlarms)
            {
                a.IsAcknowledged = true;
                a.AcknowledgedAt = DateTime.UtcNow;
            }

            var ackAllEvent = new AlarmEvent
            {
                MonitoredObjectId = unackAlarms[0].MonitoredObjectId,
                Timestamp = DateTime.UtcNow,
                Description = $"Все активные тревоги ({unackAlarms.Count}) подтверждены оператором",
                EventType = "Service",
                IsAcknowledged = true
            };
            db.Alarms.Add(ackAllEvent);

            await db.SaveChangesAsync();
            await db.RotateJournalEventsAsync(100);
            await RefreshDataAsync();
        }
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

        // Фиксация команды оператора в журнале событий
        db.Alarms.Add(new AlarmEvent
        {
            MonitoredObjectId = obj.Id,
            Timestamp = DateTime.UtcNow,
            Description = $"Запрос оператора: {description} [SMS: {rawPayload}]",
            EventType = "Command",
            IsAcknowledged = true,
            AcknowledgedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        await db.RotateJournalEventsAsync(100);
        await RefreshDataAsync();
    }

    [RelayCommand]
    public async Task DeleteObjectAsync(int objectId)
    {
        using var db = new AppDbContext(_dbPath);
        var obj = await db.Objects.FirstOrDefaultAsync(o => o.Id == objectId);
        if (obj != null)
        {
            var alarms = db.Alarms.Where(a => a.MonitoredObjectId == objectId);
            db.Alarms.RemoveRange(alarms);

            var telemetry = db.Telemetry.Where(t => t.MonitoredObjectId == objectId);
            db.Telemetry.RemoveRange(telemetry);

            var commands = db.OutgoingCommands.Where(c => c.MonitoredObjectId == objectId);
            db.OutgoingCommands.RemoveRange(commands);

            db.Objects.Remove(obj);
            await db.SaveChangesAsync();
            await RefreshDataAsync();
        }
    }
}
