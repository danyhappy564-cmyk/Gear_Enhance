using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Eft.Profile;

namespace GearEnhance;

// Prestige insurance (1.4.0). SPT builds the next prestige's character inside CreateProfile and
// then hands the old copy and the new one to ProcessPendingPrestige, which mails the items chosen
// for transfer. Before it the new character gets the insured gear's records; after it the insured
// gear SPT did not transfer is mailed. Raid deaths never come here.
public sealed class PrestigePatch : AbstractPatch
{
    internal static EnhanceService? Service;
    internal static ISptLogger<MailReturnLoader>? Logger;
    private static bool reported;
    // ProcessPendingPrestige never re-enters itself; each call overwrites the slot.
    [ThreadStatic] private static (SptProfile Fresh, PrestigeCarry Carry)? current;

    protected override MethodBase? GetTargetMethod() => typeof(PrestigeHelper).GetMethod(
        nameof(PrestigeHelper.ProcessPendingPrestige), BindingFlags.Public | BindingFlags.Instance);

    [PatchPrefix]
    public static void Prefix(SptProfile oldProfile, SptProfile newProfile, PendingPrestige prestige)
    {
        current = null;
        try
        {
            var transfers = prestige?.Items?.Select(r => r.Id.ToString()) ?? Enumerable.Empty<string>();
            if (Service?.BeginPrestige(oldProfile, newProfile, prestige?.PrestigeLevel, transfers) is { } carry) current = (newProfile, carry);
        }
        catch (Exception ex) { Report(ex); }
    }

    [PatchPostfix]
    public static void Postfix(SptProfile newProfile)
    {
        var captured = current;
        current = null;
        // The new character must be created even if the return fails.
        try
        {
            if (captured is not { } value || !ReferenceEquals(value.Fresh, newProfile) || Service == null) return;
            var (sent, failed) = Service.CompletePrestige(value.Carry);
            Logger?.Info($"Gear Enhance: 프레스티지 {value.Carry.Level} — 프레스티지 보험 장비 우편 {sent}개 발송"
                + (failed > 0 ? $", {failed}개 실패" : "") + $". Prestige {value.Carry.Level}: mailed {sent} insured gear"
                + (failed > 0 ? $", {failed} failed." : "."));
        }
        catch (Exception ex) { Report(ex); }
    }

    private static void Report(Exception ex)
    {
        if (reported) return;
        reported = true;
        Logger?.Error("Gear Enhance: 프레스티지 보험 처리 오류(프레스티지는 정상 진행). Prestige insurance error (prestige continues).", ex);
    }
}
