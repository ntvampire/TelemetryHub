using System;
using System.Collections.Generic;
using System.Text;
using KsitalTelemetryHub.Core;

namespace KsitalTelemetryHub.Modem.Engine;

public record PduConcatInfo(int ReferenceNumber, int TotalParts, int PartNumber);

public record PduDecodeResult(DecodedSms Sms, PduConcatInfo? Concat);

public static class PduDecoder
{
    private const string Gsm7DefaultAlphabet =
        "@£$¥èéùìòÇ\nØø\rÅå" +
        "Δ_ΦΓΛΩΠΨΣΘΞ\x1BÆæßÉ" +
        " !\"#¤%&'()*+,-./" +
        "0123456789:;<=>?" +
        "¡ABCDEFGHIJKLMNO" +
        "PQRSTUVWXYZÄÖÑÜ§" +
        "¿abcdefghijklmno" +
        "pqrstuvwxyzäöñüà";

    private static readonly Dictionary<byte, char> Gsm7ExtensionAlphabet = new()
    {
        [0x0A] = '\f',
        [0x14] = '^',
        [0x28] = '{',
        [0x29] = '}',
        [0x2F] = '\\',
        [0x3C] = '[',
        [0x3D] = '~',
        [0x3E] = ']',
        [0x40] = '|',
        [0x65] = '€'
    };

    public static DecodedSms Decode(string pduHex)
    {
        return DecodeWithMetadata(pduHex).Sms;
    }

    public static PduDecodeResult DecodeWithMetadata(string pduHex)
    {
        if (string.IsNullOrWhiteSpace(pduHex))
            throw new ArgumentException("PDU-строка не может быть пустой.", nameof(pduHex));

        pduHex = pduHex.Trim();
        if (pduHex.Length % 2 != 0)
            throw new FormatException("Длина PDU-строки должна быть чётной.");

        int offset = 0;

        int smscLen = ReadByte(pduHex, ref offset);
        if (smscLen > 0)
        {
            EnsureAvailable(pduHex, offset, smscLen * 2, "SMSC");
            offset += smscLen * 2;
        }

        int firstOctet = ReadByte(pduHex, ref offset);
        bool hasUserDataHeader = (firstOctet & 0x40) != 0;

        int senderLen = ReadByte(pduHex, ref offset);
        int senderType = ReadByte(pduHex, ref offset);

        int senderOctets = (senderLen + 1) / 2;
        string senderHex = ReadSlice(pduHex, ref offset, senderOctets * 2, "адреса отправителя");
        string senderNumber = DecodeSenderAddress(senderHex, senderLen, senderType);

        _ = ReadByte(pduHex, ref offset); // TP-PID
        int dcs = ReadByte(pduHex, ref offset);

        string sctsHex = ReadSlice(pduHex, ref offset, 14, "метки времени SCTS");
        DateTime timestamp = DecodeTimestamp(sctsHex);

        int udl = ReadByte(pduHex, ref offset);
        string udHex = pduHex.Substring(offset);

        var (text, concat) = DecodeUserData(udHex, udl, dcs, hasUserDataHeader);

        var sms = new DecodedSms
        {
            SenderNumber = senderNumber,
            Timestamp = timestamp,
            Text = text,
            RawPdu = pduHex
        };

        return new PduDecodeResult(sms, concat);
    }

    private static int ReadByte(string hex, ref int offset)
    {
        string slice = ReadSlice(hex, ref offset, 2, "заголовка PDU");
        return Convert.ToInt32(slice, 16);
    }

    private static string ReadSlice(string hex, ref int offset, int charCount, string fieldName)
    {
        EnsureAvailable(hex, offset, charCount, fieldName);
        string slice = hex.Substring(offset, charCount);
        offset += charCount;
        return slice;
    }

    private static void EnsureAvailable(string hex, int offset, int neededChars, string fieldName)
    {
        if (neededChars < 0 || offset + neededChars > hex.Length)
            throw new FormatException($"Повреждённый PDU: недостаточно данных для чтения {fieldName}.");
    }

    private static string DecodeSenderAddress(string hex, int length, int typeOfAddress)
    {
        if (length == 0 || string.IsNullOrEmpty(hex))
            return string.Empty;

        // Буквенно-цифровой отправитель (TON = 0b101 -> 0x50, например 0xD0)
        if ((typeOfAddress & 0x70) == 0x50)
        {
            byte[] bytes = HexToBytes(hex);
            int charCount = (length * 4) / 7;
            return UnpackGsm7Septets(bytes, 0, charCount);
        }

        var sb = new StringBuilder();
        if ((typeOfAddress & 0x70) == 0x10) // International
            sb.Append('+');

        for (int i = 0; i + 1 < hex.Length; i += 2)
        {
            char low = hex[i + 1];
            char high = hex[i];

            if (low != 'F' && low != 'f')
                sb.Append(low);
            if (high != 'F' && high != 'f')
                sb.Append(high);
        }

        string result = sb.ToString();
        if (result.StartsWith("+") && result.Length > length + 1)
            result = result.Substring(0, length + 1);
        else if (!result.StartsWith("+") && result.Length > length)
            result = result.Substring(0, length);

        return result;
    }

    private static DateTime DecodeTimestamp(string hex)
    {
        try
        {
            int year = ParseBcdByte(hex[0], hex[1]);
            int month = ParseBcdByte(hex[2], hex[3]);
            int day = ParseBcdByte(hex[4], hex[5]);
            int hour = ParseBcdByte(hex[6], hex[7]);
            int minute = ParseBcdByte(hex[8], hex[9]);
            int second = ParseBcdByte(hex[10], hex[11]);

            int tzHighNibble = HexVal(hex[12]); // units
            int tzLowNibble = HexVal(hex[13]);  // sign bit (0x08) + tens (0x07)
            bool isNegativeTz = (tzLowNibble & 0x08) != 0;
            int tzQuarters = ((tzLowNibble & 0x07) * 10) + tzHighNibble;
            if (isNegativeTz) tzQuarters = -tzQuarters;

            var tzOffset = Math.Abs(tzQuarters) <= 56
                ? TimeSpan.FromMinutes(tzQuarters * 15)
                : TimeSpan.Zero;

            int fullYear = 2000 + year;
            var dto = new DateTimeOffset(fullYear, month, day, hour, minute, second, tzOffset);
            return dto.UtcDateTime;
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }

    private static int ParseBcdByte(char unitsChar, char tensChar)
    {
        return HexVal(tensChar) * 10 + HexVal(unitsChar);
    }

    private static int HexVal(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'A' && c <= 'F') return 10 + (c - 'A');
        if (c >= 'a' && c <= 'f') return 10 + (c - 'a');
        throw new FormatException($"Некорректный hex-символ: {c}");
    }

    private static (string Text, PduConcatInfo? Concat) DecodeUserData(string udHex, int udl, int dcs, bool hasUdhi)
    {
        byte[] udBytes = HexToBytes(udHex);
        int udhByteCount = 0;
        PduConcatInfo? concat = null;

        if (hasUdhi && udBytes.Length >= 1)
        {
            int udhl = udBytes[0];
            udhByteCount = Math.Min(udBytes.Length, 1 + udhl);
            concat = ParseConcatInfo(udBytes, udhByteCount);
        }

        // Определяем кодировку по 3GPP TS 23.038
        bool isGsm7 = false;
        bool is8Bit = false;
        bool isUcs2 = false;

        if ((dcs & 0xF0) == 0xF0)
        {
            // Data coding / message class: bit 2 = 0 -> 7-bit, 1 -> 8-bit
            if ((dcs & 0x04) == 0) isGsm7 = true;
            else is8Bit = true;
        }
        else if ((dcs & 0xC0) == 0x00 || (dcs & 0xF0) == 0xD0 || (dcs & 0xF0) == 0xC0)
        {
            int alphabet = (dcs >> 2) & 0x03;
            switch (alphabet)
            {
                case 0: isGsm7 = true; break;
                case 1: is8Bit = true; break;
                case 2: isUcs2 = true; break;
            }
        }
        else if ((dcs & 0x0C) == 0x08)
        {
            isUcs2 = true;
        }
        else if ((dcs & 0x0C) == 0x04)
        {
            is8Bit = true;
        }
        else
        {
            isGsm7 = true;
        }

        if (isUcs2)
        {
            int payloadBytes = udBytes.Length - udhByteCount;
            if (udl > 0 && udl - udhByteCount < payloadBytes && udl >= udhByteCount)
            {
                payloadBytes = udl - udhByteCount;
            }
            payloadBytes = Math.Max(0, payloadBytes - (payloadBytes % 2));
            return (Encoding.BigEndianUnicode.GetString(udBytes, udhByteCount, payloadBytes), concat);
        }

        if (is8Bit)
        {
            int payloadBytes = udBytes.Length - udhByteCount;
            if (udl > 0 && udl - udhByteCount < payloadBytes && udl >= udhByteCount)
            {
                payloadBytes = udl - udhByteCount;
            }
            payloadBytes = Math.Max(0, payloadBytes);
            return (Encoding.Latin1.GetString(udBytes, udhByteCount, payloadBytes), concat);
        }

        if (isGsm7)
        {
            int headerSeptets = udhByteCount > 0 ? ((udhByteCount * 8) + 6) / 7 : 0;
            int textSeptets = Math.Max(0, udl - headerSeptets);
            int maxPossibleSeptets = Math.Max(0, (udBytes.Length * 8) / 7 - headerSeptets);
            int count = Math.Min(textSeptets, maxPossibleSeptets);
            return (UnpackGsm7Septets(udBytes, headerSeptets, count), concat);
        }

        return ("[Неизвестная кодировка]", concat);
    }

    private static PduConcatInfo? ParseConcatInfo(byte[] udBytes, int udhByteCount)
    {
        int pos = 1;
        while (pos + 1 < udhByteCount)
        {
            byte iei = udBytes[pos];
            byte iel = udBytes[pos + 1];
            int dataStart = pos + 2;
            if (dataStart + iel > udhByteCount) break;

            if (iei == 0x00 && iel == 3)
            {
                int refNum = udBytes[dataStart];
                int total = udBytes[dataStart + 1];
                int part = udBytes[dataStart + 2];
                if (total > 1 && part >= 1 && part <= total)
                    return new PduConcatInfo(refNum, total, part);
            }
            else if (iei == 0x08 && iel == 4)
            {
                int refNum = (udBytes[dataStart] << 8) | udBytes[dataStart + 1];
                int total = udBytes[dataStart + 2];
                int part = udBytes[dataStart + 3];
                if (total > 1 && part >= 1 && part <= total)
                    return new PduConcatInfo(refNum, total, part);
            }

            pos = dataStart + iel;
        }

        return null;
    }

    private static string UnpackGsm7Septets(byte[] bytes, int skipSeptets, int septetCount)
    {
        if (septetCount <= 0 || bytes.Length == 0)
            return string.Empty;

        var sb = new StringBuilder(septetCount);
        bool escapeNext = false;

        for (int i = 0; i < septetCount; i++)
        {
            int idx = skipSeptets + i;
            int bitOffset = idx * 7;
            int byteIdx = bitOffset / 8;
            if (byteIdx >= bytes.Length) break;

            int bitInByte = bitOffset % 8;
            int septet = (bytes[byteIdx] >> bitInByte) & 0x7F;
            if (bitInByte > 1 && byteIdx + 1 < bytes.Length)
            {
                septet |= (bytes[byteIdx + 1] << (8 - bitInByte)) & 0x7F;
            }

            if (escapeNext)
            {
                escapeNext = false;
                if (Gsm7ExtensionAlphabet.TryGetValue((byte)septet, out char extChar))
                {
                    sb.Append(extChar);
                }
                else if (septet < Gsm7DefaultAlphabet.Length)
                {
                    sb.Append(Gsm7DefaultAlphabet[septet]);
                }
                continue;
            }

            if (septet == 0x1B)
            {
                escapeNext = true;
                continue;
            }

            if (septet < Gsm7DefaultAlphabet.Length)
            {
                sb.Append(Gsm7DefaultAlphabet[septet]);
            }
        }

        return sb.ToString();
    }

    private static byte[] HexToBytes(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Array.Empty<byte>();
        int len = hex.Length / 2;
        byte[] bytes = new byte[len];
        for (int i = 0; i < len; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }
}
