using System.Linq;
using Xunit;
using KsitalTelemetryHub.Core;

namespace KsitalTelemetryHub.Parser.Tests;

public class DeviceCommandBuilderTests
{
    [Fact]
    public void Ksital_Templates_ShouldContain_SecurityCommands()
    {
        var templates = DeviceCommandBuilder.GetTemplates(DeviceType.Ksital);
        Assert.NotEmpty(templates);

        var armCmd = templates.FirstOrDefault(t => t.Title.Contains("Поставить на контроль"));
        var disarmCmd = templates.FirstOrDefault(t => t.Title.Contains("Снять с контроля"));

        Assert.NotNull(armCmd);
        Assert.NotNull(disarmCmd);

        Assert.Equal("Охрана и контроль", armCmd.Category);
        Assert.Equal("Охрана и контроль", disarmCmd.Category);

        string armPayload = DeviceCommandBuilder.BuildPayload(armCmd.Pattern, "12345");
        Assert.Equal("Ust control 12345", armPayload);

        string disarmPayload = DeviceCommandBuilder.BuildPayload(disarmCmd.Pattern, "12345");
        Assert.Equal("Otkl control 12345", disarmPayload);
    }

    [Fact]
    public void Ksital_Templates_ShouldContain_RelayAndTelemetryCommands()
    {
        var templates = DeviceCommandBuilder.GetTemplates(DeviceType.Ksital);

        var reportCmd = templates.FirstOrDefault(t => t.Title.Contains("Kak dela"));
        var vkl1 = templates.FirstOrDefault(t => t.Title == "Включить реле 1");
        var upr = templates.FirstOrDefault(t => t.Title == "Включить выход УПР");

        Assert.NotNull(reportCmd);
        Assert.NotNull(vkl1);
        Assert.NotNull(upr);

        Assert.Equal("Запросы и телеметрия", reportCmd.Category);
        Assert.Equal("Управление выходами", vkl1.Category);

        Assert.Equal("Vkl 1 00000", DeviceCommandBuilder.BuildPayload(vkl1.Pattern, "00000"));
        Assert.Equal("Upr vkl 00000", DeviceCommandBuilder.BuildPayload(upr.Pattern, "00000"));
    }

    [Fact]
    public void Ccu825_Templates_ShouldContain_SecurityAndRelayCommands()
    {
        var templates = DeviceCommandBuilder.GetTemplates(DeviceType.Ccu825);
        Assert.NotEmpty(templates);

        var armCmd = templates.FirstOrDefault(t => t.Title.Contains("Поставить на контроль"));
        var disarmCmd = templates.FirstOrDefault(t => t.Title.Contains("Снять с контроля"));
        var protectCmd = templates.FirstOrDefault(t => t.Title.Contains("ЗАЩИТА"));
        var out1On = templates.FirstOrDefault(t => t.Title == "Включить выход Out1");

        Assert.NotNull(armCmd);
        Assert.NotNull(disarmCmd);
        Assert.NotNull(protectCmd);
        Assert.NotNull(out1On);

        Assert.Equal("/pass ARM !", DeviceCommandBuilder.BuildPayload(armCmd.Pattern, "pass"));
        Assert.Equal("/pass DISARM !", DeviceCommandBuilder.BuildPayload(disarmCmd.Pattern, "pass"));
        Assert.Equal("/pass PROTECT !", DeviceCommandBuilder.BuildPayload(protectCmd.Pattern, "pass"));
        Assert.Equal("/pass Out1 ON !", DeviceCommandBuilder.BuildPayload(out1On.Pattern, "pass"));
    }

    [Fact]
    public void OwenPlc_Templates_ShouldContain_TelemetryAndResetCommands()
    {
        var templates = DeviceCommandBuilder.GetTemplates(DeviceType.OwenPlc);
        Assert.NotEmpty(templates);

        var tsys = templates.FirstOrDefault(t => t.Title.Contains("TSYS"));
        var psys = templates.FirstOrDefault(t => t.Title.Contains("PSYS"));
        var reset = templates.FirstOrDefault(t => t.Title.Contains("Сброс аварии"));
        var start = templates.FirstOrDefault(t => t.Title.Contains("Пуск контура"));

        Assert.NotNull(tsys);
        Assert.NotNull(psys);
        Assert.NotNull(reset);
        Assert.NotNull(start);

        Assert.Equal("1234 TSYS", DeviceCommandBuilder.BuildPayload(tsys.Pattern, "1234"));
        Assert.Equal("1234 PSYS", DeviceCommandBuilder.BuildPayload(psys.Pattern, "1234"));
        Assert.Equal("PASS:999 CMD:RESET", DeviceCommandBuilder.BuildPayload(reset.Pattern, "999"));
        Assert.Equal("PASS:999 CMD:START", DeviceCommandBuilder.BuildPayload(start.Pattern, "999"));
    }
}
