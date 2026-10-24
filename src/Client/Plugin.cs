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
    private bool patched;
    private ConfigEntry<bool> menuEnabled, soundsEnabled, providedSounds, detailedLogging;
    private ConfigEntry<float> soundVolume;
    private ConfigEntry<string> developerPassword;
    // Developer balance settings: saved values apply only while the password unlocks them.
    private bool developerUnlocked;
    private DevSetting<bool> always, highRiskEnabled;
    private DevSetting<float> chance, chanceLoss, destructionChance, completionBonus, percent, growth;
    private DevSetting<float> destructionProtectionPrice, downgradeProtectionPrice;
    private DevSetting<float> evolutionChance, evolutionPrice, thirdStarChance, thirdStarBonus;
    private DevSetting<float> basicReturn, advancedReturn, premiumReturn, basicFee, advancedFee, premiumFee;
    private ConfigEntry<UiLanguage> language;
    private readonly DeveloperOptions developerOptions = new DeveloperOptions();
    private DevSetting<int> maxLevel, minimum;
    private DevSetting<PriceBasis> price;
    private DevSetting<FailureMode> failure;
    private readonly Dictionary<EnhancePath, (DevSetting<float> Step, DevSetting<float> Cap)> effectConfig
        = new Dictionary<EnhancePath, (DevSetting<float>, DevSetting<float>)>();
    private object session, uiContext, selectedItem, itemIcon;
    private string itemId, itemName;
    private EnhancePath selectedPath;
    private EnhanceQuote quote, preview;
    private bool visible, busy, destructionConfirmed, uncertain, weapon, firstQuote;
    private bool protectDestruction, protectDowngrade, chanceBoost;
    private bool evolutionMode;
    private int evolutionTargetStars = 2;
    private int generation, syncGeneration;
    private string message = "";
    private long repaintCount, repaintTicks, maximumRepaintTicks;
    private bool feedbackUnavailable, uiFeedbackUnavailable;
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
    // Network round trip after the gauge, on top of the gauge itself.
    private const double QuoteSubmitMargin = 5;
    private static string UncertainNotice => Loc.L("결과 미확정: 재시도하지 말고 게임 종료 후 재접속하여 장비·루블을 확인해 주세요.",
        "Result unknown: do not retry. Close the game, reconnect and check your gear and roubles.");
    private double attemptWallStart;
    private static volatile bool syncWanted;
    private bool backgroundSyncing;
    private double nextBackgroundSync;
    internal static void RequestSync() => syncWanted = true;
    private Rect window;
    private GameObject blocker;
    private GUISkin windowSkin;
    private Font uiFont;
    private readonly ConcurrentQueue<Action> main = new ConcurrentQueue<Action>();

    private void Awake()
    {
        Instance = this;
        // Bound first so every later F12 entry and window text starts in the chosen language.
        language = Config.Bind("0. Language · 언어", "Language · 언어", UiLanguage.Auto, new ConfigDescription(
            "Auto: Korean Windows → 한국어, otherwise English. Close and reopen F12 to refresh its text.\n"
            + "Auto: 한국어 윈도우면 한국어, 그 외는 영어. F12 글자는 창을 닫았다 다시 열면 바뀝니다."));
        ApplyLanguage();
        developerPassword = BindGeneral(DeveloperOptions.DeveloperSection, "개발자 옵션 비밀번호", "",
            "비밀번호를 넣으면 확률·비용·항상 성공·실패·능력치·보호·진화·보험 밸런스 설정이 표시되고 적용됩니다. 잠겨 있으면 기본 밸런스로 강화합니다(바꿔 둔 값은 지우지 않음). 입력 후 F12 창을 닫았다 다시 열면 표시됩니다.");
        developerUnlocked = DeveloperLock.Matches(developerPassword.Value);
        ((ConfigurationManagerAttributes)developerPassword.Description.Tags[0]).CustomDrawer = DrawPassword;
        developerOptions.SetVisible(developerUnlocked);
        menuEnabled = BindGeneral("1. 기본", "강화 메뉴 사용", true, "창고의 무기·방어구 우클릭에 장비 강화를 표시합니다. 방탄판·내피는 방어구와 함께 강화되므로 표시하지 않습니다.");
        chance = BindDeveloper("2. 확률", "성공률 (%)", 75f, new ConfigDescription("+0에서 +1로 강화할 때 성공률. 이후 현재 단계 × 단계별 감소량만큼 줄어듭니다.", new AcceptableValueRange<float>(0, 100)));
        chanceLoss = BindDeveloper("2. 확률", "단계별 성공률 감소 (%p)", 5f, new ConfigDescription("기본: +1 시도 75%, +10 시도 30%. 0이면 같은 확률을 유지합니다.", new AcceptableValueRange<float>(0, 100)));
        always = BindDeveloper("2. 확률", "항상 성공 (100%)", false, "켜면 성공률 설정 대신 100%를 적용합니다. 비용은 소모됩니다.");
        maxLevel = BindDeveloper("2. 확률", "최대 강화 단계", 10, new ConfigDescription("각 항목 최대 단계. 효과 곡선·1차 스타 진화 조건도 함께 바뀝니다. 무기 고위험 시작은 선택 항목 +6 시도(+5→+6)로 고정됩니다.", new AcceptableValueRange<int>(1, 100)));
        price = BindDeveloper("3. 비용", "기준 가격", PriceBasis.Handbook, "Handbook = 북밸류, Flea = SPT 플리 기준가. 무기는 본체만, 방어구는 슬롯에 끼운 방탄판·삽입물·바이저 가격을 더합니다.");
        percent = BindDeveloper("3. 비용", "기준 가격 대비 비용 (%)", 10f, new ConfigDescription("0이면 최소 비용만 적용됩니다.", new AcceptableValueRange<float>(0, 1000)));
        growth = BindDeveloper("3. 비용", "단계별 비용 배수", 1.25f, new ConfigDescription("기준가 × 비용 비율 × 배수^현재단계. 최대 1억 루블.", new AcceptableValueRange<float>(1, 3)));
        minimum = BindDeveloper("3. 비용", "최소 비용 (루블)", 1000, new ConfigDescription("창고 안의 잠기지 않은 루블을 사용합니다.", new AcceptableValueRange<int>(0, 100000000)));
        failure = BindDeveloper("4. 실패", "실패 처리", FailureMode.KeepItem,
            "일반 구간 실패 처리. KeepItem: 비용만 / ZeroDurability: 현재 내구도 0 / Destroy: 본체·부착물·탄약 삭제 / Downgrade: 선택 항목 -1. 무기 고위험 구간에서는 아래 파괴·하락 규칙이 우선합니다.");
        highRiskEnabled = BindDeveloper("4. 실패", "무기 고위험 강화", true, "선택한 무기 항목의 다음 단계가 +6 이상이면(+5→+6부터) 실패 시 파괴 또는 해당 항목 -1. 다른 항목 단계는 무관합니다. 방어구는 일반 실패 처리를 따릅니다.");
        destructionChance = BindDeveloper("4. 실패", "고위험 실패 중 파괴 확률 (%)", 25f, new ConfigDescription("성공 판정과 별도입니다. 고위험 실패 중 이 확률로 본체·부착물·탄약 파괴, 나머지는 선택 항목 -1. 0은 하락만, 100은 파괴만.", new AcceptableValueRange<float>(0, 100)));
        BindEffect(EnhancePath.Recoil, "5. 무기 효과", 1.5f, 15);
        BindEffect(EnhancePath.Ergonomics, "5. 무기 효과", 1, 10);
        BindEffect(EnhancePath.Precision, "5. 무기 효과", 1, 10);
        BindEffect(EnhancePath.ArmorDurability, "6. 방어구 효과", 3, 30);
        BindEffect(EnhancePath.Mobility, "6. 방어구 효과", 2, 20);
        detailedLogging = BindGeneral("7. 피드백", "자세한 로그 남기기", false, "체크한 동안 효과음 설정·재생·대체 경로와 강화 창 UI CPU 진단을 LogOutput.log에 남깁니다. 실제 오류·경고는 체크와 관계없이 기록합니다.");
        soundsEnabled = BindGeneral("7. 피드백", "강화 효과음", true, "강화 중·결과·버튼·항목·체크박스·창 여닫기에 효과음을 재생합니다. 끄더라도 게이지 시간은 유지합니다.");
        providedSounds = BindGeneral("7. 피드백", "전용 강화 음원 사용", true, "사용자 제공 음원6개 사용. 끄면 기존 게임 UI 소리를 사용합니다. 전용 음원 오류 시에도 기본음으로 대체합니다.");
        soundVolume = BindGeneral("7. 피드백", "전용 음원 음량 (%)", 70f, new ConfigDescription("게임의 마스터/인터페이스 음량에 추가로 곱합니다. 0이면 전용 음원 무음. 기본 게임음 모드에서는 게임 소리 설정을 사용합니다.", new AcceptableValueRange<float>(0, 100)));
        completionBonus = BindDeveloper("8. 완성 보너스", "전 항목 완성 추가 효과 (%)", (float)StarEvolution.SecondDefaultBonus, new ConfigDescription("2차 스타 성공 시 기본 강화 효과의2배(+100%). 반동15%→30%, 인체공학+10→+20. 감소율80%/추가인체공학50 상한. 일반 최대 단계만으로 자동 적용되지 않습니다.", new AcceptableValueRange<float>(0, 300)));
        destructionProtectionPrice = BindDeveloper("9. 강화 보호", "파괴 방지 비용 계수 (%)", 50f, new ConfigDescription("추가 비용 = 기본 강화 비용 × 이 값/100 × 시도 단계². 해당 파괴 위험이 있을 때만 부과합니다.", new AcceptableValueRange<float>(0, 1000)));
        downgradeProtectionPrice = BindDeveloper("9. 강화 보호", "단계 하락 방지 비용 계수 (%)", 35f, new ConfigDescription("추가 비용 = 기본 강화 비용 × 이 값/100 × 시도 단계². 하락 위험이 있을 때만 부과합니다. 보호비도 성공/실패와 관계없이 소모됩니다.", new AcceptableValueRange<float>(0, 1000)));
        evolutionChance = BindDeveloper("10. 스타 강화 진화", "진화 성공률 (%)", 30f, new ConfigDescription("1차 스타 전 항목 최대 달성 후 별도 진화 창에서 사용합니다. 실패 시 파괴, 보호 불가.", new AcceptableValueRange<float>(0, 100)));
        evolutionPrice = BindDeveloper("10. 스타 강화 진화", "진화 기준가 대비 비용 (%)", 300f, new ConfigDescription("진화 비용 = 최소 200만 루블과 본체 기준가 × 이 값/100 중 큰 값. 기본3배, 1회 최대1억 루블.", new AcceptableValueRange<float>(0, 10000)));
        thirdStarChance = BindDeveloper("11. 3차 스타 진화", "3차 스타 성공률 (%)", 10f, new ConfigDescription("2차 스타 성공 및 전 항목 최대인 장비만 시도. 실패 시 보호 없이 파괴합니다.", new AcceptableValueRange<float>(0, 100)));
        thirdStarBonus = BindDeveloper("11. 3차 스타 진화", "3차 스타 총 추가 효과 (%)", (float)StarEvolution.ThirdDefaultBonus, new ConfigDescription("3차 스타 성공 시 기본 강화 효과의3배(+200%). 2차 배율에 중첩하지 않습니다. 반동15%→45%, 인체공학+10→+30. 감소율80%/추가인체공학50 상한, 빨간색 ★★★.", new AcceptableValueRange<float>(0, 300)));
        basicReturn = BindDeveloper("12. 강화 보험", "일반 보험 반환 확률 (%)", 20f, new ConfigDescription("레이드에서 잃은 강화 장비가 강화된 상태로 우편 반환될 확률. 가입할 때의 값으로 고정됩니다.", new AcceptableValueRange<float>(0, 100)));
        advancedReturn = BindDeveloper("12. 강화 보험", "고급 보험 반환 확률 (%)", 55f, new ConfigDescription("레이드에서 잃은 강화 장비가 강화된 상태로 우편 반환될 확률. 가입할 때의 값으로 고정됩니다.", new AcceptableValueRange<float>(0, 100)));
        premiumReturn = BindDeveloper("12. 강화 보험", "프리미엄 보험 반환 확률 (%)", 90f, new ConfigDescription("레이드에서 잃은 강화 장비가 강화된 상태로 우편 반환될 확률. 가입할 때의 값으로 고정됩니다.", new AcceptableValueRange<float>(0, 100)));
        basicFee = BindDeveloper("12. 강화 보험", "일반 보험료 (강화 가치 대비 %)", 5f, new ConfigDescription("강화 가치 = 지금 단계·스타까지 강화하는 데 드는 기본 비용 합계(방어구는 등록한 방탄판 가격 포함). 최소 비용 이상.", new AcceptableValueRange<float>(0, 1000)));
        advancedFee = BindDeveloper("12. 강화 보험", "고급 보험료 (강화 가치 대비 %)", 15f, new ConfigDescription("강화 가치 = 지금 단계·스타까지 강화하는 데 드는 기본 비용 합계(방어구는 등록한 방탄판 가격 포함). 최소 비용 이상.", new AcceptableValueRange<float>(0, 1000)));
        premiumFee = BindDeveloper("12. 강화 보험", "프리미엄 보험료 (강화 가치 대비 %)", 30f, new ConfigDescription("강화 가치 = 지금 단계·스타까지 강화하는 데 드는 기본 비용 합계(방어구는 등록한 방탄판 가격 포함). 최소 비용 이상.", new AcceptableValueRange<float>(0, 1000)));
        BindTranscend();
        // Migrate the previous shipped default once; preserve custom developer odds.
        var balanceVersion = Config.Bind("0. 내부", "밸런스 설정 버전", 0,
            new ConfigDescription("내부 설정 이전용", null, new ConfigurationManagerAttributes { Browsable = false }));
        if (balanceVersion.Value < 1)
        {
            if (evolutionChance.Entry.Value == 50f) evolutionChance.Entry.Value = 30f;
            balanceVersion.Value = 1;
        }
        if (balanceVersion.Value < 2)
        {
            completionBonus.Entry.Value = (float)StarEvolution.UpgradeLegacyBonus(2, completionBonus.Entry.Value);
            thirdStarBonus.Entry.Value = (float)StarEvolution.UpgradeLegacyBonus(3, thirdStarBonus.Entry.Value);
            balanceVersion.Value = 2;
        }
        PrepareFeedback();
        LogDetailed($"강화 효과음 설정: 사용={soundsEnabled.Value}, 전용={providedSounds.Value}, 음량={soundVolume.Value:0.##}%, 준비={feedbackAudio != null}, 충전={attemptDuration:0.000}초");
        StatPatches.Registry.Configure(Tuning());
        ConfigureTranscend();
        Config.SettingChanged += (_, args) => main.Enqueue(() =>
        {
            if (ReferenceEquals(args.ChangedSetting, detailedLogging))
            {
                LogDetailed($"강화 상세 로그 ON: 효과음={soundsEnabled.Value}, 전용={providedSounds.Value}, 음량={soundVolume.Value:0.##}%");
                return;
            }
            if (ReferenceEquals(args.ChangedSetting, language))
            {
                ApplyLanguage();
                ItemMarkers.RefreshAll();
                return;
            }
            if (ReferenceEquals(args.ChangedSetting, developerPassword))
            {
                var unlocked = DeveloperLock.Matches(developerPassword.Value);
                if (unlocked == developerUnlocked) return;
                developerUnlocked = unlocked;
                developerOptions.SetVisible(unlocked);
                Logger.LogInfo(unlocked ? "Gear Enhance: 개발자 옵션 열림" : "Gear Enhance: 개발자 옵션 잠김 (기본 밸런스 적용)");
                // Effective balance values changed: fall through to refresh quotes and effects.
            }
            if (ReferenceEquals(args.ChangedSetting, soundsEnabled) || ReferenceEquals(args.ChangedSetting, providedSounds)
                || ReferenceEquals(args.ChangedSetting, soundVolume))
            {
                feedbackAudio?.Stop(); feedbackUnavailable = providedFeedbackUnavailable = uiFeedbackUnavailable = false;
                if (providedSounds.Value && feedbackAudio == null) PrepareFeedback();
                return;
            }
            generation++; quote = preview = null; destructionConfirmed = false;
            try { StatPatches.Registry.Configure(Tuning()); ConfigureTranscend(); RefreshGameplay(); }
            catch (Exception ex) { Logger.LogWarning(ex); }
            if (visible && !busy) message = SettingsChanged;
        });
        try
        {
            harmony = new Harmony("rf.gearenhance");
            // Resolve hot status types during plugin startup, before a window is opened.
            GameBridge.RuntimeType("EFT.InGameStatus");
            GameBridge.RuntimeType("EFT.InventoryLogic.BackEndInventoryController");
            StatPatches.Install(harmony);
            // Each transcendence effect stands alone: a missing hook disables only that effect.
            foreach (var failed in TranscendPatches.Install(harmony))
                Logger.LogError("Gear Enhance: 초월 효과 연결 실패(나머지 기능은 정상) / transcendence hook failed: " + failed);
            ItemMarkers.Install(harmony);
            var target = GameBridge.RequiredMethod(GameBridge.RuntimeType("EFT.UI.ItemUiContext"), "GetItemContextInteractions", 2);
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(Plugin), nameof(MenuPostfix)));
            patched = true;
            Logger.LogInfo("Gear Enhance " + EnhanceProtocol.ModVersion + ": 독립 강화·장비 표식·효과음 로드 · 개발자 옵션 "
                + (developerUnlocked ? "열림" : "잠김(기본 밸런스)"));
        }
        catch (Exception ex)
        {
            harmony?.UnpatchSelf();
            Logger.LogError("SPT API 연결 실패로 Gear Enhance 패치를 취소했습니다: " + ex);
        }
    }

    // After every plugin's Awake, so mods loaded after this one can be found.
    private void Start()
    {
        if (!patched) return;
        var error = TranscendPatches.InstallRealism(harmony, out var installed);
        if (error != null) Logger.LogError("Gear Enhance: Realism 탄도 둔탁상 연결 실패(초월 둔탁상 감소가 Realism 탄도에서는 빠짐) / Realism blunt hook failed: " + error);
        else if (installed) Logger.LogInfo("Gear Enhance: Realism 탄도 둔탁상에 초월 효과 연결 / transcendence blunt reduction linked to Realism ballistics");
    }

    private static string SettingsChanged => Loc.L("설정이 바뀌었습니다. 견적을 다시 불러와 주세요.", "Settings changed. Reload the quote.");

    private void ApplyLanguage()
    {
        Loc.English = language.Value == UiLanguage.English
            || language.Value == UiLanguage.Auto && Application.systemLanguage != SystemLanguage.Korean;
        developerOptions.SetLanguage(Loc.English);
    }

    private void LogDetailed(string text)
    {
        if (detailedLogging.Value) Logger.LogInfo(text);
    }

    internal static void LogError(string text) => Instance?.Logger.LogError(text);

    private ConfigEntry<T> BindGeneral<T>(string section, string key, T defaultValue, string description) =>
        BindGeneral(section, key, defaultValue, new ConfigDescription(description));
    private ConfigEntry<T> BindGeneral<T>(string section, string key, T defaultValue, ConfigDescription description) =>
        Config.Bind(section, key, defaultValue, new ConfigDescription(description.Description, description.AcceptableValues,
            developerOptions.Describe(section, key, description.Description, false)));
    // F12 shows the password as asterisks, with the lock state beside it.
    private void DrawPassword(ConfigEntryBase entry)
    {
        var current = entry.BoxedValue as string ?? "";
        var typed = GUILayout.PasswordField(current, '*', GUILayout.ExpandWidth(true));
        if (typed != current) entry.BoxedValue = typed;
        GUILayout.Label(developerUnlocked ? Loc.L("열림", "Unlocked") : Loc.L("잠김", "Locked"), GUILayout.Width(70));
    }
    private DevSetting<T> BindDeveloper<T>(string section, string key, T defaultValue, string description) =>
        BindDeveloper(section, key, defaultValue, new ConfigDescription(description));
    private DevSetting<T> BindDeveloper<T>(string section, string key, T defaultValue, ConfigDescription description) =>
        new DevSetting<T>(Config.Bind(section, key, defaultValue, new ConfigDescription(description.Description, description.AcceptableValues,
            developerOptions.Describe(section, key, description.Description))), () => developerUnlocked);

    private void BindEffect(EnhancePath path, string section, float step, float cap)
    {
        var name = EnhanceEffects.KoreanStatName(path);
        var unit = path == EnhancePath.Ergonomics ? "" : " (%)";
        effectConfig[path] = (
            BindDeveloper(section, name + " / 단계" + unit, step, new ConfigDescription("최대 효과 기준 = 이 값 × 최대 단계와 효과 상한 중 작은 값. 실제 단계별 증가량은 후반 1→4배 곡선으로 배분됩니다. 기존 장비에도 적용됩니다.", new AcceptableValueRange<float>(0, 10))),
            BindDeveloper(section, name + " 상한" + unit, cap, new ConfigDescription("진화 보너스 적용 전 상한. 보너스 후 안전 한계는 감소율 80%, 인체공학 추가 50입니다.", new AcceptableValueRange<float>(0, path == EnhancePath.Ergonomics ? 50 : 80))));
    }
    private EffectTuning Tuning() => new EffectTuning
    {
        MaxLevel = maxLevel.Value, CompletionBonusPercent = completionBonus.Value, ThirdStarBonusPercent = thirdStarBonus.Value,
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
        EvolutionSuccessPercent = evolutionChance.Value, ThirdStarSuccessPercent = thirdStarChance.Value, EvolutionCostPercent = evolutionPrice.Value,
        PriceBasis = price.Value, CostPercent = percent.Value, CostPerLevel = growth.Value,
        MinimumCost = minimum.Value, FailureMode = failure.Value, Effects = Tuning(),
        BasicInsuranceReturnPercent = basicReturn.Value, AdvancedInsuranceReturnPercent = advancedReturn.Value,
        PremiumInsuranceReturnPercent = premiumReturn.Value, BasicInsuranceFeePercent = basicFee.Value,
        AdvancedInsuranceFeePercent = advancedFee.Value, PremiumInsuranceFeePercent = premiumFee.Value,
        Transcend = TranscendTuning()
    };
    private void ConfigureTranscend()
    {
        try { StatPatches.Registry.ConfigureTranscend(TranscendTuning()); }
        catch (System.Exception ex) { Logger.LogWarning("Gear Enhance: F12 초월 설정을 적용하지 못해 이전 값을 씁니다: " + ex.Message); }
    }

    private static void MenuPostfix(object __instance, object itemContext, object __result)
    {
        var plugin = Instance;
        if (plugin == null || !plugin.menuEnabled.Value || __result == null) return;
        try
        {
            if (!GameBridge.InStash(__instance)) return;
            var view = GameBridge.Get(itemContext, "ViewType")?.ToString();
            var item = GameBridge.Get(itemContext, "Item");
            if ((view == "Inventory" || view == "TradingPlayer") && GameBridge.Eligible(item) && !GameBridge.IsArmorPart(item))
                GameBridge.AddInteraction(__result, () => plugin.Open(__instance, item));
        }
        catch (Exception ex) { plugin.Logger.LogError(ex); }
    }

    private void Open(object context, object item)
    {
        if (busy || resultVisible) return;
        try
        {
            session = GameBridge.Get(context, "ClientSession") ?? throw Loc.Invalid("PMC 세션이 없습니다.", "No PMC session.");
            uiContext = context; selectedItem = item;
            itemId = GameBridge.Get(item, "Id")?.ToString() ?? throw Loc.Invalid("아이템 ID가 없습니다.", "No item ID.");
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
            visible = true; PlayInteraction(UiInteraction.Open); quote = null; preview = null; firstQuote = true; destructionConfirmed = false; generation++;
            protectDestruction = protectDowngrade = chanceBoost = false;
            if (uncertain) message = UncertainNotice;
            else RefreshQuote();
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
        busy = true; quote = null; message = resultMessage ?? Loc.L("견적을 불러오는 중입니다…", "Loading quote…");
        var current = generation;
        try
        {
            await GameBridge.Flush(session);
            if (firstQuote)
            {
                if (!await SyncEffects(session))
                    throw Loc.Invalid("강화 기록을 동기화하지 못했습니다. 서버 모드 버전과 연결을 확인하고 다시 불러와 주세요.",
                        "Could not sync enhancement records. Check the server mod version and connection, then reload.");
                selectedPath = StatPatches.Registry.Find(itemId)?.Path ?? selectedPath;
                firstQuote = false;
            }
            if (evolutionMode && StatPatches.Registry.StarLevel(itemId) >= evolutionTargetStars)
            {
                preview = null;
                message = resultMessage ?? StarEvolution.Stars(evolutionTargetStars) + Loc.L(" 스타 강화 진화를 완료했습니다.", " star evolution complete.");
                return;
            }
            if (!evolutionMode && (StatPatches.Registry.Find(itemId, selectedPath)?.Level ?? 0) >= maxLevel.Value)
            {
                preview = null;
                message = resultMessage ?? Loc.L("이 항목은 최대 단계입니다. 다른 항목을 선택해 주세요.", "This track is at the maximum level. Choose another track.");
                return;
            }
            var response = await GameBridge.WithTimeout(GameBridge.Query<EnhanceQuote>(
                "/rf/gear-enhance/quote", new { ProtocolVersion = EnhanceProtocol.Version, ItemId = itemId, Path = selectedPath, Settings = Settings(),
                    Protection = new EnhanceProtection { Destruction = protectDestruction, Downgrade = protectDowngrade }, ChanceBoost = chanceBoost,
                    Action = evolutionMode ? evolutionTargetStars == 3 ? EnhanceAction.EvolveThird : EnhanceAction.Evolve : EnhanceAction.Upgrade }));
            if (current != generation) { message = SettingsChanged; return; }
            quote = preview = response; weapon = response.IsWeapon; message = resultMessage ?? ""; destructionConfirmed = false;
            chanceBoost = response.ChanceBoost;
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
        var duration = evolutionMode ? evolutionAttemptDuration : attemptDuration;
        if (quote.ExpiresWithin(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), duration + QuoteSubmitMargin))
        {
            LoadQuote(Loc.L("견적 유효 시간이 얼마 남지 않아 새로 불러왔습니다. 비용을 확인하고 다시 눌러 주세요.",
                "The quote was about to expire, so it was reloaded. Check the cost and press again."));
            return;
        }
        var started = Clock;
        attemptUsesAudioClock = false;
        if (soundsEnabled.Value && providedSounds.Value && feedbackAudio != null && !providedFeedbackUnavailable)
        {
            try { started = feedbackAudio.StartAttempt(soundVolume.Value / 100, evolutionMode); attemptUsesAudioClock = true; }
            catch (Exception ex) { ProvidedAudioFailed(ex); }
        }
        if (!soundsEnabled.Value) LogDetailed("강화 충전음 생략: F12 효과음 OFF");
        if (!attemptUsesAudioClock && soundsEnabled.Value) PlayFeedback(evolutionMode ? FeedbackCue.EvolutionAttempt : FeedbackCue.Attempt, false);
        if (!attemptTimeline.Begin(started, duration)) return;
        attemptWallStart = Clock;
        pendingAttempt = quote; pendingDestructionConfirmation = destructionConfirmed; pendingGeneration = generation;
        busy = true; quote = null; message = Loc.L("강화 진행 중…", "Enhancing…");
    }

    private void SetChanceBoost(bool enabled)
    {
        if (busy || resultVisible || uncertain) return;
        chanceBoost = enabled; generation++; quote = null; destructionConfirmed = false;
        RefreshQuote();
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
                throw Loc.Invalid("강화 준비 중 설정이나 화면이 바뀌었습니다. 견적을 다시 불러와 주세요.", "Settings or screen changed while preparing. Reload the quote.");
            await GameBridge.Flush(session);
            if (expectedGeneration != generation || !visible || !GameBridge.InStash(uiContext))
                throw Loc.Invalid("강화 준비가 취소됐습니다. 견적을 다시 불러와 주세요.", "Preparation was cancelled. Reload the quote.");
            message = Loc.L("강화 결과를 확인하는 중입니다…", "Checking the result…");
            submitted = true;
            var ok = await GameBridge.WithTimeout(GameBridge.Execute(session, attempt.QuoteId, confirmed), 30000);
            if (!ok) { message = Loc.L("강화가 거절됐습니다. 서버 오류 안내를 확인하고 견적을 다시 불러와 주세요.", "The enhancement was rejected. Check the server error notice and reload the quote."); return; }
            var receipt = await GameBridge.WithTimeout(GameBridge.Query<EnhanceReceipt>("/rf/gear-enhance/receipt", new { ProtocolVersion = EnhanceProtocol.Version, attempt.QuoteId }));
            message = receipt.Success
                ? Loc.L($"{(attempt.BindLegacy ? "기존 단계 복원 완료" : "강화 성공")}! {EnhanceEffects.Name(receipt.Path)} +{receipt.Level} · {receipt.Paid:N0} ₽ 소모",
                    $"{(attempt.BindLegacy ? "Old level restored" : "Enhanced")}! {EnhanceEffects.Name(receipt.Path)} +{receipt.Level} · spent {receipt.Paid:N0} ₽")
                : receipt.FailureMode == FailureMode.Downgrade
                    ? Loc.L($"강화 실패 · {EnhanceEffects.Name(receipt.Path)} +{receipt.Level}로 하락 · {receipt.Paid:N0} ₽ 소모",
                        $"Failed · {EnhanceEffects.Name(receipt.Path)} dropped to +{receipt.Level} · spent {receipt.Paid:N0} ₽")
                    : Loc.L($"강화 실패 · {receipt.Paid:N0} ₽ 소모", $"Failed · spent {receipt.Paid:N0} ₽") + "\n" + FailureText(receipt.FailureMode);
            if (receipt.Success && receipt.IsEvolution) message = Loc.L($"스타 강화 진화 성공! {StarEvolution.Stars(receipt.StarLevel)} {StarEvolution.ColorName(receipt.StarLevel)} 표식 · 진화 보너스 적용",
                $"Star evolution succeeded! {StarEvolution.Stars(receipt.StarLevel)} {StarEvolution.ColorName(receipt.StarLevel)} marker · evolution bonus applied");
            else if (receipt.Success && attempt.CompletesOnSuccess) message += Loc.L(" · 1차 스타 전 항목 완성! 진화 창을 열 수 있습니다.", " · All 1st star tracks maxed! Star evolution is open.");
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
                ? "\n" + UncertainNotice
                : Loc.L("\n아직 강화를 전송하지 않았습니다.", "\nThe enhancement was not sent yet."));
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

    private void PlayInteraction(UiInteraction cue)
    {
        if (soundsEnabled?.Value != true || uiFeedbackUnavailable) return;
        try { GameFeedback.Play(cue); }
        catch (Exception ex)
        {
            uiFeedbackUnavailable = true;
            Logger.LogWarning("강화 창 UI 소리 연결 실패: " + ex.GetBaseException().Message);
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
            if (providedSounds.Value && feedbackAudio == null) feedbackAudio = new EnhancementAudio(gameObject, charge, LogDetailed, () => detailedLogging.Value);
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
        main.Enqueue(() => PlayFeedback(cue));
    }
    private void PlayFeedback(FeedbackCue cue, bool allowProvided = true)
    {
        if (!soundsEnabled.Value) { LogDetailed("강화 효과음 생략: F12 효과음 OFF"); return; }
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
        ? Loc.L("본체·부착물·탄창·탄약이 함께 파괴됩니다.", "Gear, attachments, magazines and ammo are destroyed together.")
        : mode == FailureMode.ZeroDurability ? Loc.L("본체·부품의 현재 내구도 0 · 최대 내구도 유지", "Gear and parts drop to 0 durability · max durability kept")
        : mode == FailureMode.Downgrade ? Loc.L("선택 항목의 강화 단계 -1 · 다른 항목과 장비 유지", "Selected track -1 · other tracks and gear kept")
        : Loc.L("비용만 소모 · 장비와 기존 강화 단계 유지", "Fee only · gear and levels kept");

    private void Update()
    {
        for (var n = 0; n < 32 && main.TryDequeue(out var action); n++) action();
        if (syncWanted && !visible && !busy && !backgroundSyncing && Clock >= nextBackgroundSync) BackgroundSync();
        try { feedbackAudio?.Tick(); }
        catch (Exception ex) { ProvidedAudioFailed(ex); }
        if (pendingAttempt != null && (attemptTimeline.TryComplete(AttemptClock) || AudioClockStalled() && attemptTimeline.ForceComplete()))
            SubmitAttempt(pendingAttempt, pendingDestructionConfirmation, pendingGeneration);
        if (visible && !busy && !GameBridge.InStash(uiContext)) Close();
    }
    // Returned gear gets new ids from SPT mail. Learn their records outside raids,
    // at most every 5 seconds; the window does its own sync when it opens.
    private async void BackgroundSync()
    {
        nextBackgroundSync = Clock + 5;
        var active = StatPatches.Session;
        if (active == null || GameBridge.Get(GameBridge.RuntimeType("EFT.InGameStatus"), "InRaid") is not false) return;
        syncWanted = false; backgroundSyncing = true;
        try { if (await SyncEffects(active)) main.Enqueue(ItemMarkers.RefreshAll); }
        catch (Exception ex) { Logger.LogWarning(ex); }
        finally { backgroundSyncing = false; }
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
        if (detailedLogging?.Value == true && visible && repaintCount > 0)
            Logger.LogInfo($"강화 창 UI CPU: Repaint {repaintCount}회, 평균 {repaintTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / repaintCount:0.00}ms, 최대 {maximumRepaintTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.00}ms (GPU/FPS 측정 아님)");
        repaintCount = repaintTicks = maximumRepaintTicks = 0;
        visible = false; quote = preview = null; session = uiContext = selectedItem = itemIcon = null; generation++;
        insuranceVisible = false; insuranceQuote = null; insuranceMessage = "";
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
