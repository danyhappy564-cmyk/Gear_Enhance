using System;
using System.IO;
using System.Text;

namespace GearEnhance;

internal sealed class WaveSound
{
    internal int Channels { get; private set; }
    internal int SampleRate { get; private set; }
    internal float[] Samples { get; private set; }
    internal int Frames => Samples.Length / Channels;
    internal double Duration => Frames / (double)SampleRate;

    internal float[] SamplesFrom(int offset, float volume)
    {
        if (offset < 0 || offset >= Frames || float.IsNaN(volume) || float.IsInfinity(volume) || volume < 0 || volume > 1)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (offset == 0 && volume == 1) return Samples;
        var samples = new float[(Frames - offset) * Channels];
        for (var index = 0; index < samples.Length; index++)
            samples[index] = Samples[offset * Channels + index] * volume;
        return samples;
    }

    // A bounded PCM16 reader, independent of Unity and the platform MP3 codec.
    internal static WaveSound Read(Stream stream)
    {
        if (stream == null || !stream.CanSeek || stream.Length < 44 || stream.Length > 16 * 1024 * 1024)
            throw new InvalidDataException("강화 음원의 파일 크기 또는 형식이 올바르지 않습니다.");
        using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
        {
            if (reader.ReadUInt32() != 0x46464952) throw new InvalidDataException("RIFF 음원이 아닙니다.");
            var end = reader.ReadUInt32() + 8L;
            if (end != stream.Length || reader.ReadUInt32() != 0x45564157)
                throw new InvalidDataException("WAVE 음원이 잘렸거나 형식이 다릅니다.");
            int channels = 0, rate = 0;
            byte[] pcm = null;
            while (stream.Position + 8 <= end)
            {
                var kind = reader.ReadUInt32();
                var length = reader.ReadUInt32();
                var next = stream.Position + length + (length & 1);
                if (next > end) throw new InvalidDataException("음원 블록이 잘렸습니다.");
                if (kind == 0x20746d66)
                {
                    if (length < 16 || reader.ReadUInt16() != 1) throw new InvalidDataException("PCM 음원이 아닙니다.");
                    channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                    var byteRate = reader.ReadInt32();
                    var block = reader.ReadUInt16();
                    var bits = reader.ReadUInt16();
                    if ((channels != 1 && channels != 2) || rate < 8000 || rate > 96000
                        || bits != 16 || block != channels * 2 || byteRate != rate * block)
                        throw new InvalidDataException("PCM16 모노/스테레오 음원이 아닙니다.");
                }
                else if (kind == 0x61746164)
                {
                    if (pcm != null || length == 0) throw new InvalidDataException("음원 데이터가 잘못됐습니다.");
                    pcm = reader.ReadBytes((int)length);
                    if (pcm.Length != length) throw new InvalidDataException("음원 데이터가 잘렸습니다.");
                }
                stream.Position = next;
            }
            if (stream.Position != end || channels == 0 || pcm == null || pcm.Length % (channels * 2) != 0)
                throw new InvalidDataException("음원 길이 또는 채널 정보가 다릅니다.");
            if (pcm.Length / (channels * 2L) > rate * 60L)
                throw new InvalidDataException("강화 음원은 60초 이하여야 합니다.");
            var samples = new float[pcm.Length / 2];
            for (var index = 0; index < samples.Length; index++)
                samples[index] = (short)(pcm[index * 2] | pcm[index * 2 + 1] << 8) / 32768f;
            return new WaveSound { Channels = channels, SampleRate = rate, Samples = samples };
        }
    }
}

internal static class EmbeddedSounds
{
    internal const int Count = 10;
    internal static int Index(FeedbackCue cue) => cue switch
    {
        FeedbackCue.Attempt => 0,
        FeedbackCue.Success => 1,
        FeedbackCue.Complete => 4,
        FeedbackCue.EvolutionAttempt => 5,
        FeedbackCue.Failure => 2,
        FeedbackCue.Downgrade => 2,
        FeedbackCue.Destroy => 3,
        FeedbackCue.MysteryConfirm => 6,
        FeedbackCue.MysteryAttempt => 7,
        FeedbackCue.MythicAchieved => 8,
        FeedbackCue.MythicUp => 9,
        _ => throw new ArgumentOutOfRangeException(nameof(cue))
    };
    // 1.4.0: the normal charge is the user's charge with its silent first 0.765 s cut
    // (design 7.1); evolution keeps its 4.412 s blend. 6~9 are the synthesized ??? / mythic sounds.
    internal static WaveSound Load(int index)
    {
        var name = index switch
        {
            0 => "charge_trimmed", 1 => "success", 2 => "failure", 3 => "destroy",
            4 => "star2", 5 => "evolution_charge",
            6 => "myth_confirm", 7 => "myth_charge", 8 => "myth_success", 9 => "myth_up",
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
        using (var stream = typeof(EmbeddedSounds).Assembly.GetManifestResourceStream("GearEnhance.Audio." + name + ".wav"))
            return WaveSound.Read(stream);
    }
}
