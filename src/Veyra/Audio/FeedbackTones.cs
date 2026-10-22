using System.IO;
using System.Runtime.InteropServices;
using Veyra.Core;

namespace Veyra.Audio;

/// <summary>
/// Kurze Signaltöne beim Stummschalten bzw. Einschalten des Mikrofons – zwei weiche Sinustöne, absteigend bzw.
/// aufsteigend (wie in Sprach-Apps). Einmal im Speicher erzeugt, keine Dateien nötig; Wiedergabe asynchron über
/// PlaySound. Der Puffer liegt in nicht verschiebbarem Speicher und bleibt bis zum Programmende bestehen.
/// </summary>
internal static class FeedbackTones
{
    private const int SampleRate = 44100;
    private const uint SND_ASYNC = 0x1, SND_NODEFAULT = 0x2, SND_MEMORY = 0x4;

    private static IntPtr _muted;
    private static IntPtr _unmuted;

    public static void PlayMuted() => Play(ref _muted, 660, 440);

    public static void PlayUnmuted() => Play(ref _unmuted, 440, 660);

    private static void Play(ref IntPtr wave, double first, double second)
    {
        try
        {
            if (wave == IntPtr.Zero)
            {
                byte[] bytes = CreateWave(first, second);
                wave = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, wave, bytes.Length);
            }
            PlaySoundW(wave, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
        }
        catch (Exception ex)
        {
            Log.Warning("Audio", $"Signalton konnte nicht abgespielt werden: {ex.Message}");
        }
    }

    /// <summary>Zwei Töne à 70 ms mit 15 ms Pause, mit kurzem Ein-/Ausblenden gegen Knacken; 16 Bit mono PCM.</summary>
    private static byte[] CreateWave(double first, double second)
    {
        const double toneSeconds = 0.07, gapSeconds = 0.015, fadeSeconds = 0.008, volume = 0.22;
        int toneSamples = (int)(SampleRate * toneSeconds), gapSamples = (int)(SampleRate * gapSeconds);
        int fadeSamples = (int)(SampleRate * fadeSeconds);
        var samples = new short[toneSamples * 2 + gapSamples];

        void Tone(int offset, double frequency)
        {
            for (int i = 0; i < toneSamples; i++)
            {
                double envelope = Math.Min(1, Math.Min(i, toneSamples - 1 - i) / (double)fadeSamples);
                samples[offset + i] = (short)(Math.Sin(2 * Math.PI * frequency * i / SampleRate) * envelope * volume * short.MaxValue);
            }
        }

        Tone(0, first);
        Tone(toneSamples + gapSamples, second);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            int dataBytes = samples.Length * 2;
            writer.Write("RIFF"u8);
            writer.Write(36 + dataBytes);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);              // Größe des fmt-Blocks
            writer.Write((short)1);        // PCM
            writer.Write((short)1);        // mono
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);  // Byte je Sekunde
            writer.Write((short)2);        // Byte je Abtastwert
            writer.Write((short)16);       // Bit je Abtastwert
            writer.Write("data"u8);
            writer.Write(dataBytes);
            foreach (short sample in samples) writer.Write(sample);
        }
        return stream.ToArray();
    }

    [DllImport("winmm.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySoundW(IntPtr sound, IntPtr module, uint flags);
}
