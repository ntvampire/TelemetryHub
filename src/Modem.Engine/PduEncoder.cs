using System;
using System.Collections.Generic;
using System.Text;

namespace KsitalTelemetryHub.Modem.Engine;

public record EncodedSubmitPdu(int TpduLength, string Hex);

/// <summary>
/// Кодировщик исходящих SMS в формат SMS-SUBMIT PDU (UCS2, включая длинные многосегментные сообщения).
/// Позволяет отправлять кириллицу и произвольный текст без переключения модема из PDU-режима.
/// </summary>
public static class PduEncoder
{
    private static int _concatRefCounter;

    public static bool RequiresUnicode(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (char c in text)
        {
            if (c > 0x7F) return true;
        }
        return false;
    }

    public static IReadOnlyList<EncodedSubmitPdu> EncodeUcs2Submit(string phoneNumber, string text, int? concatRef = null)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new ArgumentException("Номер телефона не может быть пустым.", nameof(phoneNumber));

        text ??= string.Empty;

        var result = new List<EncodedSubmitPdu>();

        // 140 байт на сегмент: при 1 части помещается 70 UCS2-символов (140 байт),
        // при >1 части UDH занимает 6 байт (05 00 03 RR TT PP), остаётся 134 байта = 67 UCS2-символов.
        if (text.Length <= 70)
        {
            byte[] ucs2 = Encoding.BigEndianUnicode.GetBytes(text);
            result.Add(BuildSingleUcs2Pdu(phoneNumber, ucs2, udh: null));
            return result;
        }

        const int chunkChars = 67;
        int totalParts = (text.Length + chunkChars - 1) / chunkChars;
        if (totalParts > 255)
            throw new ArgumentException("Слишком длинное сообщение для отправки через SMS.", nameof(text));

        byte refByte = (byte)((concatRef ?? System.Threading.Interlocked.Increment(ref _concatRefCounter)) & 0xFF);

        for (int part = 1; part <= totalParts; part++)
        {
            int start = (part - 1) * chunkChars;
            int len = Math.Min(chunkChars, text.Length - start);
            string segment = text.Substring(start, len);
            byte[] ucs2 = Encoding.BigEndianUnicode.GetBytes(segment);
            byte[] udh = { 0x05, 0x00, 0x03, refByte, (byte)totalParts, (byte)part };
            result.Add(BuildSingleUcs2Pdu(phoneNumber, ucs2, udh));
        }

        return result;
    }

    private static EncodedSubmitPdu BuildSingleUcs2Pdu(string phoneNumber, byte[] ucs2Payload, byte[]? udh)
    {
        string trimmed = phoneNumber.Trim();
        bool isInternational = trimmed.StartsWith('+');
        string digits = isInternational ? trimmed[1..] : trimmed;

        if (digits.Length == 0 || digits.Length > 20)
            throw new ArgumentException("Некорректная длина номера телефона для PDU.", nameof(phoneNumber));

        foreach (char c in digits)
        {
            if (c < '0' || c > '9')
                throw new ArgumentException("Номер телефона для PDU должен содержать только цифры.", nameof(phoneNumber));
        }

        var sb = new StringBuilder();

        // SMSC length = 0 (использовать SMSC по умолчанию с SIM-карты)
        sb.Append("00");

        int firstOctet = udh != null ? 0x51 : 0x11; // SMS-SUBMIT + Relative VP (+ UDHI при multipart)
        sb.Append(firstOctet.ToString("X2"));
        sb.Append("00"); // TP-MR

        // TP-DA
        sb.Append(digits.Length.ToString("X2"));
        sb.Append(isInternational ? "91" : "81");

        for (int i = 0; i < digits.Length; i += 2)
        {
            char low = digits[i];
            char high = (i + 1 < digits.Length) ? digits[i + 1] : 'F';
            sb.Append(high);
            sb.Append(low);
        }

        sb.Append("00"); // TP-PID
        sb.Append("08"); // TP-DCS = UCS2
        sb.Append("AA"); // TP-VP = 4 дня

        int udl = (udh?.Length ?? 0) + ucs2Payload.Length;
        sb.Append(udl.ToString("X2"));

        if (udh != null)
        {
            foreach (byte b in udh) sb.Append(b.ToString("X2"));
        }

        foreach (byte b in ucs2Payload) sb.Append(b.ToString("X2"));

        string hex = sb.ToString();
        int tpduLength = (hex.Length / 2) - 1; // Без первого октета SMSC ("00")
        return new EncodedSubmitPdu(tpduLength, hex);
    }
}
