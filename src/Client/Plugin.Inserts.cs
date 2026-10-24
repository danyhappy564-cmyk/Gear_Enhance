using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;

namespace GearEnhance;

// 1.3.0: a plate (soft insert, visor) put into enhanced armor gets its level at once when
// that is free; one taken out of transcended armor loses the added max durability once it
// sits in other armor. The server does both; this side notices the insert and asks for it.
public sealed partial class Plugin
{
    private ConfigEntry<bool> autoRegister;
    private DevSetting<float> insertSurcharge;
    private static volatile bool insertsWanted;
    private static double nextInsertSync;
    // Inserts since the last sync, so pricier plates still waiting for payment can be named.
    private static readonly ConcurrentQueue<(string Armor, string Part)> inserted = new ConcurrentQueue<(string, string)>();

    private void BindInserts()
    {
        autoRegister = BindGeneral("1. 기본", "방탄판 자동 등록", true,
            "강화된 방어구에 방탄판·내피·바이저를 끼우면 바로 그 방어구 단계를 받습니다(같거나 더 싼 방탄판, 무료). 더 비싼 방탄판은 보험·방탄판 창에서 차액을 내고 등록합니다. 끄면 모두 버튼으로 등록합니다.");
        insertSurcharge = BindDeveloper("3. 비용", "방탄판 차액 배율 (%)", 100f, new ConfigDescription(
            "더 비싼 방탄판 차액 = 가격 차이 × 가진 단계마다 (그 단계 비용 비율 × 평균 시도 횟수) × 이 값/100. 100이면 처음부터 그 방탄판으로 강화한 평균 비용과 같습니다.",
            new AcceptableValueRange<float>(0, 1000)));
    }

    // From the slot hook, on the main thread. Several plates dropped in a row share one request.
    internal static void InsertAdded(string armorId, string partId)
    {
        inserted.Enqueue((armorId, partId));
        nextInsertSync = Clock + 0.5;
        insertsWanted = true;
    }

    private void TickInserts()
    {
        if (insertsWanted && !visible && !busy && !backgroundSyncing && Clock >= nextInsertSync) SyncInserts();
    }

    private async void SyncInserts()
    {
        insertsWanted = false;
        var seen = new List<(string Armor, string Part)>();
        for (var n = 0; n < 256 && inserted.TryDequeue(out var pair); n++) seen.Add(pair);
        var active = StatPatches.Session;
        if (active == null || GameBridge.Get(GameBridge.RuntimeType("EFT.InGameStatus"), "InRaid") is not false) return;
        backgroundSyncing = true;
        try
        {
            var settings = JsonConvert.SerializeObject(Settings());
            await GameBridge.Flush(active);
            if (!await GameBridge.WithTimeout(GameBridge.SyncInserts(active, settings, autoRegister.Value)))
            {
                Logger.LogWarning("방탄판 맞추기 실패(서버가 거절). 보험·방탄판 창에서 등록할 수 있습니다.");
                return;
            }
            if (await SyncEffects(active)) main.Enqueue(ItemMarkers.RefreshAll);
            var waiting = InsertRules.Waiting(StatPatches.Registry, seen);
            if (waiting > 0) main.Enqueue(() => Notify(InsertRules.Notice(waiting, autoRegister.Value)));
        }
        catch (Exception ex) { Logger.LogWarning("방탄판 맞추기 실패: " + ex.GetBaseException().Message); }
        finally { backgroundSyncing = false; }
    }

    private void Notify(string text)
    {
        try
        {
            var manager = GameBridge.RuntimeType("EFT.Communications.NotificationManager");
            var method = manager?.GetMethods().FirstOrDefault(m => m.Name == "DisplayWarningNotification" && m.GetParameters().Length == 2);
            if (method == null) { Logger.LogInfo(text); return; }
            method.Invoke(null, new[] { text, method.GetParameters()[1].DefaultValue });
        }
        catch (Exception ex) { Logger.LogWarning("방탄판 안내 표시 실패: " + ex.GetBaseException().Message); }
    }
}

// Which inserts the server should hear about, and the notice text. Unity-free for tests.
internal static class InsertRules
{
    // Enhanced armor with a part it has not registered (auto-register or a paid one), 2-star
    // transcended armor (a part back in it gets its max durability again), or a part carrying
    // added max durability (it may now sit in other armor).
    internal static bool Relevant(EffectRegistry registry, string armorId, string partId) =>
        !string.IsNullOrEmpty(armorId) && !string.IsNullOrEmpty(partId)
        && (registry.Find(armorId) != null && !registry.Registered(armorId, partId)
            || registry.TranscendLevel(armorId, TranscendTrack.Second) > 0 || registry.Grant(partId) > 0);

    // After the sync: parts still unregistered in enhanced armor (pricier, or auto-register off).
    internal static int Waiting(EffectRegistry registry, IEnumerable<(string Armor, string Part)> seen) =>
        seen.Distinct().Count(p => registry.Find(p.Armor) != null && !registry.Registered(p.Armor, p.Part));

    internal static string Notice(int count, bool automatic) => automatic
        ? Loc.L($"더 비싼 방탄판 {count}개는 차액을 내야 강화 효과를 받습니다. 강화 창 → 보험·방탄판 → 방탄판 등록",
            $"{count} pricier plate(s) need the price difference paid before they share the enhancement. Enhance window → Insurance · plates → Register")
        : Loc.L($"강화된 방어구에 끼운 방탄판 {count}개가 아직 등록되지 않았습니다. 강화 창 → 보험·방탄판 → 방탄판 등록",
            $"{count} plate(s) in enhanced armor are not registered yet. Enhance window → Insurance · plates → Register");
}

// The game raises this for every item placed in a slot (weapon mods and magazines too), so it
// leaves at once unless the item is an armor part going into armor outside a raid.
internal static class InsertWatch
{
    private static bool reported;

    internal static void Install(Harmony harmony)
    {
        var address = GameBridge.RuntimeType("EFT.InventoryLogic.SlotItemAddress") ?? throw new TypeLoadException("SlotItemAddress");
        harmony.Patch(GameBridge.RequiredMethod(address, "RaiseAddEvent", 3), postfix: new HarmonyMethod(typeof(InsertWatch), nameof(Added)));
    }

    private static void Added(object __instance, object[] __args)
    {
        try
        {
            if (__args.Length < 2 || __args[1]?.ToString() != "Succeed" || StatPatches.Session == null) return;
            if (GameBridge.Get(GameBridge.RuntimeType("EFT.InGameStatus"), "InRaid") is not false) return;
            var part = __args[0];
            var armor = GameBridge.Get(GameBridge.Get(__instance, "Slot"), "ParentItem");
            if (armor == null || !GameBridge.IsArmorPart(part)) return;
            var armorId = GameBridge.Get(armor, "Id")?.ToString();
            var partId = GameBridge.Get(part, "Id")?.ToString();
            if (InsertRules.Relevant(StatPatches.Registry, armorId, partId)) Plugin.InsertAdded(armorId, partId);
        }
        // Never break the game's item move.
        catch (Exception ex)
        {
            if (reported) return;
            reported = true;
            Plugin.LogError("Gear Enhance: 방탄판 끼우기 감지 실패(보험·방탄판 창 등록은 정상). Plate insert hook failed: " + ex);
        }
    }
}
