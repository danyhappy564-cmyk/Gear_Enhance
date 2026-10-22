using System;
using UnityEngine;
using UnityEngine.Audio;

namespace GearEnhance;

internal sealed class EnhancementAudio : IDisposable
{
    private readonly AudioClip[] clips = new AudioClip[6];
    private AudioSource source;
    internal EnhancementAudio(GameObject host, WaveSound charge)
    {
        try
        {
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = false; source.spatialBlend = 0;
            source.priority = 128;
            for (var index = 0; index < clips.Length; index++)
            {
                var data = index == 0 ? charge : EmbeddedSounds.Load(index);
                var clip = AudioClip.Create("GearEnhance." + index, data.Frames, data.Channels, data.SampleRate, false);
                if (clip == null) throw new InvalidOperationException("강화 음원을 만들지 못했습니다.");
                clips[index] = clip;
                clip.hideFlags = HideFlags.HideAndDontSave;
                if (!clip.SetData(data.Samples, 0)) throw new InvalidOperationException("강화 음원을 읽지 못했습니다.");
            }
        }
        catch { Dispose(); throw; }
    }
    private void Prepare(float volume)
    {
        if (source == null) throw new ObjectDisposedException(nameof(EnhancementAudio));
        // Bind to the game's actual UI mixer so interface/master mute and volume
        // still apply. Never play through an unassigned mixer as a fallback.
        var group = GameFeedback.MixerGroup() as AudioMixerGroup;
        if (group == null) throw new InvalidOperationException("게임 UI 믹서에 연결할 수 없습니다.");
        source.outputAudioMixerGroup = group;
        source.Stop();
        source.volume = Mathf.Clamp01(volume);
        source.clip = null;
    }
    internal double StartAttempt(float volume, bool evolution)
    {
        Prepare(volume);
        source.clip = clips[evolution ? 5 : 0];
        // Use the audio engine's clock for the bar too; frame rate and the
        // operating system's MP3 decoder cannot drift the gauge from this clip.
        var start = AudioSettings.dspTime + 0.05;
        source.PlayScheduled(start);
        return start;
    }
    internal void Play(FeedbackCue cue, float volume)
    {
        Prepare(volume);
        if (source.volume > 0) source.PlayOneShot(clips[EmbeddedSounds.Index(cue)]);
    }
    internal void Stop() { if (source != null) source.Stop(); }
    public void Dispose()
    {
        Stop();
        if (source != null) UnityEngine.Object.Destroy(source);
        source = null;
        for (var index = 0; index < clips.Length; index++)
        {
            if (clips[index] != null) UnityEngine.Object.Destroy(clips[index]);
            clips[index] = null;
        }
    }
}
