using System;
using UnityEngine;
using UnityEngine.Audio;

namespace GearEnhance;

internal sealed class EnhancementAudio : IDisposable
{
    private readonly WaveSound[] waves = new WaveSound[EmbeddedSounds.Count];
    private readonly AudioClip[] clips = new AudioClip[EmbeddedSounds.Count];
    private readonly GameObject host;
    private readonly Action<string> log;
    private readonly Func<bool> detailedLogging;
    private GameObject audioHost;
    private AudioSource source;
    private AudioClip nativeClip;
    private volatile bool audioReset;
    private bool nativeRoute, probeNative, probeAttempt;
    private float probeAt, playbackVolume;
    private double playbackStart;
    private FeedbackCue probeCue;

    internal EnhancementAudio(GameObject host, WaveSound charge, Action<string> log, Func<bool> detailedLogging)
    {
        this.host = host; this.log = log; this.detailedLogging = detailedLogging;
        // Cache PCM only. EFT can reset the audio device during startup, which
        // invalidates procedural clips created before its sound initialization.
        for (var index = 0; index < waves.Length; index++)
            waves[index] = index == 0 ? charge : EmbeddedSounds.Load(index);
        AudioSettings.OnAudioConfigurationChanged += ConfigurationChanged;
    }
    private void ConfigurationChanged(bool deviceChanged) => audioReset = true;

    private AudioClip CreateClip(int index, float volume = 1, int offset = 0)
    {
        var wave = waves[index];
        var samples = wave.SamplesFrom(offset, volume);
        var clip = AudioClip.Create("GearEnhance." + index, wave.Frames - offset, wave.Channels, wave.SampleRate, false);
        if (clip == null) throw new InvalidOperationException("강화 음원을 만들지 못했습니다.");
        clip.hideFlags = HideFlags.HideAndDontSave;
        if (clip.SetData(samples, 0)) return clip;
        UnityEngine.Object.Destroy(clip);
        throw new InvalidOperationException("강화 음원을 읽지 못했습니다.");
    }
    // 1.4.0: called when the window opens, so the source and the ten clips (several seconds of
    // PCM each) exist before the first charge and its sound starts with the gauge. Nothing that
    // is playing is stopped. Returns false when the game's UI mixer is not there yet.
    internal bool Warm()
    {
        var group = GameFeedback.MixerGroup() as AudioMixerGroup;
        if (group == null) return false;
        if (audioReset)
        {
            if (source != null && source.isPlaying || nativeClip != null) return false;
            audioReset = false; nativeRoute = false; ReleaseAudio();
            log?.Invoke("게임 오디오 재설정 감지: 강화 음원 다시 준비");
        }
        Build(group);
        return true;
    }
    private void Prepare(float volume)
    {
        // Wait until the actual game's UI mixer exists before allocating Unity
        // audio objects; rebuild them after any subsequent device reset.
        var group = GameFeedback.MixerGroup() as AudioMixerGroup;
        if (group == null) throw new InvalidOperationException("게임 UI 믹서에 연결할 수 없습니다.");
        if (audioReset)
        {
            audioReset = false; nativeRoute = false; ReleaseAudio();
            log?.Invoke("게임 오디오 재설정 감지: 강화 음원 다시 준비");
        }
        Stop();
        Build(group);
        source.outputAudioMixerGroup = group;
        source.enabled = true; source.mute = false; source.pitch = 1;
        source.volume = Mathf.Clamp01(volume); source.clip = null;
        playbackVolume = source.volume;
    }
    private void Build(AudioMixerGroup group)
    {
        if (source == null)
        {
            audioHost = new GameObject("GearEnhance.Audio");
            audioHost.transform.SetParent(host.transform, false);
            source = audioHost.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = false; source.spatialBlend = 0;
            source.ignoreListenerPause = true; source.priority = 32;
        }
        for (var index = 0; index < clips.Length; index++)
        {
            if (clips[index] != null && clips[index].loadState == AudioDataLoadState.Loaded) continue;
            if (clips[index] != null) UnityEngine.Object.Destroy(clips[index]);
            clips[index] = CreateClip(index);
        }
        if (source.outputAudioMixerGroup != group) source.outputAudioMixerGroup = group;
    }
    // cue: Attempt, EvolutionAttempt or MysteryAttempt (the gauge follows that clip's length).
    internal double StartAttempt(float volume, FeedbackCue cue)
    {
        Prepare(volume);
        playbackStart = AudioSettings.dspTime + (nativeRoute ? 0 : 0.05);
        if (nativeRoute) PlayNative(cue, 0);
        else { source.clip = clips[EmbeddedSounds.Index(cue)]; source.PlayScheduled(playbackStart); }
        Report(cue, nativeRoute ? "게임 UI 경로 요청" : "예약");
        ScheduleProbe(cue, true);
        return playbackStart;
    }
    internal void Play(FeedbackCue cue, float volume)
    {
        Prepare(volume);
        playbackStart = AudioSettings.dspTime;
        if (nativeRoute) PlayNative(cue, 0);
        else if (playbackVolume > 0) source.PlayOneShot(clips[EmbeddedSounds.Index(cue)]);
        Report(cue, nativeRoute ? "게임 UI 경로 요청" : "재생 요청");
        ScheduleProbe(cue, false);
    }
    private void PlayNative(FeedbackCue cue, int offset)
    {
        if (playbackVolume <= 0) return;
        if (nativeClip != null) UnityEngine.Object.Destroy(nativeClip);
        // GUISounds' mixed (non-single) UI path ignores its volume argument.
        // Scale our own PCM instead; do not stop or change the shared UI source.
        nativeClip = CreateClip(EmbeddedSounds.Index(cue), playbackVolume, offset);
        GameFeedback.PlayClip(nativeClip);
    }
    private void ScheduleProbe(FeedbackCue cue, bool attempt)
    {
        probeCue = cue; probeAttempt = attempt; probeNative = nativeRoute;
        probeAt = Time.realtimeSinceStartup + 0.25f;
    }
    internal void Tick()
    {
        if (probeAt <= 0 || Time.realtimeSinceStartup < probeAt) return;
        probeAt = 0;
        if (probeNative) { log?.Invoke("강화 효과음 게임 UI 경로 요청 완료: " + probeCue); return; }
        if (source == null) return;
        Report(probeCue, "시작 후 상태");
        if (source.isPlaying || playbackVolume <= 0) return;
        var wave = waves[EmbeddedSounds.Index(probeCue)];
        var elapsed = Math.Max(0, AudioSettings.dspTime - playbackStart);
        // Never retry audio that has already finished (e.g. after a long frame).
        if (elapsed >= wave.Duration) return;
        nativeRoute = true;
        source.Stop();
        // Keep the charge's remaining sound aligned with the existing gauge.
        // Results restart from the beginning so the outcome cue stays audible.
        var offset = probeAttempt ? Math.Min(wave.Frames - 1, (int)(elapsed * wave.SampleRate)) : 0;
        PlayNative(probeCue, offset);
        log?.Invoke($"전용 강화 소스 재생 미시작: 게임 UI 경로로 전환 ({probeCue}, 건너뛴 프레임={offset})");
    }
    private void Report(FeedbackCue cue, string action)
    {
        if (!detailedLogging()) return;
        log?.Invoke($"강화 효과음 {action}: {cue}, 음량={source.volume:0.##}, 활성={source.isActiveAndEnabled}, 재생중={source.isPlaying}, 가상음성={source.isVirtual}, 샘플={source.timeSamples}, 클립={clips[EmbeddedSounds.Index(cue)].loadState}, 리스너정지={AudioListener.pause}, 리스너음량={AudioListener.volume:0.##}, 믹서={source.outputAudioMixerGroup.name}");
    }
    internal void Stop()
    {
        probeAt = 0;
        if (source != null) source.Stop();
        // This clip belongs only to Gear Enhance; leave native UI sources alone.
        if (nativeClip != null) UnityEngine.Object.Destroy(nativeClip);
        nativeClip = null;
    }
    private void ReleaseAudio()
    {
        Stop();
        if (audioHost != null) UnityEngine.Object.Destroy(audioHost);
        audioHost = null; source = null;
        for (var index = 0; index < clips.Length; index++)
        {
            if (clips[index] != null) UnityEngine.Object.Destroy(clips[index]);
            clips[index] = null;
        }
    }
    public void Dispose()
    {
        AudioSettings.OnAudioConfigurationChanged -= ConfigurationChanged;
        ReleaseAudio();
    }
}
