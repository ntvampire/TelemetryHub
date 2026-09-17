using Xunit;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Parser.Ksital;

namespace KsitalTelemetryHub.Parser.Tests;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("89161234567", "+79161234567")]
    [InlineData("+7 (916) 123-45-67", "+79161234567")]
    [InlineData("79161234567", "+79161234567")]
    [InlineData("9161234567", "+79161234567")]
    [InlineData("+79161234567", "+79161234567")]
    [InlineData("+89161234567", "+79161234567")]
    public void Should_Normalize_Russian_Mobile_Numbers(string input, string expected)
    {
        var phone = new PhoneNumber(input);
        Assert.Equal(expected, phone.Value);
    }

    [Fact]
    public void Value_Equality_Should_Work()
    {
        var p1 = new PhoneNumber("8 (916) 000-11-22");
        var p2 = new PhoneNumber("+79160001122");

        Assert.Equal(p1, p2);
        Assert.True(p1 == p2);
    }
}
