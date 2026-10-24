using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Services.InRaid;

namespace GearEnhance;

// Enhancement insurance (1.2.0). Around SPT's post-raid PMC handling: before it the
// server inventory still holds the pre-raid gear, after it lost gear is gone and the
// trader insurance packages for this raid are already queued.
public sealed class RaidEndPatch : AbstractPatch
{
    internal static EnhanceService? Service;
    internal static ISptLogger<MailReturnLoader>? Logger;
    private static bool reported;
    // HandlePostRaidPmc never re-enters itself; each call overwrites the slot.
    [ThreadStatic] private static (MongoId Session, RaidCapture Capture)? current;

    protected override MethodBase? GetTargetMethod() => typeof(LocationLifecycleService).GetMethod(
        "HandlePostRaidPmc", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

    [PatchPrefix]
    public static void Prefix(MongoId sessionId)
    {
        current = null;
        try
        {
            if (Service?.BeginRaidEnd(sessionId) is { } capture) current = (sessionId, capture);
        }
        catch (Exception ex) { Report(ex); }
    }

    [PatchPostfix]
    public static void Postfix(MongoId sessionId, EndLocalRaidRequestData request)
    {
        var captured = current;
        current = null;
        // Raid end must finish even if insurance handling fails.
        try
        {
            if (captured is not { } value || value.Session != sessionId || Service == null) return;
            var (returned, forfeited) = Service.CompleteRaidEnd(sessionId, value.Capture, request?.Results?.Profile);
            if (returned + forfeited > 0)
                Logger?.Info($"Gear Enhance: 레이드에서 잃은 강화 장비 — 강화 보험으로 반환 {returned}개, 강화 소멸 {forfeited}개. "
                    + $"Lost enhanced gear: {returned} returned by enhancement insurance, {forfeited} lost their enhancement.");
        }
        catch (Exception ex) { Report(ex); }
    }

    private static void Report(Exception ex)
    {
        if (reported) return;
        reported = true;
        Logger?.Error("Gear Enhance: 강화 보험 레이드 종료 처리 오류(레이드 종료는 정상 진행). Enhancement insurance raid-end error (raid end continues).", ex);
    }
}
