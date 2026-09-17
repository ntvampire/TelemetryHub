using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace KsitalTelemetryHub.Core;

/// <summary>
/// Неизменяемый объект-значение (Value Object) для номера телефона с автоматической нормализацией.
/// Гарантирует единообразие поиска в БД (все номера в формате +7XXXXXXXXXX или каноническом E.164).
/// </summary>
public readonly struct PhoneNumber : IEquatable<PhoneNumber>, IComparable<PhoneNumber>
{
    private static readonly Regex NonDigitsRegex = new(@"[^\d+]", RegexOptions.Compiled);
    private readonly string _normalized;

    public static readonly PhoneNumber Empty = new(string.Empty);

    public string Value => _normalized ?? string.Empty;

    public bool IsEmpty => string.IsNullOrWhiteSpace(_normalized);

    public PhoneNumber(string? rawPhone)
    {
        _normalized = Normalize(rawPhone);
    }

    public static string Normalize(string? rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone))
            return string.Empty;

        // Удаляем пробелы, тире, скобки и спецсимволы, кроме первого плюса
        string cleaned = NonDigitsRegex.Replace(rawPhone.Trim(), "");

        // Удаляем плюс в середине, если он есть
        if (cleaned.StartsWith("+"))
        {
            cleaned = "+" + Regex.Replace(cleaned[1..], @"\D", "");
        }
        else
        {
            cleaned = Regex.Replace(cleaned, @"\D", "");
        }

        // Если 11 цифр и начинается с 8 или 7 без плюса:
        if (!cleaned.StartsWith("+"))
        {
            if (cleaned.Length == 11 && (cleaned.StartsWith("8") || cleaned.StartsWith("7")))
            {
                cleaned = "+7" + cleaned[1..];
            }
            else if (cleaned.Length == 10 && cleaned.StartsWith("9"))
            {
                cleaned = "+7" + cleaned;
            }
            else if (!string.IsNullOrEmpty(cleaned))
            {
                cleaned = "+" + cleaned;
            }
        }
        else
        {
            // Начинается с +: если +89..., переводим в +79...
            if (cleaned.StartsWith("+8") && cleaned.Length == 12)
            {
                cleaned = "+7" + cleaned[2..];
            }
        }

        return cleaned;
    }

    public static PhoneNumber From(string? rawPhone) => new(rawPhone);

    public override string ToString() => Value;

    public bool Equals(PhoneNumber other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is PhoneNumber other && Equals(other);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public int CompareTo(PhoneNumber other) => string.Compare(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public static bool operator ==(PhoneNumber left, PhoneNumber right) => left.Equals(right);
    public static bool operator !=(PhoneNumber left, PhoneNumber right) => !left.Equals(right);

    public static implicit operator string(PhoneNumber phone) => phone.Value;
    public static explicit operator PhoneNumber(string? rawPhone) => new(rawPhone);
}
