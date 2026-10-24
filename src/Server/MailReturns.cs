using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Dialog;
using SPTarkov.Server.Core.Services.Commerce;

namespace GearEnhance;

// Insurance returns and expired/cancelled flea offers reach the player as mail.
// SPT clones those attachments with new ids, which used to orphan the records.
[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public sealed class MailReturnLoader(EnhanceService service, ISptLogger<MailReturnLoader> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        MailReturnPatch.Service = service;
        MailReturnPatch.Logger = logger;
        try
        {
            new MailReturnPatch().Enable();
            // One line in the server log confirms the 1.2.0 server half is loaded.
            logger.Info($"Gear Enhance {EnhanceProtocol.ModVersion}: 보험·플리 반환 장비 강화 기록 연결 준비 완료. "
                + "Insurance/flea return records hook ready.");
        }
        catch (Exception ex)
        {
            // Enhancement itself keeps working; only returned mail stays unlinked.
            logger.Error("Gear Enhance: 보험·플리 반환 우편 연결 실패. 돌아온 장비의 강화 기록은 이어지지 않습니다. "
                + "Insurance/flea return mail hook failed; returned gear will lose its enhancement.", ex);
        }
        RaidEndPatch.Service = service;
        RaidEndPatch.Logger = logger;
        try { new RaidEndPatch().Enable(); }
        catch (Exception ex)
        {
            logger.Error("Gear Enhance: 강화 보험 레이드 종료 연결 실패. 잃은 강화 장비는 강화 보험으로 돌아오지 않습니다. "
                + "Enhancement insurance raid-end hook failed; lost enhanced gear will not be returned by it.", ex);
        }
        return Task.CompletedTask;
    }
}

public sealed class MailReturnPatch : AbstractPatch
{
    internal static EnhanceService? Service;
    internal static ISptLogger<MailReturnLoader>? Logger;
    private static bool reported;
    // SendMessageToPlayer never re-enters itself; each call overwrites the slot.
    [ThreadStatic] private static Capture? current;

    internal sealed record Capture(SendMessageDetails Details, List<Item> Items, (string Id, string Template)[] Before);

    protected override MethodBase? GetTargetMethod() => typeof(MailSendService).GetMethod(
        nameof(MailSendService.SendMessageToPlayer), BindingFlags.Public | BindingFlags.Instance, [typeof(SendMessageDetails)]);

    [PatchPrefix]
    public static void Prefix(SendMessageDetails messageDetails)
    {
        current = null;
        try
        {
            if (messageDetails?.Items is { Count: > 0 } items)
                current = new(messageDetails, items, items.Select(i => (i.Id.ToString(), i.Template.ToString())).ToArray());
        }
        catch (Exception ex) { Report(ex); }
    }

    [PatchPostfix]
    public static void Postfix(SendMessageDetails messageDetails)
    {
        var captured = current;
        current = null;
        // Mail must be delivered even if linking a record fails.
        try
        {
            if (captured == null || !ReferenceEquals(captured.Details, messageDetails) || Service == null) return;
            var pairs = Pairs(captured.Before, captured.Items, messageDetails.Items);
            if (pairs.Count == 0) return;
            var moved = Service.Remap(messageDetails.RecipientId, pairs);
            if (moved > 0) Logger?.Info($"Gear Enhance: 반환 우편 장비 {moved}개의 강화 기록을 새 아이템으로 이었습니다. "
                + $"Linked enhancement records of {moved} returned item(s).");
        }
        catch (Exception ex) { Report(ex); }
    }

    // SPT replaces the list with Clone(items).ReplaceIDs(): same order and templates.
    // Anything else (no clone, filtered or reordered list) is left untouched.
    internal static List<(string Old, string New)> Pairs((string Id, string Template)[] before, List<Item> original, List<Item>? after)
    {
        var pairs = new List<(string, string)>();
        if (after == null || ReferenceEquals(after, original) || after.Count != before.Length) return pairs;
        for (var index = 0; index < before.Length; index++)
        {
            if (after[index]?.Template.ToString() != before[index].Template) return new();
            pairs.Add((before[index].Id, after[index].Id.ToString()));
        }
        return pairs;
    }

    private static void Report(Exception ex)
    {
        if (reported) return;
        reported = true;
        Logger?.Error("Gear Enhance: 반환 우편 강화 기록 연결 오류(우편은 정상 발송). Return mail link error (mail still sent).", ex);
    }
}
