using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace KsitalTelemetryHub.UI.WinUI.Services;

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
            var soundPath = Path.Combine(AppContext.BaseDirectory, "Assets", "alarm.wav");
            if (File.Exists(soundPath))
            {
                PlaySound(soundPath, IntPtr.Zero, SND_ASYNC | SND_FILENAME | SND_NODEFAULT);
                return;
            }

            lock (_lock)
            {
                if (_cachedWav == null)
                {
                    _cachedWav = GenerateAlarmBeepWav();
                    try
                    {
                        var dir = Path.GetDirectoryName(soundPath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }
                        File.WriteAllBytes(soundPath, _cachedWav);
                    }
                    catch { }
                }
            }

            PlaySound(_cachedWav, IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
        }
        catch (Exception ex)
        {
            App.LogError("SoundService.PlayAlarmSound", ex);
        }
    }

    public static byte[] GenerateAlarmBeepWav()
    {
        // 44.1 kHz, 16-bit mono PCM
        // 3 sharp rising alert pulses with smooth attack/decay:
        // Pulse 1: 880 Hz, 150 ms
        // Silence: 40 ms
        // Pulse 2: 1200 Hz, 180 ms
        // Silence: 40 ms
        // Pulse 3: 1500 Hz, 240 ms
        int sampleRate = 44100;
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        var samples = new List<short>();
        void AddTone(double freq, int msDuration)
        {
            int totalSamples = (sampleRate * msDuration) / 1000;
            int attackDecay = Math.Min(totalSamples / 6, sampleRate * 10 / 1000); // 10ms smooth ramp
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

        // RIFF header
        writer.Write("RIFF"u8);
        writer.Write(36 + byteCount);
        writer.Write("WAVE"u8);

        // fmt subchunk
        writer.Write("fmt "u8);
        writer.Write(16); // subchunk1size (16 for PCM)
        writer.Write((short)1); // audio format 1 = PCM
        writer.Write((short)1); // num channels = 1 (mono)
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2); // byte rate
        writer.Write((short)2); // block align
        writer.Write((short)16); // bits per sample

        // data subchunk
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
