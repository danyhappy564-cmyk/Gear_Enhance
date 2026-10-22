using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace GearEnhance;

[BepInPlugin("rf.gearenhance", "Gear Enhance", EnhanceProtocol.ModVersion)]
public sealed partial class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance;
    private Harmony harmony;
    private ConfigEntry<bool> menuEnabled, always, soundsEnabled;
    private ConfigEntry<float> chance, percent, growth;
    private ConfigEntry<int> maxLevel, minimum;
    private ConfigEntry<PriceBasis> price;
    private ConfigEntry<FailureMode> failure;
    private readonly Dictionary<EnhancePath, (ConfigEntry<float> Step, ConfigEntry<float> Cap)> effectConfig
        = new Dictionary<EnhancePath, (ConfigEntry<float>, ConfigEntry<float>)>();
    private object session, uiContext, selectedItem, itemIcon;
    private string itemId, itemName;
    private EnhancePath selectedPath;
    private EnhanceQuote quote, preview;
    private bool visible, busy, destructionConfirmed, uncertain, weapon, firstQuote;
    private int generation, syncGeneration;
    private string message = "";
    private long repaintCount, repaintTicks, maximumRepaintTicks;
    private bool feedbackUnavailable;
    private Rect window;
    private GameObject blocker;
    private GUISkin windowSkin;
    private Font uiFont;
    private readonly ConcurrentQueue<Action> main = new ConcurrentQueue<Action>();

    private void Awake()
    {
        Instance = this;
        menuEnabled = Config.Bind("1. 기본", "강화 메뉴 사용", true, "창고의 무기·방어구 우클릭에 장비 강화를 표시합니다.");
        chance = Config.Bind("2. 확률", "성공률 (%)", 75f, new ConfigDescription("다음 견적부터 적용됩니다.", new AcceptableValueRange<float>(0, 100)));
        always = Config.Bind("2. 확률", "항상 성공 (100%)", false, "켜면 성공률 설정 대신 100%를 적용합니다. 비용은 소모됩니다.");
        maxLevel = Config.Bind("2. 확률", "최대 강화 단계", 10, new ConfigDescription("각 항목의 최대 단계입니다. 효과별 수치 상한은 별도로 적용합니다.", new AcceptableValueRange<int>(1, 100)));
        price = Config.Bind("3. 비용", "기준 가격", PriceBasis.Handbook, "Handbook = 북밸류, Flea = SPT 플리 기준가. 장비 본체만 계산합니다.");
        percent = Config.Bind("3. 비용", "기준 가격 대비 비용 (%)", 10f, new ConfigDescription("0이면 최소 비용만 적용됩니다.", new AcceptableValueRange<float>(0, 1000)));
        growth = Config.Bind("3. 비용", "단계별 비용 배수", 1.25f, new ConfigDescription("기준가 × 비용 비율 × 배수^현재단계. 최대 1억 루블.", new AcceptableValueRange<float>(1, 3)));
        minimum = Config.Bind("3. 비용", "최소 비용 (루블)", 1000, new ConfigDescription("창고 안의 잠기지 않은 루블을 사용합니다.", new AcceptableValueRange<int>(0, 100000000)));
        failure = Config.Bind("4. 실패", "실패 처리", FailureMode.KeepItem,
            "KeepItem: 비용만 소모 / ZeroDurability: 장비·부품의 현재 내구도 0 / Destroy: 본체·부착물·탄약 삭제. 최대 내구도는 유지됩니다.");
        BindEffect(EnhancePath.Recoil, "5. 무기 효과", 1.5f, 15);
        BindEffect(EnhancePath.Ergonomics, "5. 무기 효과", 1, 10);
        BindEffect(EnhancePath.Precision, "5. 무기 효과", 1, 10);
        BindEffect(EnhancePath.ArmorDurability, "6. 방어구 효과", 3, 30);
        BindEffect(EnhancePath.Mobility, "6. 방어구 효과", 2, 20);
        soundsEnabled = Config.Bind("7. 피드백", "강화 효과음", true, "시도·성공·실패에 게임 UI 효과음을 재생합니다. 게임의 소리 설정을 따릅니다.");
        StatPatches.Registry.Configure(Tuning());
        Config.SettingChanged += (_, _) => main.Enqueue(() =>
        {
            generation++; quote = preview = null; destructionConfirmed = false;
            try { StatPatches.Registry.Configure(Tuning()); RefreshGameplay(); }
            catch (Exception ex) { Logger.LogWarning(ex); }
            if (visible && !busy) message = "설정이 바뀌었습니다. 견적을 다시 불러와 주세요.";
        });
        try
        {
            harmony = new Harmony("rf.gearenhance");
            // Resolve hot status types during plugin startup, before a window is opened.
            GameBridge.RuntimeType("EFT.InGameStatus");
            GameBridge.RuntimeType("EFT.InventoryLogic.BackEndInventoryController");
            StatPatches.Install(harmony);
            ItemMarkers.Install(harmony);
            var target = GameBridge.RequiredMethod(GameBridge.RuntimeType("EFT.UI.ItemUiContext"), "GetItemContextInteractions", 2);
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(Plugin), nameof(MenuPostfix)));
            Logger.LogInfo("Gear Enhance " + EnhanceProtocol.ModVersion + ": 독립 강화·장비 표식·효과음 로드");
        }
        catch (Exception ex)
        {
            harmony?.UnpatchSelf();
            Logger.LogError("SPT API 연결 실패로 Gear Enhance 패치를 취소했습니다: " + ex);
        }
    }

    internal static void LogError(string text) => Instance?.Logger.LogError(text);

    private void BindEffect(EnhancePath path, string section, float step, float cap)
    {
        var name = EnhanceEffects.StatName(path);
        var unit = path == EnhancePath.Ergonomics ? "" : " (%)";
        effectConfig[path] = (
            Config.Bind(section, name + " / 단계" + unit, step, new ConfigDescription("기존 강화 장비에도 적용됩니다.", new AcceptableValueRange<float>(0, 10))),
            Config.Bind(section, name + " 상한" + unit, cap, new ConfigDescription("단계가 높아도 이 값을 넘지 않습니다.", new AcceptableValueRange<float>(0, path == EnhancePath.Ergonomics ? 50 : 80))));
    }
    private EffectTuning Tuning() => new EffectTuning
    {
        RecoilPerLevel = effectConfig[EnhancePath.Recoil].Step.Value, RecoilCap = effectConfig[EnhancePath.Recoil].Cap.Value,
        ErgonomicsPerLevel = effectConfig[EnhancePath.Ergonomics].Step.Value, ErgonomicsCap = effectConfig[EnhancePath.Ergonomics].Cap.Value,
        PrecisionPerLevel = effectConfig[EnhancePath.Precision].Step.Value, PrecisionCap = effectConfig[EnhancePath.Precision].Cap.Value,
        ArmorDurabilityPerLevel = effectConfig[EnhancePath.ArmorDurability].Step.Value, ArmorDurabilityCap = effectConfig[EnhancePath.ArmorDurability].Cap.Value,
        MobilityPerLevel = effectConfig[EnhancePath.Mobility].Step.Value, MobilityCap = effectConfig[EnhancePath.Mobility].Cap.Value
    };
    private EnhanceSettings Settings() => new EnhanceSettings
    {
        SuccessPercent = chance.Value, AlwaysSucceed = always.Value, MaxLevel = maxLevel.Value,
        PriceBasis = price.Value, CostPercent = percent.Value, CostPerLevel = growth.Value,
        MinimumCost = minimum.Value, FailureMode = failure.Value, Effects = Tuning()
    };

    private static void MenuPostfix(object __instance, object itemContext, object __result)
    {
        var plugin = Instance;
        if (plugin == null || !plugin.menuEnabled.Value || __result == null) return;
        try
        {
            if (!GameBridge.InStash(__instance)) return;
            var view = GameBridge.Get(itemContext, "ViewType")?.ToString();
            var item = GameBridge.Get(itemContext, "Item");
            if ((view == "Inventory" || view == "TradingPlayer") && GameBridge.Eligible(item))
                GameBridge.AddInteraction(__result, () => plugin.Open(__instance, item));
        }
        catch (Exception ex) { plugin.Logger.LogError(ex); }
    }

    private void Open(object context, object item)
    {
        if (busy || uncertain) return;
        try
        {
            session = GameBridge.Get(context, "ClientSession") ?? throw new InvalidOperationException("PMC 세션이 없습니다.");
            uiContext = context; selectedItem = item;
            itemId = GameBridge.Get(item, "Id")?.ToString() ?? throw new InvalidOperationException("아이템 ID가 없습니다.");
            weapon = GameBridge.IsWeapon(item);
            selectedPath = StatPatches.Registry.Find(itemId)?.Path ?? (weapon ? EnhancePath.Recoil : EnhancePath.ArmorDurability);
            itemName = GameBridge.Get(item, "ShortName")?.ToString() ?? itemId;
            var manager = GameBridge.RuntimeType("EFT.LocalizationManager");
            var localizer = manager?.GetMethod("LocalizedValue", new[] { typeof(string) });
            var instance = manager == null ? null : AccessTools.Property(manager, "Instance")?.GetValue(null);
            if (instance != null && localizer != null) itemName = localizer.Invoke(instance, new object[] { itemName })?.ToString() ?? itemName;
            itemIcon = GameBridge.RequiredMethod(GameBridge.RuntimeType("EFT.UI.DragAndDrop.ItemViewFactory"), "LoadItemIcon", 3)
                .Invoke(null, new[] { item, (object)1, false });
            CreateBlocker(); ResetWindow();
            visible = true; quote = null; preview = null; firstQuote = true; destructionConfirmed = false; generation++;
            RefreshQuote();
        }
        catch (Exception ex) { Logger.LogError(ex); Close(); }
    }

    internal async Task<bool> SyncEffects(object activeSession)
    {
        var expected = GameBridge.ProfileId(activeSession);
        var request = ++syncGeneration;
        if (expected.Length == 0) { StatPatches.Registry.Clear(); return false; }
        try
        {
            var snapshot = await GameBridge.WithTimeout(GameBridge.Query<EnhanceSnapshot>("/rf/gear-enhance/state", new { ProtocolVersion = EnhanceProtocol.Version }));
            if (request != syncGeneration || expected != GameBridge.ProfileId(activeSession)) return false;
            StatPatches.Registry.Replace(snapshot, expected);
            main.Enqueue(RefreshGameplay);
            return true;
        }
        catch (Exception ex)
        {
            if (request != syncGeneration) return false;
            if (StatPatches.Registry.ProfileId != expected) StatPatches.Registry.Clear();
            Logger.LogWarning("강화 효과 동기화 실패: " + ex.GetBaseException().Message);
            return false;
        }
    }

    private void RefreshGameplay()
    {
        try
        {
            if (selectedItem != null) GameBridge.RequiredMethod(selectedItem.GetType(), "UpdateAttributes", 0).Invoke(selectedItem, null);
            GameBridge.RefreshGameplay();
            ItemMarkers.Refresh(itemId);
        }
        catch (Exception ex) { Logger.LogWarning("강화 능력치 재계산: " + ex.GetBaseException().Message); }
    }

    private void RefreshQuote() => LoadQuote(null);
    private async void LoadQuote(string resultMessage)
    {
        if (busy || uncertain || itemId == null) return;
        busy = true; quote = null; message = resultMessage ?? "견적을 불러오는 중입니다…";
        var current = generation;
        try
        {
            await GameBridge.Flush(session);
            if (firstQuote)
            {
                if (!await SyncEffects(session))
                    throw new InvalidOperationException("강화 기록을 동기화하지 못했습니다. 서버 모드 버전과 연결을 확인하고 다시 불러와 주세요.");
                selectedPath = StatPatches.Registry.Find(itemId)?.Path ?? selectedPath;
                firstQuote = false;
            }
            if ((StatPatches.Registry.Find(itemId, selectedPath)?.Level ?? 0) >= maxLevel.Value)
            {
                preview = null;
                message = resultMessage ?? "이 항목은 최대 단계입니다. 다른 항목을 선택해 주세요.";
                return;
            }
            var response = await GameBridge.WithTimeout(GameBridge.Query<EnhanceQuote>(
                "/rf/gear-enhance/quote", new { ProtocolVersion = EnhanceProtocol.Version, ItemId = itemId, Path = selectedPath, Settings = Settings() }));
            if (current != generation) { message = "설정이 바뀌었습니다. 견적을 다시 불러와 주세요."; return; }
            quote = preview = response; weapon = response.IsWeapon; message = resultMessage ?? ""; destructionConfirmed = false;
        }
        catch (Exception ex) { message = ex.GetBaseException().Message; Logger.LogWarning(ex); }
        finally { busy = false; }
    }

    private void SelectPath(EnhancePath path)
    {
        if (busy || selectedPath == path) return;
        selectedPath = path; quote = preview = null; generation++; RefreshQuote();
    }

    private async void Execute()
    {
        if (busy || quote == null || uncertain) return;
        var attempt = quote;
        busy = true; quote = null; message = "강화 결과를 확인하는 중입니다…";
        var submitted = false;
        var completed = false;
        try
        {
            await GameBridge.Flush(session);
            QueueFeedback(FeedbackCue.Attempt);
            submitted = true;
            var ok = await GameBridge.WithTimeout(GameBridge.Execute(session, attempt.QuoteId, destructionConfirmed), 30000);
            if (!ok) { message = "강화가 거절됐습니다. 서버 오류 안내를 확인하고 견적을 다시 불러와 주세요."; return; }
            var receipt = await GameBridge.WithTimeout(GameBridge.Query<EnhanceReceipt>("/rf/gear-enhance/receipt", new { ProtocolVersion = EnhanceProtocol.Version, attempt.QuoteId }));
            message = receipt.Success
                ? $"{(attempt.BindLegacy ? "기존 단계 복원 완료" : "강화 성공")}! {EnhanceEffects.Name(receipt.Path)} +{receipt.Level} · {receipt.Paid:N0} ₽ 소모"
                : $"강화 실패 · {receipt.Paid:N0} ₽ 소모\n{FailureText(receipt.FailureMode)}";
            StatPatches.Registry.ApplyReceipt(receipt, GameBridge.ProfileId(session));
            completed = true;
            QueueFeedback(receipt.Success ? FeedbackCue.Success : FeedbackCue.Failure);
            RefreshGameplay();
            await SyncEffects(session);
            if (!receipt.Success && receipt.FailureMode == FailureMode.Destroy) { itemId = null; selectedItem = null; itemIcon = null; }
        }
        catch (Exception ex)
        {
            uncertain = submitted;
            message = ex.GetBaseException().Message + (submitted
                ? "\n결과 미확정: 재시도하지 말고 게임 종료 후 재접속하여 장비·루블을 확인해 주세요."
                : "\n아직 강화를 전송하지 않았습니다.");
            Logger.LogError(ex);
        }
        finally
        {
            busy = false; destructionConfirmed = false;
            if (completed && itemId != null && !uncertain)
            {
                var result = message;
                main.Enqueue(() => LoadQuote(result));
            }
        }
    }

    private void QueueFeedback(FeedbackCue cue)
    {
        if (!soundsEnabled.Value || feedbackUnavailable) return;
        main.Enqueue(() =>
        {
            try { GameFeedback.Play(cue); }
            catch (Exception ex)
            {
                feedbackUnavailable = true;
                Logger.LogWarning("강화 효과음 연결 실패(강화 결과에는 영향 없음): " + ex.GetBaseException().Message);
            }
        });
    }

    private static string FailureText(FailureMode mode) => mode == FailureMode.Destroy
        ? "본체·부착물·탄창·탄약이 함께 파괴됩니다."
        : mode == FailureMode.ZeroDurability ? "본체·부품의 현재 내구도 0 · 최대 내구도 유지"
        : "비용만 소모 · 장비와 기존 강화 단계 유지";

    private void Update()
    {
        for (var n = 0; n < 32 && main.TryDequeue(out var action); n++) action();
        if (visible && !busy && !GameBridge.InStash(uiContext)) Close();
    }
    private void CreateBlocker()
    {
        if (blocker != null) return;
        var raycaster = GameBridge.RuntimeType("UnityEngine.UI.GraphicRaycaster");
        var image = GameBridge.RuntimeType("UnityEngine.UI.Image");
        if (raycaster == null || image == null) throw new TypeLoadException("Unity UI를 찾을 수 없습니다.");
        blocker = new GameObject("GearEnhance.InputBlocker", typeof(RectTransform), typeof(Canvas), raycaster, image);
        DontDestroyOnLoad(blocker);
        var canvas = blocker.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = short.MaxValue;
        var rect = blocker.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        AccessTools.Property(image, "color").SetValue(blocker.GetComponent(image), Color.clear);
    }
    private void Close()
    {
        if (visible && repaintCount > 0)
            Logger.LogInfo($"강화 창 UI CPU: Repaint {repaintCount}회, 평균 {repaintTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / repaintCount:0.00}ms, 최대 {maximumRepaintTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.00}ms (GPU/FPS 측정 아님)");
        repaintCount = repaintTicks = maximumRepaintTicks = 0;
        visible = false; quote = preview = null; session = uiContext = selectedItem = itemIcon = null; generation++;
        itemId = null;
        if (blocker != null) Destroy(blocker);
        blocker = null;
    }
    private void OnDestroy()
    {
        syncGeneration++;
        Close(); harmony?.UnpatchSelf(); StatPatches.Registry.Clear(); ItemMarkers.Clear();
        if (windowSkin != null) Destroy(windowSkin);
        if (uiFont != null) Destroy(uiFont);
        foreach (var texture in skinTextures) if (texture != null) Destroy(texture);
        skinTextures.Clear();
        if (Instance == this) Instance = null;
    }
}
