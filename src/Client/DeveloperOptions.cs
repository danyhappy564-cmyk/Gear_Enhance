using System.Collections.Generic;

namespace GearEnhance;

// ConfigurationManager reads tags with this conventional class/field shape.
// The stored cfg keys stay unchanged; only the F12 presentation is overridden.
internal sealed class ConfigurationManagerAttributes
{
    public bool? Browsable;
    public string Category;
    public string DispName;
    public string Description;
    public System.Action<BepInEx.Configuration.ConfigEntryBase> CustomDrawer;
}

public enum UiLanguage { Auto, Korean, English }

internal sealed class DeveloperOptions
{
    private sealed class Entry
    {
        internal ConfigurationManagerAttributes Metadata;
        internal string Section, Key, Description;
        internal bool Developer;
    }
    private readonly List<Entry> entries = new List<Entry>();
    private bool visible, english;
    internal const string DeveloperSection = "99. 개발자 옵션";

    internal ConfigurationManagerAttributes Describe(string section, string key, string description = null, bool developer = true)
    {
        var entry = new Entry
        {
            Metadata = new ConfigurationManagerAttributes(), Section = section, Key = key,
            Description = description, Developer = developer
        };
        if (developer) entry.Metadata.Browsable = visible;
        entries.Add(entry); Apply(entry);
        return entry.Metadata;
    }
    internal void SetVisible(bool value)
    {
        visible = value;
        foreach (var entry in entries) if (entry.Developer) entry.Metadata.Browsable = value;
    }
    internal void SetLanguage(bool value)
    {
        english = value;
        foreach (var entry in entries) Apply(entry);
    }
    private void Apply(Entry entry)
    {
        var section = ConfigText.Get(entry.Section, english);
        var key = ConfigText.Get(entry.Key, english);
        entry.Metadata.Category = entry.Developer ? ConfigText.Get(DeveloperSection, english) : section;
        entry.Metadata.DispName = entry.Developer ? section + " · " + key : key;
        entry.Metadata.Description = entry.Description == null ? null : ConfigText.Get(entry.Description, english);
    }
}

// Korean F12 text → English. A missing pair falls back to Korean (checked by tests).
internal static class ConfigText
{
    internal static string Get(string korean, bool english) =>
        english && korean != null && English.TryGetValue(korean, out var text) ? text : korean;
    internal static bool Has(string korean) => English.ContainsKey(korean);

    private static readonly Dictionary<string, string> English = new Dictionary<string, string>
    {
        ["99. 개발자 옵션"] = "99. Developer options",
        ["1. 기본"] = "1. General",
        ["2. 확률"] = "2. Chance",
        ["3. 비용"] = "3. Cost",
        ["4. 실패"] = "4. Failure",
        ["5. 무기 효과"] = "5. Weapon effects",
        ["6. 방어구 효과"] = "6. Armor effects",
        ["7. 피드백"] = "7. Feedback",
        ["8. 완성 보너스"] = "8. Completion bonus",
        ["9. 강화 보호"] = "9. Protection",
        ["10. 스타 강화 진화"] = "10. Star evolution",
        ["11. 3차 스타 진화"] = "11. 3rd star evolution",
        ["12. 강화 보험"] = "12. Enhancement insurance",
        ["13. 초월 강화"] = "13. Transcendence",
        ["방탄판 자동 등록"] = "Auto-register plates",
        ["강화된 방어구에 방탄판·내피·바이저를 끼우면 바로 그 방어구 단계를 받습니다(같거나 더 싼 방탄판, 무료). 더 비싼 방탄판은 보험·방탄판 창에서 차액을 내고 등록합니다. 끄면 모두 버튼으로 등록합니다."]
            = "Plates, soft inserts and visors put into enhanced armor get its level at once (same or cheaper plates, free). Pricier plates are registered in the insurance · plates window for the price difference. When off, every plate is registered with the button.",
        ["방탄판 차액 배율 (%)"] = "Plate price difference multiplier (%)",
        ["더 비싼 방탄판 차액 = 가격 차이 × 가진 단계마다 (그 단계 비용 비율 × 평균 시도 횟수) × 이 값/100. 100이면 처음부터 그 방탄판으로 강화한 평균 비용과 같습니다."]
            = "Pricier plate fee = price difference × for each held level (that level's cost ratio × average attempts) × this/100. At 100 it equals the average cost of enhancing with that plate from the start.",
        ["2성 초월 성공률 (→1~→5, %)"] = "2-star transcendence chance (→1 to →5, %)",
        ["3성 초월 성공률 (→1~→5, %)"] = "3-star transcendence chance (→1 to →5, %)",
        ["다섯 숫자를 쉼표로 구분합니다. 형식이 틀리면 기본값을 씁니다."] = "Five numbers separated by commas. A wrong format uses the defaults.",
        ["2성 천장 (실패당 %p)"] = "2-star pity (%p per failure)",
        ["3성 천장 (실패당 %p)"] = "3-star pity (%p per failure)",
        ["실패할 때마다 그 장비의 다음 2성 초월 성공률에 더합니다. 성공하면 0으로 돌아갑니다."]
            = "Each failure adds this to that gear's next 2-star chance. A success resets it to 0.",
        ["실패할 때마다 그 장비의 다음 3성 초월 성공률에 더합니다. 성공하면 0으로 돌아갑니다."]
            = "Each failure adds this to that gear's next 3-star chance. A success resets it to 0.",
        ["천장 최대 (%p)"] = "Pity cap (%p)",
        ["쌓이는 천장의 최대치입니다."] = "The most pity that can build up.",
        ["2성 비용 (기준가 대비 %)"] = "2-star cost (% of base price)",
        ["3성 비용 (기준가 대비 %)"] = "3-star cost (% of base price)",
        ["비용 = 최소 비용과 기준가 × 이 값/100 × 배수^현재단계 중 큰 값. 방어구는 끼운 부품 가격을 더합니다. 최대 1억 루블."]
            = "Cost = the larger of the minimum cost and base price × this/100 × multiplier^current level. Armor adds its inserted parts. Max 100,000,000 roubles.",
        ["2성 최소 비용 (루블)"] = "2-star minimum cost (roubles)",
        ["3성 최소 비용 (루블)"] = "3-star minimum cost (roubles)",
        ["한 번 시도의 최소 비용입니다."] = "Minimum cost of one attempt.",
        ["초월 단계별 비용 배수"] = "Transcendence cost multiplier per level",
        ["단계가 오를수록 비용에 곱하는 배수입니다."] = "Multiplied into the cost for each level already held.",
        ["초월 하락 방지 비용 (기본 비용 대비 %)"] = "Transcendence downgrade protection (% of base cost)",
        ["4·5단계 시도에만 씁니다. 추가 비용 = 이번 초월 기본 비용 × 이 값/100(단계 제곱 없음). 100이면 하락 방지를 켰을 때 비용이 2배."]
            = "Level 4 and 5 attempts only. Extra fee = this attempt's base cost × this/100 (no level squared). At 100, protection doubles the cost.",
        ["초월 재료 요구"] = "Require transcendence materials",
        ["끄면 재료 없이 루블만 냅니다. 재료는 성공·실패와 관계없이 소모됩니다."]
            = "When off, only roubles are paid. Materials are used up on success and failure alike.",
        ["수평 반동 감소 / 단계 (%)"] = "Horizontal recoil reduction / level (%)",
        ["총기 2성 초월. 뒤로 차는 힘과 좌우로 튀는 각도를 함께 줄입니다. 반동 제어 강화와 합쳐 최대 80%."]
            = "Weapon 2-star transcendence. Reduces the backward kick and the left/right kick angle. At most 80% together with the recoil track.",
        ["최대 내구도 증가 / 단계 (%)"] = "Max durability bonus / level (%)",
        ["방어구 2성 초월. 원래 최대 내구도 기준. 바꾸면 다음 강화·초월·등록 때 다시 맞춰집니다."]
            = "Armor 2-star transcendence. Based on the original max durability. A change applies at the next enhancement, transcendence or registration.",
        ["발사속도 증가 / 단계 (%)"] = "Fire rate bonus / level (%)",
        ["총기 3성 초월(연사·점사 가능 총). 아래 상한과 비교해 작은 값을 더합니다."]
            = "Weapon 3-star transcendence (guns with auto or burst). Adds the smaller of this and the limit below.",
        ["발사속도 증가 상한 / 단계 (rpm)"] = "Fire rate bonus limit / level (rpm)",
        ["단계당 더하는 발사속도의 최대치입니다."] = "The most fire rate added per level.",
        ["고장 확률 감소 / 단계 (%)"] = "Malfunction chance reduction / level (%)",
        ["총기 3성 초월(반자동·볼트액션 총)."] = "Weapon 3-star transcendence (semi-auto and bolt-action guns).",
        ["과열 증가 감소 / 단계 (%)"] = "Overheat buildup reduction / level (%)",
        ["총기 3성 초월(반자동·볼트액션 총). 한 발당 쌓이는 과열을 줄입니다."]
            = "Weapon 3-star transcendence (semi-auto and bolt-action guns). Less overheat per shot.",
        ["둔탁상 감소 / 단계 (%)"] = "Blunt damage reduction / level (%)",
        ["방어구 3성 초월. 막았을 때 몸에 들어오는 피해를 줄입니다. 3단계 +1클, 5단계 +2클(최대 6클)."]
            = "Armor 3-star transcendence. Less damage gets through when the armor stops a hit. +1 class at level 3, +2 at level 5 (max class 6).",
        ["6클 대체 둔탁상 감소 / 클 (%)"] = "Class 6 substitute blunt reduction / class (%)",
        ["이미 6클 이상이라 등급을 못 올리는 부품이 1클 대신 받는 둔탁상 감소입니다."]
            = "Blunt reduction a part at class 6 or above gets for each class it cannot gain.",
        ["일반 보험 반환 확률 (%)"] = "Basic insurance return chance (%)",
        ["고급 보험 반환 확률 (%)"] = "Advanced insurance return chance (%)",
        ["프리미엄 보험 반환 확률 (%)"] = "Premium insurance return chance (%)",
        ["레이드에서 잃은 강화 장비가 강화된 상태로 우편 반환될 확률. 가입할 때의 값으로 고정됩니다."]
            = "Chance that enhanced gear lost in a raid is mailed back still enhanced. Locked in when the insurance is bought.",
        ["일반 보험료 (강화 가치 대비 %)"] = "Basic insurance fee (% of value)",
        ["고급 보험료 (강화 가치 대비 %)"] = "Advanced insurance fee (% of value)",
        ["프리미엄 보험료 (강화 가치 대비 %)"] = "Premium insurance fee (% of value)",
        ["강화 가치 = 지금 단계·스타까지 강화하는 데 드는 기본 비용 합계(방어구는 등록한 방탄판 가격 포함). 최소 비용 이상."]
            = "Enhancement value = the base cost of reaching the current levels and stars (armor includes its registered plates). At least the minimum cost.",

        ["개발자 옵션 비밀번호"] = "Developer options password",
        ["비밀번호를 넣으면 확률·비용·항상 성공·실패·능력치·보호·진화·보험 밸런스 설정이 표시되고 적용됩니다. 잠겨 있으면 기본 밸런스로 강화합니다(바꿔 둔 값은 지우지 않음). 입력 후 F12 창을 닫았다 다시 열면 표시됩니다."]
            = "Enter the password to show and apply the chance, cost, always-succeed, failure, stat, protection, evolution and insurance balance settings. While locked, enhancing uses the default balance (changed values are kept, not deleted). Close and reopen F12 after entering it.",
        ["강화 메뉴 사용"] = "Enable the Enhance gear menu",
        ["창고의 무기·방어구 우클릭에 장비 강화를 표시합니다. 방탄판·내피는 방어구와 함께 강화되므로 표시하지 않습니다."]
            = "Adds \"Enhance gear\" to the right-click menu of weapons and armor in the stash. Plates and inserts are enhanced with their armor, so they do not get it.",
        ["성공률 (%)"] = "Success chance (%)",
        ["+0에서 +1로 강화할 때 성공률. 이후 현재 단계 × 단계별 감소량만큼 줄어듭니다."]
            = "Chance for +0 → +1. It then drops by current level × per-level loss.",
        ["단계별 성공률 감소 (%p)"] = "Chance loss per level (%p)",
        ["기본: +1 시도 75%, +10 시도 30%. 0이면 같은 확률을 유지합니다."] = "Default: 75% for +1, 30% for +10. 0 keeps the same chance.",
        ["항상 성공 (100%)"] = "Always succeed (100%)",
        ["켜면 성공률 설정 대신 100%를 적용합니다. 비용은 소모됩니다."] = "Uses 100% instead of the chance settings. The cost is still paid.",
        ["최대 강화 단계"] = "Max level",
        ["각 항목 최대 단계. 효과 곡선·1차 스타 진화 조건도 함께 바뀝니다. 고위험 시작은 선택 항목 +6 시도(+5→+6)로 고정됩니다."]
            = "Max level of each track. Also changes the effect curve and the 1st star evolution condition. High risk always starts at the +6 attempt (+5 → +6).",
        ["기준 가격"] = "Base price",
        ["Handbook = 북밸류, Flea = SPT 플리 기준가. 무기는 본체만, 방어구는 슬롯에 끼운 방탄판·삽입물·바이저 가격을 더합니다."]
            = "Handbook = handbook value, Flea = SPT flea price. Weapons count the body only; armor adds the plates, inserts and visors in its slots.",
        ["기준 가격 대비 비용 (%)"] = "Cost (% of base price)",
        ["0이면 최소 비용만 적용됩니다."] = "0 uses only the minimum cost.",
        ["단계별 비용 배수"] = "Cost multiplier per level",
        ["기준가 × 비용 비율 × 배수^현재단계. 최대 1억 루블."] = "Base price × cost % × multiplier^current level. Max 100,000,000 roubles.",
        ["최소 비용 (루블)"] = "Minimum cost (roubles)",
        ["창고 안의 잠기지 않은 루블을 사용합니다."] = "Uses unlocked roubles in the stash.",
        ["실패 처리"] = "On failure",
        ["일반 구간 실패 처리. KeepItem: 비용만 / ZeroDurability: 현재 내구도 0 / Destroy: 본체·부착물·탄약 삭제 / Downgrade: 선택 항목 -1. 고위험 구간에서는 아래 파괴·하락 규칙이 우선합니다."]
            = "Failure outside the high-risk range. KeepItem: fee only / ZeroDurability: current durability 0 / Destroy: delete gear, mods and ammo / Downgrade: selected track -1. In the high-risk range the destruction/downgrade rules below apply instead.",
        ["고위험 강화 (무기·방어구)"] = "High risk (weapons and armor)",
        ["선택한 항목의 다음 단계가 +6 이상이면(+5→+6부터) 실패 시 파괴 또는 해당 항목 -1. 1.4.0부터 방어구도 같습니다. 다른 항목 단계는 무관합니다."]
            = "When the selected track's next level is +6 or more (from +5 → +6), failure destroys the gear or drops that track by 1. From 1.4.0 armor works the same. Other tracks do not matter.",
        ["고위험 실패 중 파괴 확률 (%)"] = "Destruction share of high-risk failures (%)",
        ["성공 판정과 별도입니다. 고위험 실패 중 이 확률로 본체·부착물·탄약 파괴, 나머지는 선택 항목 -1. 0은 하락만, 100은 파괴만."]
            = "Rolled separately from success. This share of high-risk failures destroys the gear, mods and ammo; the rest drop the selected track by 1. 0 = downgrade only, 100 = destruction only.",
        ["반동 감소 / 단계 (%)"] = "Recoil reduction / level (%)",
        ["반동 감소 상한 (%)"] = "Recoil reduction cap (%)",
        ["인체공학 증가 / 단계"] = "Ergonomics bonus / level",
        ["인체공학 증가 상한"] = "Ergonomics bonus cap",
        ["탄 퍼짐 감소 / 단계 (%)"] = "Dispersion reduction / level (%)",
        ["탄 퍼짐 감소 상한 (%)"] = "Dispersion reduction cap (%)",
        ["내구도 소모 감소 / 단계 (%)"] = "Durability loss reduction / level (%)",
        ["내구도 소모 감소 상한 (%)"] = "Durability loss reduction cap (%)",
        ["장비 페널티 완화 / 단계 (%)"] = "Gear penalty relief / level (%)",
        ["장비 페널티 완화 상한 (%)"] = "Gear penalty relief cap (%)",
        ["최대 효과 기준 = 이 값 × 최대 단계와 효과 상한 중 작은 값. 실제 단계별 증가량은 후반 1→4배 곡선으로 배분됩니다. 기존 장비에도 적용됩니다."]
            = "Max effect = the smaller of this value × max level and the effect cap. Per-level gains grow toward later levels (up to 4x). Also applies to existing gear.",
        ["진화 보너스 적용 전 상한. 보너스 후 안전 한계는 감소율 80%, 인체공학 추가 50입니다."]
            = "Cap before the evolution bonus. After the bonus the safety limits are 80% reduction and +50 ergonomics.",
        ["자세한 로그 남기기"] = "Detailed logging",
        ["체크한 동안 효과음 설정·재생·대체 경로, 강화 창 UI CPU, 초월 발사속도 실측, 은신처 장비 연결 진단을 LogOutput.log에 남깁니다. 실제 오류·경고는 체크와 관계없이 기록합니다."]
            = "While on, writes sound setup/playback/fallback, window UI CPU, measured transcendence fire rate and hideout gear link diagnostics to LogOutput.log. Real errors and warnings are always logged.",
        ["강화 효과음"] = "Enhancement sounds",
        ["강화 중·결과·버튼·항목·체크박스·창 여닫기에 효과음을 재생합니다. 끄더라도 게이지 시간은 유지합니다."]
            = "Plays sounds for charging, results, buttons, tracks, check boxes and opening/closing the window. The gauge time stays the same when off.",
        ["전용 강화 음원 사용"] = "Use bundled enhancement sounds",
        ["사용자 제공 음원6개 사용. 끄면 기존 게임 UI 소리를 사용합니다. 전용 음원 오류 시에도 기본음으로 대체합니다."]
            = "Uses the 6 bundled sounds. When off, the game's own UI sounds are used. Also falls back to them if the bundled sounds fail.",
        ["전용 음원 음량 (%)"] = "Bundled sound volume (%)",
        ["게임의 마스터/인터페이스 음량에 추가로 곱합니다. 0이면 전용 음원 무음. 기본 게임음 모드에서는 게임 소리 설정을 사용합니다."]
            = "Multiplied on top of the game's master/interface volume. 0 mutes the bundled sounds. Game-sound mode uses the game's sound settings.",
        ["전 항목 완성 추가 효과 (%)"] = "2nd star bonus effect (%)",
        ["2차 스타 성공 시 기본 강화 효과의2배(+100%). 반동15%→30%, 인체공학+10→+20. 감소율80%/추가인체공학50 상한. 일반 최대 단계만으로 자동 적용되지 않습니다."]
            = "On 2nd star success, 2x the base effect (+100%). Recoil 15% → 30%, ergonomics +10 → +20. Capped at 80% reduction / +50 ergonomics. Not applied just by maxing levels.",
        ["파괴 방지 비용 계수 (%)"] = "Destruction protection fee (%)",
        ["추가 비용 = 기본 강화 비용 × 이 값/100 × 시도 단계². 해당 파괴 위험이 있을 때만 부과합니다."]
            = "Extra cost = base cost × this/100 × attempted level². Charged only when destruction is possible.",
        ["단계 하락 방지 비용 계수 (%)"] = "Downgrade protection fee (%)",
        ["추가 비용 = 기본 강화 비용 × 이 값/100 × 시도 단계². 하락 위험이 있을 때만 부과합니다. 보호비도 성공/실패와 관계없이 소모됩니다."]
            = "Extra cost = base cost × this/100 × attempted level². Charged only when a downgrade is possible. Paid on success and failure alike.",
        ["진화 성공률 (%)"] = "Evolution chance (%)",
        ["1차 스타 전 항목 최대 달성 후 별도 진화 창에서 사용합니다. 실패 시 파괴, 보호 불가."]
            = "Used in the separate evolution window after every 1st star track is maxed. Failure destroys the gear; no protection.",
        ["진화 기준가 대비 비용 (%)"] = "Evolution cost (% of base price)",
        ["진화 비용 = 최소 200만 루블과 본체 기준가 × 이 값/100 중 큰 값. 기본3배, 1회 최대1억 루블."]
            = "Evolution cost = the larger of 2,000,000 roubles and base price × this/100. Default 3x, max 100,000,000 roubles per try.",
        ["3차 스타 성공률 (%)"] = "3rd star chance (%)",
        ["2차 스타 성공 및 전 항목 최대인 장비만 시도. 실패 시 보호 없이 파괴합니다."]
            = "Only gear with a 2nd star and every track maxed. Failure destroys it with no protection.",
        ["3차 스타 총 추가 효과 (%)"] = "3rd star total bonus (%)",
        ["3차 스타 성공 시 기본 강화 효과의3배(+200%). 2차 배율에 중첩하지 않습니다. 반동15%→45%, 인체공학+10→+30. 감소율80%/추가인체공학50 상한, 빨간색 ★★★."]
            = "On 3rd star success, 3x the base effect (+200%). Does not stack with the 2nd star. Recoil 15% → 45%, ergonomics +10 → +30. Capped at 80% reduction / +50 ergonomics, red ★★★.",
        // 1.4.0
        ["연출 줄이기"] = "Reduce motion",
        ["켜면 상태창 떨어짐·막대 채움·창 흔들림·신화 섬광·빛 고리·불꽃을 끄고 바로 보여 줍니다."] = "Turns off the stat drop-in, bar fills, shake, mythic flash, rings and sparks; everything shows at once.",
        ["창고 신화 칸 반짝임"] = "Stash mythic shimmer",
        ["창고에서 신화 장비 칸 테두리가 분홍↔금빛으로 반짝입니다. 끄면 고정된 분홍 테두리만 보입니다."] = "Mythic gear cells in the stash shimmer pink and gold. Off: a steady pink border.",
        ["파괴 확인 창"] = "Destruction prompt",
        ["파괴될 수 있는 시도(파괴 방지 없는 +6 이상, 스타 진화) 전에 한 번 더 묻습니다. ??? 는 이 설정과 관계없이 항상 묻습니다."] = "Asks once more before an attempt that can destroy the gear (unguarded +6 and up, star evolution). ??? always asks.",
        ["14. 성공률 재료"] = "14. Chance materials",
        ["15. 즉시 강화 · 파괴 방지권"] = "15. Instant · destruction tickets",
        ["16. ??? · 신화"] = "16. ??? · Mythic",
        ["일반 보험 돌아오는 시간 (시간)"] = "Basic insurance return delay (hours)",
        ["고급 보험 돌아오는 시간 (시간)"] = "Advanced insurance return delay (hours)",
        ["프리미엄 보험 돌아오는 시간 (시간)"] = "Premium insurance return delay (hours)",
        ["레이드에서 잃은 장비가 강화 보험으로 돌아올 때 우편이 오기까지의 실제 시간입니다. 0이면 즉시. 가입할 때 값으로 고정됩니다."] = "Real hours until gear lost in a raid and returned by enhancement insurance arrives by mail. 0 = at once. Fixed when the insurance is bought.",
        ["재료 1개가 더하는 성공률입니다. 2종까지 고를 수 있고 성공률은 100%를 넘지 않습니다."] = "Chance one item adds. Up to two kinds per attempt; the chance never exceeds 100%.",
        ["이 재료로 올릴 수 있는 최대 성공률입니다. 최대 개수 = 최대 ÷ 개당."] = "Most chance this material can add. Max count = max ÷ per item.",
        ["재료 1개마다 함께 내는 루블입니다. 재료와 루블은 성공·실패와 관계없이 소모됩니다."] = "Roubles paid with each item. Items and roubles are spent on success or failure.",
        ["독택 개당 성공률 (%p)"] = "Dogtag chance per item (%p)",
        ["독택 최대 (%p)"] = "Dogtag max (%p)",
        ["독택 개당 비용 (루블)"] = "Dogtag cost per item (roubles)",
        ["그래픽 카드 개당 성공률 (%p)"] = "Graphics card chance per item (%p)",
        ["그래픽 카드 최대 (%p)"] = "Graphics card max (%p)",
        ["그래픽 카드 개당 비용 (루블)"] = "Graphics card cost per item (roubles)",
        ["비트코인 개당 성공률 (%p)"] = "Bitcoin chance per item (%p)",
        ["비트코인 최대 (%p)"] = "Bitcoin max (%p)",
        ["비트코인 개당 비용 (루블)"] = "Bitcoin cost per item (roubles)",
        ["한 번에 쓸 재료 종류 (최대)"] = "Material kinds per attempt (max)",
        ["한 시도에 고를 수 있는 재료 종류 수입니다. ??? 에는 재료를 쓸 수 없습니다."] = "How many material kinds one attempt can use. ??? takes no materials.",
        ["즉시 강화 비용 (루블)"] = "Instant fee (roubles)",
        ["충전 게이지 없이 바로 결과를 봅니다. 일반 강화·초월·신화에만 있고 스타 진화·??? 에는 없습니다."] = "Skip the charge gauge and see the result at once. Enhancement, transcendence and mythic only; not star evolution or ???.",
        ["장비 하나가 평생 살 수 있는 장수입니다. 다 사면 더 살 수 없습니다."] = "Tickets one gear can ever buy. None more once they are all bought.",
        ["방지권 1장 가격입니다. 쓰면 진화 시도마다 1장 소모, 실패해도 장비가 남습니다."] = "Price of one ticket. Using it spends one per evolution attempt; a failure keeps the gear.",
        ["★★ 진화 방지권 최대 (장)"] = "★★ evolution tickets (max)",
        ["★★ 진화 방지권 가격 (루블)"] = "★★ evolution ticket price (roubles)",
        ["★★★ 진화 방지권 최대 (장)"] = "★★★ evolution tickets (max)",
        ["★★★ 진화 방지권 가격 (루블)"] = "★★★ evolution ticket price (roubles)",
        ["??? 성공 확률 (%)"] = "??? success chance (%)",
        ["★★★ 장비만 도전합니다. 실패하면 장비·부착물·기록이 파괴됩니다. 재료·방지권·즉시 강화는 쓸 수 없습니다."] = "★★★ gear only. Failure destroys the gear, its attachments and records. No materials, tickets or instant.",
        ["??? 비용 (루블)"] = "??? cost (roubles)",
        ["??? 한 번 도전 비용입니다."] = "Cost of one ??? attempt.",
        ["??? LEDX (개)"] = "??? LEDX (items)",
        ["??? 한 번에 필요한 LEDX 수입니다."] = "LEDX needed for one ??? attempt.",
        ["신화 성공률 (→1~→5, %)"] = "Mythic chance (→1 to →5, %)",
        ["신화 천장 (실패당 %p)"] = "Mythic pity (%p per failure)",
        ["실패할 때마다 그 장비의 다음 신화 성공률에 더합니다. 성공하면 0으로 돌아갑니다."] = "Added to that gear's next mythic chance after every failure. Resets to 0 on success.",
        ["신화 천장 최대 (%p)"] = "Mythic pity max (%p)",
        ["신화 기본 비용 (루블)"] = "Mythic base cost (roubles)",
        ["→1 시도 비용입니다. 단계마다 아래 배수를 곱합니다."] = "Cost of the →1 attempt. Each level multiplies it by the factor below.",
        ["신화 단계별 비용 배수"] = "Mythic cost multiplier per level",
        ["기본 1.1: 500만 → 550만 → 605만 → 665.5만 → 732.05만."] = "Default 1.1: 5,000,000 → 5,500,000 → 6,050,000 → 6,655,000 → 7,320,500.",
        ["신화 LEDX (개)"] = "Mythic LEDX (items)",
        ["신화 강화 한 번에 필요한 LEDX 수입니다."] = "LEDX needed for one mythic attempt.",
        ["신화 재료 요구"] = "Require mythic materials",
        ["끄면 ???·신화·다시 뽑기에 LEDX·그래픽 카드 없이 루블만 냅니다."] = "Off: ???, mythic attempts and rerolls cost roubles only, no LEDX or graphics card.",
        ["신화 하락 방지 비용 (기본 비용 대비 %)"] = "Mythic downgrade protection (% of base cost)",
        ["→4·→5 시도에만 씁니다. 실패하면 1단계 하락하며, 방지하면 이 비율만큼 더 냅니다."] = "Only for →4 and →5 attempts, which drop one level on failure. Protection costs this share extra.",
        ["신화 무기 탄 데미지·관통 / 단계 (%)"] = "Mythic weapon damage · penetration per level (%)",
        ["그 총으로 쏜 탄에만 붙습니다. 스타 배율을 받지 않습니다."] = "Only bullets fired from that gun. No star multiplier.",
        ["신화 방어구 받는 피해 감소 / 단계 (%)"] = "Mythic armor damage taken reduction per level (%)",
        ["신화 방어구·헬멧을 입은 플레이어가 받는 피해를 줄입니다."] = "Reduces the damage the player wearing mythic armor or a mythic helmet takes.",
        ["신화 방어구 추가 부위 (곳)"] = "Mythic armor extra zones",
        ["??? 성공 때 아직 못 막는 부위 중 무작위로 더 막는 곳의 수입니다."] = "Zones the gear does not protect yet that a ??? success adds at random.",
        ["다시 뽑기 기본 비용 (루블)"] = "Reroll base cost (roubles)",
        ["추가 부위 한 곳을 다시 뽑는 첫 비용입니다. 그 장비에서 뽑을 때마다 아래 배수를 곱합니다."] = "First cost to reroll one added zone. Every reroll on that gear multiplies it by the factor below.",
        ["다시 뽑기 비용 배수"] = "Reroll cost multiplier",
        ["기본 1.5: 200만 → 300만 → 450만 → 675만."] = "Default 1.5: 2,000,000 → 3,000,000 → 4,500,000 → 6,750,000.",
        ["다시 뽑기 그래픽 카드 (개)"] = "Reroll graphics cards (items)",
        ["다시 뽑기 한 번에 필요한 그래픽 카드 수입니다."] = "Graphics cards needed for one reroll.",
    };
}
