using Xunit;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Parser.Ksital;

namespace KsitalTelemetryHub.Parser.Tests;

public class KsitalMessageParserTests
{
    [Fact]
    public void Should_Parse_Standard_Boiler_Report()
    {
        string sampleText = 
            "Котельная №1\n" +
            "220V: Есть\n" +
            "Асс: 12.6V\n" +
            "T1=+72.5 T2=+55.0 T3=+22.0\n" +
            "З1:Норма З2:Норма З3:Сработка\n" +
            "Баланс: 250.40р";

        var sms = new DecodedSms
        {
            SenderNumber = "+79160001122",
            Timestamp = DateTime.UtcNow,
            Text = sampleText
        };

        var report = KsitalMessageParser.Parse(sms);

        Assert.Equal("Котельная №1", report.DeviceName);
        Assert.Equal(PowerState.Normal, report.MainPower);
        Assert.Equal(12.6, report.BatteryVoltage);
        Assert.Equal(250.40m, report.SimBalance);

        // Проверка температур (Т1 - подача, Т2 - обратка)
        Assert.Equal(72.5, report.Temperatures["T1"]);
        Assert.Equal(55.0, report.Temperatures["T2"]);
        Assert.Equal(22.0, report.Temperatures["T3"]);

        // Проверка зон (З3 - тревога/загазованность)
        Assert.Equal(ZoneState.Normal, report.Zones[1]);
        Assert.Equal(ZoneState.Normal, report.Zones[2]);
        Assert.Equal(ZoneState.Triggered, report.Zones[3]);
    }

    [Fact]
    public void Should_Identify_Alarm_Message()
    {
        var sms = new DecodedSms
        {
            SenderNumber = "+79160001122",
            Timestamp = DateTime.UtcNow,
            Text = "Тревога! Вскрытие двери котельной З1"
        };

        var report = KsitalMessageParser.Parse(sms);

        Assert.True(report.IsAlarm);
        Assert.Contains("Тревога!", report.AlarmDescription);
    }

    [Fact]
    public void Should_Parse_Cyrillic_Temperature_And_Trigger_Alarm_On_Zone_Or_PowerOff()
    {
        string raw = "Котельная №2\n220V: Нет\nТ1=+65.0 T2=+42.0\nЗ1:Норма Z2:Обрыв";
        var report = KsitalMessageParser.Parse(raw);

        Assert.Equal(PowerState.Off, report.MainPower);
        Assert.True(report.IsAlarm);
        Assert.Equal(65.0, report.Temperatures["T1"]);
        Assert.Equal(42.0, report.Temperatures["T2"]);
        Assert.Equal(ZoneState.OpenCircuit, report.Zones[2]);
        Assert.Contains("Обрыв", report.AlarmDescription);
    }

    [Fact]
    public void DevicePasswordProtector_EncryptsDecryptsAndMasksPayload()
    {
        string tempKeyDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"keytest_{Guid.NewGuid():N}");
        try
        {
            string encrypted = DevicePasswordProtector.Protect("98765", tempKeyDir);
            Assert.StartsWith(DevicePasswordProtector.Prefix, encrypted);
            Assert.NotEqual("98765", encrypted);

            string decrypted = DevicePasswordProtector.Unprotect(encrypted, tempKeyDir);
            Assert.Equal("98765", decrypted);

            // Обратная совместимость со старыми незашифрованными паролями
            Assert.Equal("00000", DevicePasswordProtector.Unprotect("00000", tempKeyDir));

            // Маскировка пароля в логах и журнале
            Assert.Equal("Otkl control ***", DevicePasswordProtector.MaskPasswordInPayload("Otkl control 98765", "98765"));
            Assert.Equal("/*** DISARM !", DevicePasswordProtector.MaskPasswordInPayload("/mySecret DISARM !", "mySecret"));
            Assert.Equal("PASS:*** CMD:RESET", DevicePasswordProtector.MaskPasswordInPayload("PASS:4321 CMD:RESET", "4321"));
        }
        finally
        {
            if (System.IO.Directory.Exists(tempKeyDir))
            {
                try { System.IO.Directory.Delete(tempKeyDir, true); } catch { }
            }
        }
    }
}