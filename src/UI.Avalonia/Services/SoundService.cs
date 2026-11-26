using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using KsitalTelemetryHub.Core;

namespace KsitalTelemetryHub.UI.Avalonia.Services;

public static class SoundService
{
    [DllImport("winmm.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(byte[]? pszSound, IntPtr hmod, uint fdwSound);

    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_MEMORY = 0x0004;
    private const uint SND_FILENAME = 0x00020000;

    private static byte[]? _cachedWav;
    private static readonly object _lock = new();

    public static void PlayAlarmSound()
    {
        try
        {
            string? soundFile = EnsureAlarmWavFile();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!string.IsNullOrEmpty(soundFile) && File.Exists(soundFile))
                {
                    PlaySound(soundFile, IntPtr.Zero, SND_ASYNC | SND_FILENAME | SND_NODEFAULT);
                    return;
                }

                byte[] wav = GetOrGenerateWavBytes();
                PlaySound(wav, IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
            }
            else
            {
                if (!string.IsNullOrEmpty(soundFile) && File.Exists(soundFile) && TryPlayLinuxAudioFile(soundFile))
                {
                    return;
                }
                Console.Write("\a");
            }
        }
        catch (Exception ex)
        {
            App.LogError("SoundService.PlayAlarmSound", ex);
        }
    }

    private static byte[] GetOrGenerateWavBytes()
    {
        lock (_lock)
        {
            return _cachedWav ??= GenerateAlarmBeepWav();
        }
    }

    private static string? EnsureAlarmWavFile()
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "alarm.wav");
        if (File.Exists(bundled))
            return bundled;

        string writablePath = Path.Combine(AppPaths.GetDataDirectory(), "Assets", "alarm.wav");
        if (File.Exists(writablePath))
            return writablePath;

        try
        {
            byte[] wav = GetOrGenerateWavBytes();
            string? dir = Path.GetDirectoryName(writablePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(writablePath, wav);
            return writablePath;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryPlayLinuxAudioFile(string wavPath)
    {
        string[] players = { "paplay", "pw-play", "aplay" };
        foreach (string player in players)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = player,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                psi.ArgumentList.Add(wavPath);
                var proc = Process.Start(psi);
                if (proc != null)
                {
                    return true;
                }
            }
            catch
            {
                // Пробуем следующий аудиоплеер Linux
            }
        }
        return false;
    }

    public static byte[] GenerateAlarmBeepWav()
    {
        int sampleRate = 44100;
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        var samples = new List<short>();
        void AddTone(double freq, int msDuration)
        {
            int totalSamples = (sampleRate * msDuration) / 1000;
            int attackDecay = Math.Min(totalSamples / 6, sampleRate * 10 / 1000);
            for (int i = 0; i < totalSamples; i++)
            {
                double factor = 1.0;
                if (i < attackDecay) factor = (double)i / attackDecay;
                else if (i > totalSamples - attackDecay) factor = (double)(totalSamples - i) / attackDecay;

                double angle = 2.0 * Math.PI * freq * i / sampleRate;
                short sample = (short)(Math.Sin(angle) * 28000 * factor);
                samples.Add(sample);
            }
        }

        void AddSilence(int msDuration)
        {
            int totalSamples = (sampleRate * msDuration) / 1000;
            for (int i = 0; i < totalSamples; i++) samples.Add(0);
        }

        AddTone(880, 150);
        AddSilence(40);
        AddTone(1200, 180);
        AddSilence(40);
        AddTone(1500, 240);

        int byteCount = samples.Count * 2;

        writer.Write("RIFF"u8);
        writer.Write(36 + byteCount);
        writer.Write("WAVE"u8);

        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);

        writer.Write("data"u8);
        writer.Write(byteCount);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }

        writer.Flush();
        return ms.ToArray();
    }
}
