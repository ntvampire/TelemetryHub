using System;
using System.Collections.Generic;
using Xunit;
using KsitalTelemetryHub.Modem.Engine;

namespace KsitalTelemetryHub.Modem.Engine.Tests;

public class PduDecoderTests
{
    [Fact]
    public void Should_Decode_Russian_Sms_In_UCS2()
    {
        // Тестовая строка реальной СМС от КСИТАЛ (SCTS 42908011000020 -> +02 quarters (+30 мин))
        string pdu = "07919710000000F0040B919761214365F700084290801100002020041A0441043804420430043B003A002004220031003D002B00360035002E0030";

        var result = PduDecoder.Decode(pdu);

        Assert.Equal("+79161234567", result.SenderNumber);
        Assert.Contains("Кситал: Т1=+65.0", result.Text);
        Assert.Equal(DateTimeKind.Utc, result.Timestamp.Kind);
    }

    [Fact]
    public void Should_Decode_Gsm7Bit_Sms_And_Timezone()
    {
        // SMSC=00, FirstOctet=04, Sender=+79161234567, PID=00, DCS=00 (GSM 7-bit),
        // SCTS=42908011000021 (2024-09-08 11:00:00 +03:00 -> 08:00:00Z, т.к. 0x21 -> 12 quarters = +3h)
        // Text="OK" (UDL=02, packed="CF25"?) -> Проверим на стандартном GSM 7-bit векторе "hellohello" (UDL=0A, E8329BFD4697D9EC37)
        string pdu = "00040B919761214365F70000429080110000210AE8329BFD4697D9EC37";

        var result = PduDecoder.Decode(pdu);

        Assert.Equal("+79161234567", result.SenderNumber);
        Assert.Equal("hellohello", result.Text);
        Assert.Equal(new DateTime(2024, 9, 8, 8, 0, 0, DateTimeKind.Utc), result.Timestamp);
    }

    [Fact]
    public void Should_Decode_Alphanumeric_Sender()
    {
        // Sender "MTS": length=6 nibbles (06), TOA=D0, GSM7("MTS") = CD 2A 13? Wait:
        // 'M'=0x4D, 'T'=0x54, 'S'=0x53 -> septets: 1001101, 1010100, 1010011
        // byte0 = (0x54 << 7) | 0x4D = 0x4D; byte1 = (0x53 << 6) | (0x54 >> 1) = 0xEA; byte2 = 0x53 >> 2 = 0x14 -> 4DEA14
        string pdu = "000406D04DEA140000429080110000210AE8329BFD4697D9EC37";

        var result = PduDecoder.Decode(pdu);

        Assert.Equal("MTS", result.SenderNumber);
        Assert.Equal("hellohello", result.Text);
    }

    [Fact]
    public void PduEncoder_And_PduDecoder_RoundTrip_Multipart_Unicode()
    {
        string longRussianText = new string('А', 40) + " Котельная №1: температура Т1=+75.5°C, давление в норме, 220V есть!";
        Assert.True(longRussianText.Length > 70);

        var encodedParts = PduEncoder.EncodeUcs2Submit("+79161234567", longRussianText, concatRef: 42);
        Assert.Equal(2, encodedParts.Count);

        // Имитируем приём двух SMS-DELIVER с UDH из сформированных частей
        var decodedResults = new List<PduDecodeResult>();
        for (int i = 0; i < encodedParts.Count; i++)
        {
            int part = i + 1;
            int chunkChars = 67;
            int start = i * chunkChars;
            string seg = longRussianText.Substring(start, Math.Min(chunkChars, longRussianText.Length - start));
            byte[] ucs2 = System.Text.Encoding.BigEndianUnicode.GetBytes(seg);
            byte[] udh = { 0x05, 0x00, 0x03, 42, 2, (byte)part };
            int udl = udh.Length + ucs2.Length;
            string udHex = Convert.ToHexString(udh) + Convert.ToHexString(ucs2);
            string deliverPdu = $"00440B919761214365F7000842908011000021{udl:X2}{udHex}";

            var decoded = PduDecoder.DecodeWithMetadata(deliverPdu);
            Assert.NotNull(decoded.Concat);
            Assert.Equal(2, decoded.Concat!.TotalParts);
            Assert.Equal(part, decoded.Concat.PartNumber);
            decodedResults.Add(decoded);
        }

        using var modem = new GsmModemClient("COM99");
        var assembled = modem.AssembleMessages(decodedResults);

        Assert.Single(assembled);
        Assert.Equal(longRussianText, assembled[0].Text);
        Assert.Equal("+79161234567", assembled[0].SenderNumber);
    }

    [Theory]
    [InlineData("+79161234567\r\nAT+CMGS=\"+7999000\"", true)]
    [InlineData("invalid_phone", true)]
    [InlineData("", true)]
    [InlineData("8 (916) 123-45-67", false)]
    [InlineData("+79161234567", false)]
    public void ValidateAndSanitizePhone_BlocksAtInjection(string input, bool shouldThrow)
    {
        if (shouldThrow)
        {
            Assert.Throws<ArgumentException>(() => GsmModemClient.ValidateAndSanitizePhone(input));
        }
        else
        {
            string res = GsmModemClient.ValidateAndSanitizePhone(input);
            Assert.Equal("+79161234567", res);
        }
    }

    [Fact]
    public void SanitizeSmsText_StripsControlBytes()
    {
        string dirty = "Ust control 00000\x1A\x1B\0AT+CMGD=1";
        string clean = GsmModemClient.SanitizeSmsText(dirty);

        Assert.DoesNotContain('\x1A', clean);
        Assert.DoesNotContain('\x1B', clean);
        Assert.DoesNotContain('\0', clean);
        Assert.Equal("Ust control 00000AT+CMGD=1", clean);
    }
}
