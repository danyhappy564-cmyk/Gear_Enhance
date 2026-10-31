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
    public static void Prefix(MongoId sessionId, string locationName)
    {
        current = null;
        try
        {
            // User 2026-10-28: after a Terminal raid enhancements were gone and the enhancement
            // insurance mailed a second copy. Manimal Terminal puts the whole pre-raid inventory
            // back after a PMC raid there (after this method), so nothing in it was lost.
            if (TerminalRestores(sessionId, locationName))
            {
                Logger?.Info("Gear Enhance: 터미널 레이드 — 장비가 입장 전으로 복구되므로 강화 보험 처리를 건너뜁니다. "
                    + "Terminal raid: gear is restored to its pre-raid state, enhancement insurance skipped.");
                return;
            }
            if (Service?.BeginRaidEnd(sessionId) is { } capture) current = (sessionId, capture);
        }
        catch (Exception ex) { Report(ex); }
    }

    private const string TerminalType = "Manimal.Terminal.Server.TerminalSelfContained";
    private static Type? terminal;
    private static bool searched;

    // Manimal Terminal's own snapshot of this raid; without the field (another version) trust the mod.
    internal static bool TerminalRestores(MongoId session, string? location)
    {
        if (!string.Equals(location, "terminal", StringComparison.OrdinalIgnoreCase)) return false;
        if (!searched)
        {
            searched = true;
            terminal = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(TerminalType, false)).FirstOrDefault(t => t != null);
        }
        if (terminal == null) return false;
        var field = terminal.GetField("_snapshots", BindingFlags.NonPublic | BindingFlags.Static);
        return field?.GetValue(null) is not System.Collections.IDictionary snapshots || snapshots.Contains(session.ToString());
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
            // Queued by EquipmentIsEternal's prefix on this same method when the player died.
            var restored = EternalRestores.Pending(sessionId, message => Logger?.Warning(message));
            if (restored != null)
            {
                var kept = value.Capture.Gear.Count(g => restored.Contains(g.Root));
                Logger?.Info($"Gear Enhance: EquipmentIsEternal이 사망 후 장비를 되돌립니다 — 강화 장비 {kept}개 기록 유지. "
                    + $"EquipmentIsEternal restores gear after death: {kept} enhanced items keep their records.");
            }
            var (returned, forfeited) = Service.CompleteRaidEnd(sessionId, value.Capture, request?.Results?.Profile, restored);
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
