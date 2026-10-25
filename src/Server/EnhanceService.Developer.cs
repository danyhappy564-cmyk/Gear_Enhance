using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace GearEnhance;

// 1.4.0 developer sandbox (user request): what is enhanced while the F12 developer options are
// unlocked does not stay. The records are copied when they unlock and put back when they lock,
// when another game launch talks to the server, or when the game starts again (Alt+F4 included).
// Roubles and materials spent, and gear destroyed, in that time are not given back.
public sealed partial class EnhanceService
{
    // Called with every request that carries the client settings.
    private void Sandbox(MongoId session, PmcData profile, EnhanceState state, EnhanceSettings settings, List<Item> changed)
    {
        var sandbox = state.Sandbox;
        if (sandbox != null && (!settings.Developer || sandbox.LaunchId != settings.LaunchId))
            EndSandbox(session, profile, state, changed);
        if (settings.Developer && state.Sandbox == null)
            state.Sandbox = new DeveloperSandbox
            {
                LaunchId = settings.LaunchId ?? "", StartedUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Records = serializer.Serialize<EnhanceRecords>(state)!
            };
    }

    // client/game/start (GameStartPatch): a new launch ends the last one's sandbox before the
    // client loads the profile, so durability put back here reaches the game too.
    public bool GameStarted(MongoId session)
    {
        lock (sync)
        {
            var saved = backend.Profile(session);
            var profile = saved?.CharacterData?.PmcData;
            if (saved?.ExtensionData == null || !saved.ExtensionData.ContainsKey(StateKey) || profile?.Inventory?.Items == null) return false;
            var state = State(session);
            if (state.Sandbox == null) return false;
            EndSandbox(session, profile, state, new List<Item>());
            return true;
        }
    }

    private void EndSandbox(MongoId session, PmcData profile, EnhanceState state, List<Item> changed)
    {
        var sandbox = state.Sandbox!;
        state.Sandbox = null;
        pending.Remove(session);
        EnhanceRecords? saved = null;
        try { saved = string.IsNullOrEmpty(sandbox.Records) ? null : serializer.Deserialize<EnhanceRecords>(sandbox.Records); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { }
        if (saved == null)
        {
            state.Notice = "Gear Enhance: 개발자 옵션 전 강화 기록을 읽지 못해 되돌리지 못했습니다. "
                + "The records from before the developer options could not be read, so nothing was rolled back.";
            return;
        }
        var before = state.Grants.ToDictionary(g => g.Key, g => g.Value.Amount);
        // Raid results and settings of this session stay: the gear lost or mailed meanwhile is real.
        var (returns, forfeited, settings, auto) = (state.Returns, state.Forfeited, state.LastSettings, state.AutoRegisterInserts);
        SealedStateConverter.Copy(saved, state);
        (state.Returns, state.Forfeited, state.LastSettings, state.AutoRegisterInserts, state.Sandbox) = (returns, forfeited, settings, auto, null);
        Normalize(state);
        // Max durability added by an armor 2nd transcendence goes back to the saved amounts.
        var items = new Dictionary<string, Item>();
        foreach (var item in InventoryItems(profile)) items.TryAdd(item.Id.ToString(), item);
        foreach (var id in before.Keys.Union(state.Grants.Keys).ToArray())
        {
            var delta = (state.Grants.GetValueOrDefault(id)?.Amount ?? 0) - before.GetValueOrDefault(id);
            if (state.Grants.TryGetValue(id, out var grant)) GrantIndex.Set(id, grant.Amount); else GrantIndex.Remove(id);
            if (Math.Abs(delta) < 0.005 || !items.TryGetValue(id, out var target) || target.Upd?.Repairable == null) continue;
            Grant(target.Upd.Repairable, delta);
            if (!changed.Contains(target)) changed.Add(target);
        }
        state.Notice = "Gear Enhance: 개발자 옵션을 켠 동안의 강화를 되돌렸습니다(쓴 루블·재료와 파괴된 장비는 돌아오지 않음). "
            + "Enhancements made while the developer options were on were rolled back (spent roubles, materials and destroyed gear do not come back).";
    }
}

// client/game/start: the first request of every game launch.
public sealed class GameStartPatch : SPTarkov.Reflection.Patching.AbstractPatch
{
    internal static EnhanceService? Service;
    private static bool reported;

    protected override System.Reflection.MethodBase? GetTargetMethod() => typeof(SPTarkov.Server.Core.Controllers.GameController).GetMethod(
        "GameStart", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    [SPTarkov.Reflection.Patching.PatchPrefix]
    public static void Prefix(MongoId sessionId)
    {
        // The game start must go on whatever happens here.
        try
        {
            if (Service?.GameStarted(sessionId) == true)
                RaidEndPatch.Logger?.Info("Gear Enhance: 지난 실행에서 개발자 옵션을 켠 동안의 강화를 되돌렸습니다. "
                    + "Enhancements made with the developer options on in the last launch were rolled back.");
        }
        catch (Exception ex)
        {
            if (reported) return;
            reported = true;
            RaidEndPatch.Logger?.Error("Gear Enhance: 개발자 옵션 강화 되돌리기 실패(게임 시작은 정상 진행). Developer rollback failed (game start continues).", ex);
        }
    }
}
