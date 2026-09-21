using Xunit;
using KsitalTelemetryHub.Modem.Engine;

namespace KsitalTelemetryHub.Modem.Engine.Tests;

public class GsmModemParserTests
{
    [Theory]
    [InlineData("+CSQ: 18,0\r\n\r\nOK", 18)]
    [InlineData("+CSQ: 31,99\r\nOK", 31)]
    [InlineData("+CSQ: 5,0", 5)]
    [InlineData("+CSQ: 99,99\r\nOK", 0)] // 99 means not known or not detectable
    [InlineData("+CSQ: 0,0", 0)]
    [InlineData("ERROR", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void Should_Parse_SignalStrength_Correctly(string? rawResponse, int expectedCsq)
    {
        int actual = GsmModemClient.ParseSignalStrength(rawResponse ?? "");
        Assert.Equal(expectedCsq, actual);
    }

    [Theory]
    [InlineData("+COPS: 0,2,\"25001\"\r\n\r\nOK", "MTS")]
    [InlineData("+COPS: 0,2,\"25002\"", "MegaFon")]
    [InlineData("+COPS: 0,2,\"25099\"", "Beeline")]
    [InlineData("+COPS: 0,2,\"25020\"", "Tele2")]
    [InlineData("+COPS: 0,0,\"MTS RUS\"", "MTS RUS")]
    [InlineData("+COPS: 0,0,\"Beeline\"", "Beeline")]
    [InlineData("ERROR", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Should_Parse_OperatorName_Correctly(string? rawResponse, string? expectedOperator)
    {
        string? actual = GsmModemClient.ParseOperatorName(rawResponse ?? "");
        Assert.Equal(expectedOperator, actual);
    }
}
