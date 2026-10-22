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
    private ConfigEntry<bool> menuEnabled, always, soundsEnabled, providedSounds, highRiskEnabled;
    private ConfigEntry<float> chance, chanceLoss, destructionChance, completionBonus, soundVolume, percent, growth;
    private ConfigEntry<float> destructionProtectionPrice, downgradeProtectionPrice;
    private ConfigEntry<float> evolutionChance, evolutionPrice;
    private ConfigEntry<bool> showDeveloperOptions;
    private readonly DeveloperOptions developerOptions = new DeveloperOptions();
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
    private bool protectDestruction, protectDowngrade;
    private bool evolutionMode;
    private int generation, syncGeneration;
    private string message = "";
    private long repaintCount, repaintTicks, maximumRepaintTicks;
    private bool feedbackUnavailable;
    private bool providedFeedbackUnavailable, attemptUsesAudioClock;
    private EnhancementAudio feedbackAudio;
    private double attemptDuration = 1;
    private double evolutionAttemptDuration = 1;
    private readonly AttemptTimeline attemptTimeline = new AttemptTimeline();
    private EnhanceQuote pendingAttempt;
    private bool pendingDestructionConfirmation;
    private int pendingGeneration;
    private static double Clock => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
    private double AttemptClock => attemptUsesAudioClock ? AudioSettings.dspTime : Clock;
    // dspTime stops while the audio device is paused or lost. Submit after the
    // clip length plus this grace on the monotonic clock so busy is never stuck.
    private const double AudioStallGrace = 3;
    private double attemptWallStart;
    private Rect window;
    private GameObject blocker;
    private GUISkin windowSkin;
    private Font uiFont;
    private readonly ConcurrentQueue<Action> main = new ConcurrentQueue<Action>();

    private void Awake()
    {
        Instance = this;
        showDeveloperOptions = Config.Bind("99. 개발자 옵션", "개발자 옵션 표시", false,
            "확률·비용·항상 성공·실패·능력치·보호·진화 밸런스 설정을 표시합니다. 바꾼 뒤 F12 창을 닫았다 다시 열면 적용됩니다. 기본은 숨김입니다.");
        developerOptions.SetVisible(showDeveloperOptions.Value);
        menuEnabled = Config.Bind("1. 기본", "강화 메뉴 사용", true, "창고의 무기·방어구 우클릭에 장비 강화를 표시합니다.");
        chance = BindDeveloper("2. 확률", "성공률 (%)", 75f, new ConfigDescription("+0에서 +1로 강화할 때 성공률. 이후 현재 단계 × 단계별 감소량만큼 줄어듭니다.", new AcceptableValueRange<float>(0, 100)));
        chanceLoss = BindDeveloper("2. 확률", "단계별 성공률 감소 (%p)", 5f, new ConfigDescription("기본: +1 시도 75%, +10 시도 30%. 0이면 같은 확률을 유지합니다.", new AcceptableValueRange<float>(0, 100)));
        always = BindDeveloper("2. 확률", "항상 성공 (100%)", false, "켜면 성공률 설정 대신 100%를 적용합니다. 비용은 소모됩니다.");
        maxLevel = BindDeveloper("2. 확률", "최대 강화 단계", 10, new ConfigDescription("각 항목 최대 단계. 효과 곡선·무기 고위험 시작(절반 올림)·1차 스타 진화 조건도 함께 바뀝니다.", new AcceptableValueRange<int>(1, 100)));
        price = BindDeveloper("3. 비용", "기준 가격", PriceBasis.Handbook, "Handbook = 북밸류, Flea = SPT 플리 기준가. 장비 본체만 계산합니다.");
        percent = BindDeveloper("3. 비용", "기준 가격 대비 비용 (%)", 10f, new ConfigDescription("0이면 최소 비용만 적용됩니다.", new AcceptableValueRange<float>(0, 1000)));
        growth = BindDeveloper("3. 비용", "단계별 비용 배수", 1.25f, new ConfigDescription("기준가 × 비용 비율 × 배수^현재단계. 최대 1억 루블.", new AcceptableValueRange<float>(1, 3)));
        minimum = BindDeveloper("3. 비용", "최소 비용 (루블)", 1000, new ConfigDescription("창고 안의 잠기지 않은 루블을 사용합니다.", new AcceptableValueRange<int>(0, 100000000)));
        failure = BindDeveloper("4. 실패", "실패 처리", FailureMode.KeepItem,
            "일반 구간 실패 처리. KeepItem: 비용만 / ZeroDurability: 현재 내구도 0 / Destroy: 본체·부착물·탄약 삭제 / Downgrade: 선택 항목 -1. 무기 고위험 구간에서는 아래 파괴·하락 규칙이 우선합니다.");
        highRiskEnabled = BindDeveloper("4. 실패", "무기 고위험 강화", true, "무기 3개 항목이 모두 최대 단계의 절반(올림) 이상이면 실패 시 파괴 또는 선택 항목 -1. 방어구는 일반 실패 처리를 따릅니다.");
        destructionChance = BindDeveloper("4. 실패", "고위험 실패 중 파괴 확률 (%)", 25f, new ConfigDescription("성공 판정과 별도입니다. 고위험 실패 중 이 확률로 본체·부착물·탄약 파괴, 나머지는 선택 항목 -1. 0은 하락만, 100은 파괴만.", new AcceptableValueRange<float>(0, 100)));
        BindEffect(EnhancePath.Recoil, "5. 무기 효과", 1.5f, 15);
        BindEffect(EnhancePath.Ergonomics, "5. 무기 효과", 1, 10);
        BindEffect(EnhancePath.Precision, "5. 무기 효과", 1, 10);
        BindEffect(EnhancePath.ArmorDurability, "6. 방어구 효과", 3, 30);
        BindEffect(EnhancePath.Mobility, "6. 방어구 효과", 2, 20);
        soundsEnabled = Config.Bind("7. 피드백", "강화 효과음", true, "강화 중·성공·실패·단계 하락·파괴에 효과음을 재생합니다. 끄더라도 게이지 시간은 유지합니다.");
        providedSounds = Config.Bind("7. 피드백", "전용 강화 음원 사용", true, "사용자 제공 음원6개 사용. 끄면 기존 게임 UI 소리를 사용합니다. 전용 음원 오류 시에도 기본음으로 대체합니다.");
        soundVolume = Config.Bind("7. 피드백", "전용 음원 음량 (%)", 70f, new ConfigDescription("게임의 마스터/인터페이스 음량에 추가로 곱합니다. 0이면 전용 음원 무음. 기본 게임음 모드에서는 게임 소리 설정을 사용합니다.", new AcceptableValueRange<float>(0, 100)));
        completionBonus = BindDeveloper("8. 완성 보너스", "전 항목 완성 추가 효과 (%)", 20f, new ConfigDescription("모든 항목 최대 달성 후 별도 스타 강화 진화에 성공한 장비에만 추가 배율. 예: 반동15%→18%, ★★ 보라색. 자동 적용이 아닙니다.", new AcceptableValueRange<float>(0, 100)));
        destructionProtectionPrice = BindDeveloper("9. 강화 보호", "파괴 방지 비용 계수 (%)", 50f, new ConfigDescription("추가 비용 = 기본 강화 비용 × 이 값/100 × 시도 단계². 해당 파괴 위험이 있을 때만 부과합니다.", new AcceptableValueRange<float>(0, 1000)));
        downgradeProtectionPrice = BindDeveloper("9. 강화 보호", "단계 하락 방지 비용 계수 (%)", 35f, new ConfigDescription("추가 비용 = 기본 강화 비용 × 이 값/100 × 시도 단계². 하락 위험이 있을 때만 부과합니다. 보호비도 성공/실패와 관계없이 소모됩니다.", new AcceptableValueRange<float>(0, 1000)));
        evolutionChance = BindDeveloper("10. 스타 강화 진화", "진화 성공률 (%)", 50f, new ConfigDescription("1차 스타 전 항목 최대 달성 후 별도 진화 창에서 사용합니다. 실패 시 파괴, 보호 불가.", new AcceptableValueRange<float>(0, 100)));
        evolutionPrice = BindDeveloper("10. 스타 강화 진화", "진화 기준가 대비 비용 (%)", 300f, new ConfigDescription("진화 비용 = 본체 기준가 × 이 값/100. 기본3배, 1회 최대1억 루블.", new AcceptableValueRange<float>(0, 10000)));
        PrepareFeedback();
        StatPatches.Registry.Configure(Tuning());
        Config.SettingChanged += (_, args) => main.Enqueue(() =>
        {
            if (ReferenceEquals(args.ChangedSetting, showDeveloperOptions))
            {
                developerOptions.SetVisible(showDeveloperOptions.Value);
                return;
            }
            if (ReferenceEquals(args.ChangedSetting, soundsEnabled) || ReferenceEquals(args.ChangedSetting, providedSounds)
                || ReferenceEquals(args.ChangedSetting, soundVolume))
            {
                feedbackAudio?.Stop(); feedbackUnavailable = providedFeedbackUnavailable = false;
                if (providedSounds.Value && feedbackAudio == null) PrepareFeedback();
                return;
            }
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

    private ConfigEntry<T> BindDeveloper<T>(string section, string key, T defaultValue, string description) =>
        BindDeveloper(section, key, defaultValue, new ConfigDescription(description));
    private ConfigEntry<T> BindDeveloper<T>(string section, string key, T defaultValue, ConfigDescription description) =>
        Config.Bind(section, key, defaultValue, new ConfigDescription(description.Description, description.AcceptableValues,
            developerOptions.Describe(section, key)));

    private void BindEffect(EnhancePath path, string section, float step, float cap)
    {
        var name = EnhanceEffects.StatName(path);
        var unit = path == EnhancePath.Ergonomics ? "" : " (%)";
        effectConfig[path] = (
            BindDeveloper(section, name + " / 단계" + unit, step, new ConfigDescription("최대 효과 기준 = 이 값 × 최대 단계와 효과 상한 중 작은 값. 실제 단계별 증가량은 후반 1→4배 곡선으로 배분됩니다. 기존 장비에도 적용됩니다.", new AcceptableValueRange<float>(0, 10))),
            BindDeveloper(section, name + " 상한" + unit, cap, new ConfigDescription("진화 보너스 적용 전 상한. 보너스 후 안전 한계는 감소율 80%, 인체공학 추가 50입니다.", new AcceptableValueRange<float>(0, path == EnhancePath.Ergonomics ? 50 : 80))));
    }
    private EffectTuning Tuning() => new EffectTuning
    {
        MaxLevel = maxLevel.Value, CompletionBonusPercent = completionBonus.Value,
        RecoilPerLevel = effectConfig[EnhancePath.Recoil].Step.Value, RecoilCap = effectConfig[EnhancePath.Recoil].Cap.Value,
        ErgonomicsPerLevel = effectConfig[EnhancePath.Ergonomics].Step.Value, ErgonomicsCap = effectConfig[EnhancePath.Ergonomics].Cap.Value,
        PrecisionPerLevel = effectConfig[EnhancePath.Precision].Step.Value, PrecisionCap = effectConfig[EnhancePath.Precision].Cap.Value,
        ArmorDurabilityPerLevel = effectConfig[EnhancePath.ArmorDurability].Step.Value, ArmorDurabilityCap = effectConfig[EnhancePath.ArmorDurability].Cap.Value,
        MobilityPerLevel = effectConfig[EnhancePath.Mobility].Step.Value, MobilityCap = effectConfig[EnhancePath.Mobility].Cap.Value
    };
    private EnhanceSettings Settings() => new EnhanceSettings
    {
        SuccessPercent = chance.Value, AlwaysSucceed = always.Value, MaxLevel = maxLevel.Value,
        SuccessLossPerLevel = chanceLoss.Value, HighRiskEnabled = highRiskEnabled.Value,
        HighRiskDestructionPercent = destructionChance.Value,
        DestructionProtectionPercent = destructionProtectionPrice.Value, DowngradeProtectionPercent = downgradeProtectionPrice.Value,
        EvolutionSuccessPercent = evolutionChance.Value, EvolutionCostPercent = evolutionPrice.Value,
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
        if (busy || uncertain || resultVisible) return;
        try
        {
            session = GameBridge.Get(context, "ClientSession") ?? throw new InvalidOperationException("PMC 세션이 없습니다.");
            uiContext = context; selectedItem = item;
            itemId = GameBridge.Get(item, "Id")?.ToString() ?? throw new InvalidOperationException("아이템 ID가 없습니다.");
            weapon = GameBridge.IsWeapon(item);
            evolutionMode = false;
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
            protectDestruction = protectDowngrade = false;
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
            if (evolutionMode && StatPatches.Registry.IsEvolved(itemId))
            {
                preview = null;
                message = resultMessage ?? "★★ 스타 강화 진화를 완료했습니다.";
                return;
            }
            if (!evolutionMode && (StatPatches.Registry.Find(itemId, selectedPath)?.Level ?? 0) >= maxLevel.Value)
            {
                preview = null;
                message = resultMessage ?? "이 항목은 최대 단계입니다. 다른 항목을 선택해 주세요.";
                return;
            }
            var response = await GameBridge.WithTimeout(GameBridge.Query<EnhanceQuote>(
                "/rf/gear-enhance/quote", new { ProtocolVersion = EnhanceProtocol.Version, ItemId = itemId, Path = selectedPath, Settings = Settings(),
                    Protection = new EnhanceProtection { Destruction = protectDestruction, Downgrade = protectDowngrade },
                    Action = evolutionMode ? EnhanceAction.Evolve : EnhanceAction.Upgrade }));
            if (current != generation) { message = "설정이 바뀌었습니다. 견적을 다시 불러와 주세요."; return; }
            quote = preview = response; weapon = response.IsWeapon; message = resultMessage ?? ""; destructionConfirmed = false;
            protectDestruction = response.Protection.Destruction; protectDowngrade = response.Protection.Downgrade;
        }
        catch (Exception ex) { message = ex.GetBaseException().Message; Logger.LogWarning(ex); }
        finally { busy = false; }
    }

    private void SelectPath(EnhancePath path)
    {
        if (busy || resultVisible || evolutionMode || selectedPath == path) return;
        selectedPath = path; quote = preview = null; generation++; RefreshQuote();
    }

    private void Execute()
    {
        if (busy || resultVisible || quote == null || uncertain) return;
        if (attemptTimeline.Active) return;
        var started = Clock;
        attemptUsesAudioClock = false;
        if (soundsEnabled.Value && providedSounds.Value && feedbackAudio != null && !providedFeedbackUnavailable)
        {
            try { started = feedbackAudio.StartAttempt(soundVolume.Value / 100, evolutionMode); attemptUsesAudioClock = true; }
            catch (Exception ex) { ProvidedAudioFailed(ex); }
        }
        if (!attemptUsesAudioClock && soundsEnabled.Value) PlayFeedback(evolutionMode ? FeedbackCue.EvolutionAttempt : FeedbackCue.Attempt, false);
        if (!attemptTimeline.Begin(started, evolutionMode ? evolutionAttemptDuration : attemptDuration)) return;
        attemptWallStart = Clock;
        pendingAttempt = quote; pendingDestructionConfirmation = destructionConfirmed; pendingGeneration = generation;
        busy = true; quote = null; message = "강화 진행 중…";
    }

    private void SetProtection(bool destruction, bool enabled)
    {
        if (busy || resultVisible || evolutionMode || uncertain) return;
        if (destruction) protectDestruction = enabled; else protectDowngrade = enabled;
        generation++; quote = null; destructionConfirmed = false;
        RefreshQuote();
    }

    private async void SubmitAttempt(EnhanceQuote attempt, bool confirmed, int expectedGeneration)
    {
        var submitted = false;
        var completed = false;
        try
        {
            if (expectedGeneration != generation || !visible || !GameBridge.InStash(uiContext))
                throw new InvalidOperationException("강화 준비 중 설정이나 화면이 바뀌었습니다. 견적을 다시 불러와 주세요.");
            await GameBridge.Flush(session);
            if (expectedGeneration != generation || !visible || !GameBridge.InStash(uiContext))
                throw new InvalidOperationException("강화 준비가 취소됐습니다. 견적을 다시 불러와 주세요.");
            message = "강화 결과를 확인하는 중입니다…";
            submitted = true;
            var ok = await GameBridge.WithTimeout(GameBridge.Execute(session, attempt.QuoteId, confirmed), 30000);
            if (!ok) { message = "강화가 거절됐습니다. 서버 오류 안내를 확인하고 견적을 다시 불러와 주세요."; return; }
            var receipt = await GameBridge.WithTimeout(GameBridge.Query<EnhanceReceipt>("/rf/gear-enhance/receipt", new { ProtocolVersion = EnhanceProtocol.Version, attempt.QuoteId }));
            message = receipt.Success
                ? $"{(attempt.BindLegacy ? "기존 단계 복원 완료" : "강화 성공")}! {EnhanceEffects.Name(receipt.Path)} +{receipt.Level} · {receipt.Paid:N0} ₽ 소모"
                : receipt.FailureMode == FailureMode.Downgrade
                    ? $"강화 실패 · {EnhanceEffects.Name(receipt.Path)} +{receipt.Level}로 하락 · {receipt.Paid:N0} ₽ 소모"
                    : $"강화 실패 · {receipt.Paid:N0} ₽ 소모\n{FailureText(receipt.FailureMode)}";
            if (receipt.Success && receipt.IsEvolution) message = "스타 강화 진화 성공! ★★ 보라색 표식 · 진화 보너스 적용";
            else if (receipt.Success && attempt.CompletesOnSuccess) message += " · 1차 스타 전 항목 완성! 진화 창을 열 수 있습니다.";
            StatPatches.Registry.ApplyReceipt(receipt, GameBridge.ProfileId(session));
            completed = true;
            ShowResultFeedback(receipt);
            QueueFeedback(GameFeedback.Result(receipt, receipt.IsEvolution && receipt.Success));
            RefreshGameplay();
            await SyncEffects(session);
            if (!receipt.Success && receipt.FailureMode == FailureMode.Destroy) { itemId = null; selectedItem = null; itemIcon = null; preview = null; }
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
            attemptTimeline.Reset(); pendingAttempt = null;
            if (completed && itemId != null && !uncertain)
            {
                var result = message;
                main.Enqueue(() => LoadQuote(result));
            }
        }
    }

    private void PrepareFeedback()
    {
        try
        {
            // Read the actual frame count even when audio is muted or native
            // mode is selected, so muting cannot shorten a paid attempt.
            var charge = EmbeddedSounds.Load(0);
            attemptDuration = charge.Duration;
            evolutionAttemptDuration = EmbeddedSounds.Load(5).Duration;
            if (providedSounds.Value && feedbackAudio == null) feedbackAudio = new EnhancementAudio(gameObject, charge);
        }
        catch (Exception ex) { ProvidedAudioFailed(ex); }
    }
    private void ProvidedAudioFailed(Exception ex)
    {
        feedbackAudio?.Stop();
        if (!providedFeedbackUnavailable)
            Logger.LogWarning("전용 강화 음원 재생 불가, 게임 기본음으로 대체: " + ex.GetBaseException().Message);
        providedFeedbackUnavailable = true;
    }
    private void QueueFeedback(FeedbackCue cue)
    {
        if (soundsEnabled.Value) main.Enqueue(() => PlayFeedback(cue));
    }
    private void PlayFeedback(FeedbackCue cue, bool allowProvided = true)
    {
        if (!soundsEnabled.Value) return;
        if (allowProvided && providedSounds.Value && feedbackAudio != null && !providedFeedbackUnavailable)
        {
            try { feedbackAudio.Play(cue, soundVolume.Value / 100); return; }
            catch (Exception ex) { ProvidedAudioFailed(ex); }
        }
        if (feedbackUnavailable) return;
        try { GameFeedback.Play(cue); }
        catch (Exception ex)
        {
            feedbackUnavailable = true;
            Logger.LogWarning("강화 효과음 연결 실패(강화 결과에는 영향 없음): " + ex.GetBaseException().Message);
        }
    }

    private static string FailureText(FailureMode mode) => mode == FailureMode.Destroy
        ? "본체·부착물·탄창·탄약이 함께 파괴됩니다."
        : mode == FailureMode.ZeroDurability ? "본체·부품의 현재 내구도 0 · 최대 내구도 유지"
        : mode == FailureMode.Downgrade ? "선택 항목의 강화 단계 -1 · 다른 항목과 장비 유지"
        : "비용만 소모 · 장비와 기존 강화 단계 유지";

    private void Update()
    {
        for (var n = 0; n < 32 && main.TryDequeue(out var action); n++) action();
        if (pendingAttempt != null && (attemptTimeline.TryComplete(AttemptClock) || AudioClockStalled() && attemptTimeline.ForceComplete()))
            SubmitAttempt(pendingAttempt, pendingDestructionConfirmation, pendingGeneration);
        if (visible && !busy && !GameBridge.InStash(uiContext)) Close();
    }
    private bool AudioClockStalled()
    {
        if (!attemptUsesAudioClock || Clock - attemptWallStart <= attemptTimeline.Duration + AudioStallGrace) return false;
        attemptUsesAudioClock = false;
        feedbackAudio?.Stop();
        Logger.LogWarning("게임 오디오 시계가 멈춰 강화 게이지를 실제 시간 기준으로 마쳤습니다.");
        return true;
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
        attemptTimeline.Reset(); pendingAttempt = null;
        feedbackAudio?.Stop();
        resultSprite = null; resultStarted = -100;
        resultVisible = false; resultReceipt = null;
        evolutionMode = false;
        if (blocker != null) Destroy(blocker);
        blocker = null;
    }
    private void OnDestroy()
    {
        syncGeneration++;
        Close(); harmony?.UnpatchSelf(); StatPatches.Registry.Clear(); ItemMarkers.Clear();
        feedbackAudio?.Dispose(); feedbackAudio = null;
        if (windowSkin != null) Destroy(windowSkin);
        if (uiFont != null) Destroy(uiFont);
        foreach (var texture in skinTextures) if (texture != null) Destroy(texture);
        skinTextures.Clear();
        if (Instance == this) Instance = null;
    }
}
