using System.IO;
using System.Media;

namespace KeyDone;

public enum SoundEvent { Opened, Locked, Copied, Cleared, Saved, Deleted, Error, Warning }

/// <summary>
/// Kısa bildirim sesleri. Sesler program içinde üretilir; kullanıcı isterse
/// %APPDATA%\KeyDone\Sesler klasörüne aynı adla bir WAV dosyası koyarak değiştirebilir.
/// </summary>
public static class Sounds
{
    public sealed record Info(SoundEvent Event, string FileName, string Title);

    public static readonly IReadOnlyList<Info> All =
    [
        new(SoundEvent.Opened, "acildi", "Kasa açıldı"),
        new(SoundEvent.Locked, "kilitlendi", "Kasa kilitlendi"),
        new(SoundEvent.Copied, "kopyalandi", "Panoya kopyalandı"),
        new(SoundEvent.Cleared, "pano-temizlendi", "Pano temizlendi"),
        new(SoundEvent.Saved, "kaydedildi", "Kayıt kaydedildi"),
        new(SoundEvent.Deleted, "silindi", "Kayıt silindi"),
        new(SoundEvent.Error, "hata", "Hata"),
        new(SoundEvent.Warning, "uyari", "Uyarı (kasa kilitlenmek üzere)"),
    ];

    public static bool Enabled { get; set; } = true;

    /// <summary>0 ile 100 arası.</summary>
    public static int Volume { get; set; } = 60;

    public static string Folder => Path.Combine(AppPaths.DataFolder, "Sesler");

    public static string? CustomFile(SoundEvent e)
    {
        string path = Path.Combine(Folder, All.First(i => i.Event == e).FileName + ".wav");
        return File.Exists(path) ? path : null;
    }

    // Çalan sesin belleği çalma bitene kadar yaşamalı.
    static SoundPlayer? _player;

    public static void Play(SoundEvent e, bool evenIfDisabled = false)
    {
        if (!Enabled && !evenIfDisabled) return;
        try
        {
            byte[] data = CustomFile(e) is { } file ? File.ReadAllBytes(file) : Synthesize(e, Volume / 100.0);
            _player?.Stop();
            _player = new SoundPlayer(new MemoryStream(data));
            _player.Play();
        }
        catch (Exception)
        {
            // Ses çalınamazsa uygulama sessizce devam eder.
        }
    }

    // ---------------- Ses üretimi ----------------

    const int Rate = 44100;

    /// <summary>Nota: frekans (Hz), başlangıç ve süre (ms).</summary>
    readonly record struct Note(double Hz, int StartMs, int LengthMs);

    static byte[] Synthesize(SoundEvent e, double volume)
    {
        const double C5 = 523.25, E5 = 659.25, G5 = 783.99, A5 = 880, B5 = 987.77, D6 = 1174.66;
        Note[] notes = e switch
        {
            SoundEvent.Opened => [new(C5, 0, 110), new(E5, 70, 110), new(G5, 140, 200)],
            SoundEvent.Locked => [new(G5, 0, 110), new(E5, 70, 110), new(C5, 140, 220)],
            SoundEvent.Copied => [new(A5, 0, 70), new(D6, 55, 110)],
            SoundEvent.Cleared => [new(D6, 0, 70), new(A5, 55, 130)],
            SoundEvent.Saved => [new(E5, 0, 260), new(B5, 0, 260)],
            SoundEvent.Deleted => [new(392, 0, 100), new(261.63, 80, 200)],
            SoundEvent.Error => [new(196, 0, 130), new(196, 170, 160)],
            SoundEvent.Warning => [new(B5, 0, 80), new(B5, 140, 80), new(B5, 280, 80)],
            _ => [],
        };
        bool buzz = e == SoundEvent.Error;
        double gain = volume * (e == SoundEvent.Cleared ? 0.55 : 0.8);

        int total = (notes.Max(n => n.StartMs + n.LengthMs) + 30) * Rate / 1000;
        var mix = new double[total];
        foreach (var n in notes)
        {
            int start = n.StartMs * Rate / 1000, length = n.LengthMs * Rate / 1000, attack = Rate / 200;
            for (int i = 0; i < length && start + i < total; i++)
            {
                double t = (double)i / Rate;
                double envelope = (i < attack ? (double)i / attack : 1) * Math.Exp(-4.0 * i / length);
                double phase = 2 * Math.PI * n.Hz * t;
                double wave = buzz
                    ? 0.5 * Math.Sign(Math.Sin(phase)) + 0.3 * Math.Sin(phase)
                    : Math.Sin(phase) + 0.25 * Math.Sin(2 * phase);
                mix[start + i] += wave * envelope;
            }
        }
        double peak = Math.Max(1, mix.Max(Math.Abs));

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = total * 2;
        w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dataBytes);
        foreach (double s in mix) w.Write((short)(s / peak * gain * short.MaxValue * 0.9));
        w.Flush();
        return ms.ToArray();
    }
}
