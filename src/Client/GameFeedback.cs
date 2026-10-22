using System;
using System.Reflection;
using HarmonyLib;

namespace GearEnhance;

internal enum FeedbackCue { Attempt, Success, Failure, Destroy }
internal static class GameFeedback
{
    private static Type soundType, singletonType;
    private static MethodInfo play;
    internal static string SoundName(FeedbackCue cue) => cue == FeedbackCue.Attempt ? "MenuInstallModFunc"
        : cue == FeedbackCue.Success ? "RepairComplete" : cue == FeedbackCue.Destroy ? "MenuWeaponDisassemble" : "ErrorMessage";
    internal static void Play(FeedbackCue cue)
    {
        if (play == null)
        {
            var gui = GameBridge.RuntimeType("EFT.UI.GUISounds") ?? throw new TypeLoadException("GUISounds");
            play = GameBridge.RequiredMethod(gui, "PlayUISound", 1);
            soundType = play.GetParameters()[0].ParameterType;
            singletonType = (GameBridge.RuntimeType("Comfort.Common.Singleton`1")
                ?? throw new TypeLoadException("Comfort.Common.Singleton" )).MakeGenericType(gui);
        }
        var instance = GameBridge.Get(singletonType, "Instance");
        if (instance != null) play.Invoke(instance, new[] { Enum.Parse(soundType, SoundName(cue)) });
    }
}
