using System;
using UnityEngine;
using UnityEngine.Audio;

namespace GearEnhance;

internal sealed class EnhancementAudio : IDisposable
{
    private readonly AudioClip[] clips = new AudioClip[6];
    private readonly Action<string> log;
    private GameObject audioHost;
    private AudioSource source;
    private float probeAt;
    private FeedbackCue probeCue;
    internal EnhancementAudio(GameObject host, WaveSound charge, Action<string> log)
    {
        this.log = log;
        try
        {
            // BepInEx plugins share their manager object. Keep this source off
            // that object so other plugins' GetComponent<AudioSource>() cannot
            // accidentally stop or reconfigure our playback.
            audioHost = new GameObject("GearEnhance.Audio");
            audioHost.transform.SetParent(host.transform, false);
            source = audioHost.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = false; source.spatialBlend = 0;
            source.ignoreListenerPause = true;
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
        source.enabled = true; source.mute = false; source.pitch = 1;
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
        Report(evolution ? FeedbackCue.EvolutionAttempt : FeedbackCue.Attempt, "예약");
        ScheduleProbe(evolution ? FeedbackCue.EvolutionAttempt : FeedbackCue.Attempt);
        return start;
    }
    internal void Play(FeedbackCue cue, float volume)
    {
        Prepare(volume);
        if (source.volume > 0) source.PlayOneShot(clips[EmbeddedSounds.Index(cue)]);
        Report(cue, "재생");
        ScheduleProbe(cue);
    }
    private void ScheduleProbe(FeedbackCue cue)
    {
        probeCue = cue;
        probeAt = Time.realtimeSinceStartup + 0.25f;
    }
    internal void Tick()
    {
        if (probeAt <= 0 || Time.realtimeSinceStartup < probeAt) return;
        probeAt = 0;
        if (source != null) Report(probeCue, "시작 후 상태");
    }
    private void Report(FeedbackCue cue, string action) => log?.Invoke(
        $"강화 효과음 {action}: {cue}, 음량={source.volume:0.##}, 활성={source.isActiveAndEnabled}, 재생중={source.isPlaying}, 가상음성={source.isVirtual}, 샘플={source.timeSamples}, 리스너정지={AudioListener.pause}, 리스너음량={AudioListener.volume:0.##}, 믹서={source.outputAudioMixerGroup.name}");
    internal void Stop() { probeAt = 0; if (source != null) source.Stop(); }
    public void Dispose()
    {
        Stop();
        if (audioHost != null) UnityEngine.Object.Destroy(audioHost);
        audioHost = null;
        source = null;
        for (var index = 0; index < clips.Length; index++)
        {
            if (clips[index] != null) UnityEngine.Object.Destroy(clips[index]);
            clips[index] = null;
        }
    }
}
