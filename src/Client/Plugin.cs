using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace GearEnhance;

[BepInPlugin("rf.gearenhance", "Gear Enhance", "0.1.0")]
public sealed class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance;
    private Harmony harmony;
    private ConfigEntry<bool> menuEnabled, always;
    private ConfigEntry<float> chance, percent, growth;
    private ConfigEntry<int> maxLevel, minimum;
    private ConfigEntry<PriceBasis> price;
    private ConfigEntry<FailureMode> failure;
    private object session;
    private object uiContext;
    private string itemId, itemName;
    private EnhanceQuote quote;
    private bool visible, busy, destructionConfirmed, uncertain;
    private int generation;
    private string message = "";
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
        maxLevel = Config.Bind("2. 확률", "최대 강화 단계", 10, new ConfigDescription("현재는 단계만 기록하며 능력치 효과는 없습니다.", new AcceptableValueRange<int>(1, 100)));
        price = Config.Bind("3. 비용", "기준 가격", PriceBasis.Handbook, "Handbook = 북밸류, Flea = SPT 플리 기준가. 부착물 가격은 포함하지 않습니다.");
        percent = Config.Bind("3. 비용", "기준 가격 대비 비용 (%)", 10f, new ConfigDescription("0이면 최소 비용만 적용됩니다.", new AcceptableValueRange<float>(0, 1000)));
        growth = Config.Bind("3. 비용", "단계별 비용 배수", 1.25f, new ConfigDescription("비용 = 기준가 × 비용 비율 × 배수^현재단계. 최대 1억 루블.", new AcceptableValueRange<float>(1, 3)));
        minimum = Config.Bind("3. 비용", "최소 비용 (루블)", 1000, new ConfigDescription("창고 안의 잠기지 않은 루블을 사용합니다.", new AcceptableValueRange<int>(0, 100000000)));
        failure = Config.Bind("4. 실패", "실패 처리", FailureMode.KeepItem,
            "KeepItem: 비용만 소모 / ZeroDurability: 장비와 내구도 있는 부착물의 현재 내구도 0 / Destroy: 장비와 모든 부착물 삭제. 최대 내구도는 변경하지 않습니다.");
        Config.SettingChanged += (_, _) => main.Enqueue(() =>
        {
            generation++; quote = null; destructionConfirmed = false;
            if (visible && !busy) message = "설정이 바뀌었습니다. 견적을 다시 불러와 주세요.";
        });
        try
        {
            var target = GameBridge.RequiredMethod(AccessTools.TypeByName("EFT.UI.ItemUiContext"), "GetItemContextInteractions", 2);
            harmony = new Harmony("rf.gearenhance");
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(Plugin), nameof(MenuPostfix)));
            Logger.LogInfo("Gear Enhance 0.1.0: 단계 기록용 프로토타입, 능력치 효과 없음");
        }
        catch (Exception ex) { Logger.LogError(ex); }
    }

    private static void MenuPostfix(object __instance, object itemContext, object __result)
    {
        var plugin = Instance;
        if (plugin == null || !plugin.menuEnabled.Value || __result == null || !GameBridge.InStash(__instance)) return;
        try
        {
            var view = GameBridge.Get(itemContext, "ViewType")?.ToString();
            if (view != "Inventory" && view != "TradingPlayer") return;
            var item = GameBridge.Get(itemContext, "Item");
            if (!GameBridge.Eligible(item)) return;
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
            uiContext = context;
            itemId = GameBridge.Get(item, "Id")?.ToString() ?? throw new InvalidOperationException("아이템 ID가 없습니다.");
            // A readable template key is available even when a localization mod fails.
            itemName = GameBridge.Get(item, "ShortName")?.ToString() ?? itemId;
            var manager = AccessTools.TypeByName("EFT.LocalizationManager");
            var localizer = manager?.GetMethod("LocalizedValue", new[] { typeof(string) });
            var instance = manager == null ? null : AccessTools.Property(manager, "Instance")?.GetValue(null);
            if (instance != null && localizer != null) itemName = localizer.Invoke(instance, new object[] { itemName })?.ToString() ?? itemName;
            CreateBlocker();
            window = new Rect(Math.Max(0, (Screen.width - 540) / 2), Math.Max(0, (Screen.height - 500) / 2), 540, 500);
            visible = true; quote = null; destructionConfirmed = false; generation++;
            RefreshQuote();
        }
        catch (Exception ex) { Logger.LogError(ex); Close(); }
    }

    private EnhanceSettings Settings() => new EnhanceSettings
    {
        SuccessPercent = chance.Value, AlwaysSucceed = always.Value, MaxLevel = maxLevel.Value,
        PriceBasis = price.Value, CostPercent = percent.Value, CostPerLevel = growth.Value,
        MinimumCost = minimum.Value, FailureMode = failure.Value
    };

    private async void RefreshQuote()
    {
        if (busy || uncertain) return;
        busy = true; quote = null; message = "견적을 불러오는 중입니다…";
        var current = generation;
        try
        {
            await GameBridge.Flush(session);
            var response = await GameBridge.WithTimeout(GameBridge.Query<EnhanceQuote>(
                "/rf/gear-enhance/quote", new { ItemId = itemId, Settings = Settings() }));
            if (current != generation) { message = "설정이 바뀌었습니다. 견적을 다시 불러와 주세요."; return; }
            quote = response; message = ""; destructionConfirmed = false;
        }
        catch (Exception ex) { message = ex.GetBaseException().Message; Logger.LogWarning(ex); }
        finally { busy = false; }
    }

    private async void Execute()
    {
        if (busy || quote == null || uncertain) return;
        var attempt = quote;
        busy = true; quote = null; message = "강화 결과를 확인하는 중입니다…";
        var submitted = false;
        try
        {
            await GameBridge.Flush(session);
            submitted = true;
            var ok = await GameBridge.WithTimeout(GameBridge.Execute(session, attempt.QuoteId, destructionConfirmed), 30000);
            if (!ok) { message = "강화가 거절됐습니다. 서버 오류 안내를 확인하고 견적을 다시 불러와 주세요."; return; }
            var receipt = await GameBridge.WithTimeout(GameBridge.Query<EnhanceReceipt>(
                "/rf/gear-enhance/receipt", new { attempt.QuoteId }));
            message = receipt.Success
                ? $"강화 성공! +{receipt.Level} / {receipt.Paid:N0} ₽ 소모\n현재 능력치 효과는 없습니다."
                : $"강화 실패 / {receipt.Paid:N0} ₽ 소모\n{FailureText(receipt.FailureMode)}";
            if (!receipt.Success && receipt.FailureMode == FailureMode.Destroy) itemId = null;
        }
        catch (Exception ex)
        {
            uncertain = submitted;
            message = ex.GetBaseException().Message + (submitted
                ? "\n결과가 미확정입니다. 재시도하지 말고 게임을 종료한 뒤 재접속하여 장비·루블·강화 기록을 확인해 주세요."
                : "\n아직 강화를 전송하지 않았습니다.");
            Logger.LogError(ex);
        }
        finally { busy = false; destructionConfirmed = false; }
    }

    private static string FailureText(FailureMode mode) => mode == FailureMode.Destroy
        ? "장비와 모든 부착물(탄창·탄약 포함)이 파괴됩니다. 되돌릴 수 없습니다."
        : mode == FailureMode.ZeroDurability
            ? "장비와 내구도 있는 부착물의 현재 내구도가 0이 됩니다. 최대 내구도는 유지됩니다."
            : "비용만 소모하며 장비와 강화 단계는 유지됩니다.";

    private void Update()
    {
        for (var n = 0; n < 32 && main.TryDequeue(out var action); n++) action();
        if (visible && !busy && !GameBridge.InStash(uiContext)) Close();
    }

    private void OnGUI()
    {
        if (!visible) return;
        if (windowSkin == null)
        {
            windowSkin = Instantiate(GUI.skin);
            uiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "Arial Unicode MS" }, 16);
            if (uiFont != null) windowSkin.font = uiFont;
            windowSkin.toggle.wordWrap = true;
            windowSkin.button.fontSize = 16;
        }
        var oldSkin = GUI.skin;
        GUI.skin = windowSkin;
        var old = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.55f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = old;
        window = GUILayout.Window(GetInstanceID(), window, DrawWindow, "장비 강화 · 1차 프로토타입");
        GUI.skin = oldSkin;
    }

    private void DrawWindow(int id)
    {
        var body = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 16, richText = false };
        GUILayout.Space(10);
        GUILayout.Label(itemName ?? "장비", body);
        GUILayout.Label("현재 버전은 강화 단계만 기록합니다. 능력치 효과 없음.", body);
        GUILayout.Space(10);
        if (quote != null)
        {
            GUILayout.Label($"+{quote.CurrentLevel} → +{quote.NextLevel}    성공률 {quote.SuccessPercent:0.##}%", body);
            GUILayout.Label($"비용 {quote.Cost:N0} ₽    보유 {quote.AvailableRoubles:N0} ₽", body);
            GUILayout.Label($"가격 기준: {quote.PriceSource} (장비 본체만)", body);
            var left = Math.Max(0, quote.ExpiresUnixSeconds - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            GUILayout.Label($"견적 유효 시간: {left}초", body);
            GUILayout.Label("실패 시: " + FailureText(quote.FailureMode), body);
            if (quote.FailureMode == FailureMode.Destroy && quote.SuccessPercent < 100)
                destructionConfirmed = GUILayout.Toggle(destructionConfirmed,
                    $" 본체 + 부착물 {quote.AttachedItems}개가 사라질 수 있음을 확인했습니다.");
            GUI.enabled = !busy && left > 0 && quote.AvailableRoubles >= quote.Cost
                && (quote.FailureMode != FailureMode.Destroy || quote.SuccessPercent == 100 || destructionConfirmed);
            if (GUILayout.Button("비용을 지불하고 강화", GUILayout.Height(42))) Execute();
            GUI.enabled = true;
        }
        if (!string.IsNullOrEmpty(message)) GUILayout.Label(message, body);
        GUILayout.FlexibleSpace();
        GUI.enabled = !busy && !uncertain && itemId != null;
        if (GUILayout.Button("견적 다시 불러오기", GUILayout.Height(32))) RefreshQuote();
        GUI.enabled = !busy;
        if (GUILayout.Button("닫기", GUILayout.Height(32))) Close();
        GUI.enabled = true;
        GUI.DragWindow(new Rect(0, 0, window.width, 24));
    }

    private void CreateBlocker()
    {
        if (blocker != null) return;
        var raycaster = AccessTools.TypeByName("UnityEngine.UI.GraphicRaycaster");
        var image = AccessTools.TypeByName("UnityEngine.UI.Image");
        if (raycaster == null || image == null) throw new TypeLoadException("Unity UI를 찾을 수 없습니다.");
        blocker = new GameObject("GearEnhance.InputBlocker", typeof(RectTransform), typeof(Canvas), raycaster, image);
        DontDestroyOnLoad(blocker);
        var canvas = blocker.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        var rect = blocker.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        AccessTools.Property(image, "color").SetValue(blocker.GetComponent(image), Color.clear);
    }

    private void Close()
    {
        visible = false; quote = null; session = null; uiContext = null; generation++;
        if (blocker != null) Destroy(blocker);
        blocker = null;
    }

    private void OnDestroy()
    {
        Close(); harmony?.UnpatchSelf();
        if (windowSkin != null) Destroy(windowSkin);
        if (uiFont != null) Destroy(uiFont);
        if (Instance == this) Instance = null;
    }
}
