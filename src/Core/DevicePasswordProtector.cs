using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KsitalTelemetryHub.Core;

/// <summary>
/// Защита паролей контроллеров (GSM-кодов) в БД и маскирование секретов в логах / журнале событий.
/// Сохраняет обратную совместимость с незашифрованными паролями из старых БД и импортов.
/// </summary>
public static class DevicePasswordProtector
{
    public const string Prefix = "ENC:";
    private const string KeyFileName = ".hub.key";
    private static readonly object KeyLock = new();
    private static byte[]? _cachedKey;
    private static string? _cachedKeyDir;

    public static string Protect(string? plainPassword, string? keyDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(plainPassword))
            return string.Empty;

        string trimmed = plainPassword.Trim();
        if (trimmed.StartsWith(Prefix, StringComparison.Ordinal))
            return trimmed;

        byte[] key = GetOrCreateMasterKey(keyDirectory);
        byte[] plaintext = Encoding.UTF8.GetBytes(trimmed);
        byte[] nonce = new byte[12];
        byte[] tag = new byte[16];
        byte[] ciphertext = new byte[plaintext.Length];

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        byte[] combined = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length + tag.Length, ciphertext.Length);

        return Prefix + Convert.ToBase64String(combined);
    }

    public static string Unprotect(string? storedPassword, string? keyDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(storedPassword))
            return string.Empty;

        string trimmed = storedPassword.Trim();
        if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
            return trimmed;

        try
        {
            byte[] combined = Convert.FromBase64String(trimmed.Substring(Prefix.Length));
            if (combined.Length < 28)
                return string.Empty;

            byte[] key = GetOrCreateMasterKey(keyDirectory);
            byte[] nonce = new byte[12];
            byte[] tag = new byte[16];
            byte[] ciphertext = new byte[combined.Length - 28];
            byte[] plaintext = new byte[ciphertext.Length];

            Buffer.BlockCopy(combined, 0, nonce, 0, 12);
            Buffer.BlockCopy(combined, 12, tag, 0, 16);
            Buffer.BlockCopy(combined, 28, ciphertext, 0, ciphertext.Length);

            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Заменяет пароль устройства в тексте SMS-команды на "***" для безопасного логирования.
    /// </summary>
    public static string MaskPasswordInPayload(string? payload, string? plainPassword)
    {
        if (string.IsNullOrEmpty(payload))
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(plainPassword))
        {
            string pass = plainPassword.Trim();
            string pattern = $@"(?<![A-Za-z0-9]){Regex.Escape(pass)}(?![A-Za-z0-9])";
            string replaced = Regex.Replace(payload, pattern, "***");
            if (!string.Equals(replaced, payload, StringComparison.Ordinal))
                return replaced;

            int idx = payload.LastIndexOf(pass, StringComparison.Ordinal);
            if (idx >= 0)
            {
                return string.Concat(payload.AsSpan(0, idx), "***", payload.AsSpan(idx + pass.Length));
            }
        }

        // Универсальная страховка для известных форматов с паролем (PASS:..., /pass ...)
        string masked = Regex.Replace(payload, @"(PASS\s*:\s*)\S+", "$1***", RegexOptions.IgnoreCase);
        masked = Regex.Replace(masked, @"^(/\s*)\S+", "$1***");
        return masked;
    }

    private static byte[] GetOrCreateMasterKey(string? keyDirectory)
    {
        string dir = string.IsNullOrWhiteSpace(keyDirectory)
            ? AppPaths.GetDataDirectory()
            : Path.GetFullPath(keyDirectory);

        lock (KeyLock)
        {
            if (_cachedKey != null && string.Equals(_cachedKeyDir, dir, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedKey;
            }

            Directory.CreateDirectory(dir);
            string keyPath = Path.Combine(dir, KeyFileName);

            byte[] key;
            if (File.Exists(keyPath))
            {
                key = File.ReadAllBytes(keyPath);
                if (key.Length == 32)
                {
                    _cachedKey = key;
                    _cachedKeyDir = dir;
                    return key;
                }
            }

            key = new byte[32];
            RandomNumberGenerator.Fill(key);
            File.WriteAllBytes(keyPath, key);

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    File.SetAttributes(keyPath, File.GetAttributes(keyPath) | FileAttributes.Hidden);
                }
                else
                {
                    File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
            catch { }

            _cachedKey = key;
            _cachedKeyDir = dir;
            return key;
        }
    }
}
