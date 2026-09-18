using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Storage.Sqlite;
using KsitalTelemetryHub.UI.WinUI.Models;
using KsitalTelemetryHub.UI.WinUI.ViewModels;
using KsitalTelemetryHub.UI.WinUI.Views;
using WinUIEx;

namespace KsitalTelemetryHub.UI.WinUI.Services;

public static class AlarmManager
{
    private static readonly Dictionary<long, DateTime> _snoozedAlarms = new();
    private static readonly object _snoozeLock = new();
    private static AlarmWindow? _currentAlarmWindow;

    public static bool IsSnoozed(long alarmId)
    {
        lock (_snoozeLock)
        {
            if (_snoozedAlarms.TryGetValue(alarmId, out var until))
            {
                return DateTime.UtcNow < until;
            }
            return false;
        }
    }

    public static async Task SnoozeAlarmAsync(AlarmDisplayItem alarm, int minutes = 5, string reason = "по кнопке «Отложить»")
    {
        lock (_snoozeLock)
        {
            _snoozedAlarms[alarm.Id] = DateTime.UtcNow.AddMinutes(minutes);
        }

        try
        {
            using var db = new AppDbContext(App.DatabasePath);
            var snoozeEvent = new AlarmEvent
            {
                MonitoredObjectId = alarm.MonitoredObjectId,
                Timestamp = DateTime.UtcNow,
                Description = $"Тревога по объекту '{alarm.ObjectName}' отложена на {minutes} мин ({reason}): {alarm.Description}",
                EventType = "Service",
                IsAcknowledged = true
            };
            db.Alarms.Add(snoozeEvent);
            await db.SaveChangesAsync();
            await db.RotateJournalEventsAsync(100);
        }
        catch (Exception ex)
        {
            App.LogError("AlarmManager.SnoozeAlarmAsync", ex);
        }
    }

    public static async Task AcknowledgeAlarmAsync(AlarmDisplayItem alarm, MainViewModel vm)
    {
        lock (_snoozeLock)
        {
            _snoozedAlarms.Remove(alarm.Id);
        }

        try
        {
            using var db = new AppDbContext(App.DatabasePath);
            var dbAlarm = await db.Alarms.FirstOrDefaultAsync(a => a.Id == alarm.Id);
            if (dbAlarm != null)
            {
                dbAlarm.IsAcknowledged = true;
                dbAlarm.AcknowledgedAt = DateTime.UtcNow;
            }

            var ackEvent = new AlarmEvent
            {
                MonitoredObjectId = alarm.MonitoredObjectId,
                Timestamp = DateTime.UtcNow,
                Description = $"Тревога подтверждена оператором: {alarm.ObjectName} ({alarm.Description})",
                EventType = "Service",
                IsAcknowledged = true
            };
            db.Alarms.Add(ackEvent);
            await db.SaveChangesAsync();
            await db.RotateJournalEventsAsync(100);
        }
        catch (Exception ex)
        {
            App.LogError("AlarmManager.AcknowledgeAlarmAsync", ex);
        }

        await vm.RefreshDataAsync();
    }

    public static async Task CheckSnoozedAlarmsAsync(MainViewModel vm, Action<AlarmDisplayItem> onReAlert)
    {
        List<long> expiredAlarmIds = new();
        lock (_snoozeLock)
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _snoozedAlarms)
            {
                if (now >= kv.Value)
                {
                    expiredAlarmIds.Add(kv.Key);
                }
            }
        }

        if (expiredAlarmIds.Count == 0) return;

        try
        {
            using var db = new AppDbContext(App.DatabasePath);
            foreach (var id in expiredAlarmIds)
            {
                lock (_snoozeLock)
                {
                    _snoozedAlarms.Remove(id);
                }

                var dbAlarm = await db.Alarms.FirstOrDefaultAsync(a => a.Id == id && !a.IsAcknowledged && a.EventType == "Alarm");
                if (dbAlarm != null)
                {
                    var relatedObj = await db.Objects.FirstOrDefaultAsync(o => o.Id == dbAlarm.MonitoredObjectId);

                    var reAlertEvent = new AlarmEvent
                    {
                        MonitoredObjectId = dbAlarm.MonitoredObjectId,
                        Timestamp = DateTime.UtcNow,
                        Description = $"Истекло время отложения (5 мин). Повторное оповещение о тревоге: {relatedObj?.Name ?? "Объект"} ({dbAlarm.Description})",
                        EventType = "Service",
                        IsAcknowledged = true
                    };
                    db.Alarms.Add(reAlertEvent);
                    await db.SaveChangesAsync();
                    await db.RotateJournalEventsAsync(100);

                    string devTypeName = relatedObj?.DeviceType switch
                    {
                        DeviceType.Ksital => "КСИТАЛ GSM",
                        DeviceType.Ccu825 => "CCU-825",
                        DeviceType.OwenPlc => "ОВЕН ПЛК",
                        _ => "Контроллер"
                    };

                    var displayItem = new AlarmDisplayItem
                    {
                        Id = dbAlarm.Id,
                        MonitoredObjectId = dbAlarm.MonitoredObjectId,
                        ObjectName = relatedObj?.Name ?? $"Объект #{dbAlarm.MonitoredObjectId}",
                        PhoneNumber = relatedObj?.PhoneNumber ?? "",
                        District = relatedObj?.District ?? "Основной участок",
                        DeviceTypeName = devTypeName,
                        Timestamp = dbAlarm.Timestamp,
                        Description = dbAlarm.Description,
                        IsAcknowledged = false,
                        EventType = "Alarm"
                    };

                    onReAlert(displayItem);
                }
            }
        }
        catch (Exception ex)
        {
            App.LogError("AlarmManager.CheckSnoozedAlarmsAsync", ex);
        }
    }

    public static void ShowAlarm(AlarmDisplayItem alarm, MainViewModel vm)
    {
        if (IsSnoozed(alarm.Id)) return;

        if (vm.IsSoundAlarmEnabled)
        {
            SoundService.PlayAlarmSound();
        }

        if (_currentAlarmWindow != null)
        {
            try
            {
                _currentAlarmWindow.AddAlarm(alarm);
                _currentAlarmWindow.BringToFront();
                return;
            }
            catch
            {
                _currentAlarmWindow = null;
            }
        }

        try
        {
            _currentAlarmWindow = new AlarmWindow(alarm, vm);
            _currentAlarmWindow.Closed += (s, e) =>
            {
                _currentAlarmWindow = null;
            };
            _currentAlarmWindow.Activate();
            _currentAlarmWindow.BringToFront();
        }
        catch (Exception ex)
        {
            App.LogError("AlarmManager.ShowAlarm", ex);
        }
    }
}
