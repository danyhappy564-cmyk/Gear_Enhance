using System;
using System.Reflection;
using HarmonyLib;

namespace GearEnhance;

internal enum FeedbackCue { Attempt, Success, Failure, Destroy, Complete, Downgrade, EvolutionAttempt }
internal enum UiInteraction { Button, CheckBox, Open, Close }
internal static class GameFeedback
{
    private static Type soundType, singletonType, guiType;
    private static MethodInfo play, mixerGroup, playClip;
    internal static string SoundName(FeedbackCue cue) => cue == FeedbackCue.Attempt || cue == FeedbackCue.EvolutionAttempt ? "MenuInstallModFunc"
        : cue == FeedbackCue.Success ? "RepairComplete" : cue == FeedbackCue.Destroy ? "MenuWeaponDisassemble"
        : cue == FeedbackCue.Complete ? "QuestCompleted" : "ErrorMessage";
    internal static string SoundName(UiInteraction cue) => cue switch
    {
        UiInteraction.CheckBox => "MenuCheckBox", UiInteraction.Open => "MenuInspectorWindowOpen",
        UiInteraction.Close => "MenuInspectorWindowClose", _ => "ButtonClick"
    };
    internal static FeedbackCue Result(EnhanceReceipt receipt, bool complete) => receipt.Success
        ? complete ? FeedbackCue.Complete : FeedbackCue.Success
        : receipt.FailureMode == FailureMode.Destroy ? FeedbackCue.Destroy
        : receipt.FailureMode == FailureMode.Downgrade ? FeedbackCue.Downgrade : FeedbackCue.Failure;

    private static object Gui()
    {
        if (guiType == null)
        {
            var resolvedGui = GameBridge.RuntimeType("EFT.UI.GUISounds") ?? throw new TypeLoadException("GUISounds");
            var resolvedSingleton = (GameBridge.RuntimeType("Comfort.Common.Singleton`1")
                ?? throw new TypeLoadException("Comfort.Common.Singleton")).MakeGenericType(resolvedGui);
            guiType = resolvedGui; singletonType = resolvedSingleton;
        }
        return GameBridge.Get(singletonType, "Instance") ?? throw new InvalidOperationException("게임 UI 소리가 아직 준비되지 않았습니다.");
    }

    internal static object MixerGroup()
    {
        var instance = Gui();
        if (mixerGroup == null) mixerGroup = GameBridge.RequiredMethod(guiType, "GetCommonSoundsMixerGroup", 0);
        return mixerGroup.Invoke(instance, null) ?? throw new InvalidOperationException("게임 UI 믹서가 아직 준비되지 않았습니다.");
    }
    internal static void PlayClip(object clip)
    {
        var instance = Gui();
        if (playClip == null) playClip = GameBridge.RequiredMethod(guiType, "PlaySound", 4);
        // Mixed UI playback: don't stop a native source or replace its clip.
        playClip.Invoke(instance, new[] { clip, (object)false, false, 1f });
    }
    internal static void Play(FeedbackCue cue) => PlayNamed(SoundName(cue));
    internal static void Play(UiInteraction cue) => PlayNamed(SoundName(cue));
    // The trader insurance screen's confirmation sound; older names as fallbacks.
    internal static void PlayInsured()
    {
        Gui();
        if (play == null) { play = GameBridge.RequiredMethod(guiType, "PlayUISound", 1); soundType = play.GetParameters()[0].ParameterType; }
        foreach (var name in new[] { "InsuranceInsured", "InsuranceItemOnInsure" })
            if (Enum.IsDefined(soundType, name)) { PlayNamed(name); return; }
        PlayNamed(SoundName(FeedbackCue.Success));
    }
    private static void PlayNamed(string name)
    {
        var instance = Gui();
        if (play == null)
        {
            play = GameBridge.RequiredMethod(guiType, "PlayUISound", 1);
            soundType = play.GetParameters()[0].ParameterType;
        }
        play.Invoke(instance, new[] { Enum.Parse(soundType, name) });
    }
}
