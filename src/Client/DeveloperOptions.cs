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

        ["개발자 옵션 표시"] = "Show developer options",
        ["확률·비용·항상 성공·실패·능력치·보호·진화 밸런스 설정을 표시합니다. 바꾼 뒤 F12 창을 닫았다 다시 열면 적용됩니다. 기본은 숨김입니다."]
            = "Shows the chance, cost, always-succeed, failure, stat, protection and evolution balance settings. Close and reopen F12 after changing it. Hidden by default.",
        ["강화 메뉴 사용"] = "Enable enhance menu",
        ["창고의 무기·방어구 우클릭에 장비 강화를 표시합니다."] = "Adds Enhance gear to the right-click menu of weapons and armor in the stash.",
        ["성공률 (%)"] = "Success chance (%)",
        ["+0에서 +1로 강화할 때 성공률. 이후 현재 단계 × 단계별 감소량만큼 줄어듭니다."]
            = "Chance for +0 → +1. It then drops by current level × per-level loss.",
        ["단계별 성공률 감소 (%p)"] = "Chance loss per level (%p)",
        ["기본: +1 시도 75%, +10 시도 30%. 0이면 같은 확률을 유지합니다."] = "Default: 75% for +1, 30% for +10. 0 keeps the same chance.",
        ["항상 성공 (100%)"] = "Always succeed (100%)",
        ["켜면 성공률 설정 대신 100%를 적용합니다. 비용은 소모됩니다."] = "Uses 100% instead of the chance settings. The cost is still paid.",
        ["최대 강화 단계"] = "Max level",
        ["각 항목 최대 단계. 효과 곡선·1차 스타 진화 조건도 함께 바뀝니다. 무기 고위험 시작은 선택 항목 +6 시도(+5→+6)로 고정됩니다."]
            = "Max level of each track. Also changes the effect curve and the 1st star evolution condition. Weapon high risk always starts at the +6 attempt (+5 → +6).",
        ["기준 가격"] = "Base price",
        ["Handbook = 북밸류, Flea = SPT 플리 기준가. 장비 본체만 계산합니다."] = "Handbook = handbook value, Flea = SPT flea price. Only the gear body is counted.",
        ["기준 가격 대비 비용 (%)"] = "Cost of base price (%)",
        ["0이면 최소 비용만 적용됩니다."] = "0 uses only the minimum cost.",
        ["단계별 비용 배수"] = "Cost multiplier per level",
        ["기준가 × 비용 비율 × 배수^현재단계. 최대 1억 루블."] = "Base price × cost % × multiplier^current level. Max 100,000,000 roubles.",
        ["최소 비용 (루블)"] = "Minimum cost (roubles)",
        ["창고 안의 잠기지 않은 루블을 사용합니다."] = "Uses unlocked roubles in the stash.",
        ["실패 처리"] = "On failure",
        ["일반 구간 실패 처리. KeepItem: 비용만 / ZeroDurability: 현재 내구도 0 / Destroy: 본체·부착물·탄약 삭제 / Downgrade: 선택 항목 -1. 무기 고위험 구간에서는 아래 파괴·하락 규칙이 우선합니다."]
            = "Normal failure. KeepItem: fee only / ZeroDurability: current durability 0 / Destroy: delete gear, mods and ammo / Downgrade: selected track -1. In the weapon high-risk range the destruction/downgrade rules below apply instead.",
        ["무기 고위험 강화"] = "Weapon high risk",
        ["선택한 무기 항목의 다음 단계가 +6 이상이면(+5→+6부터) 실패 시 파괴 또는 해당 항목 -1. 다른 항목 단계는 무관합니다. 방어구는 일반 실패 처리를 따릅니다."]
            = "When the selected weapon track's next level is +6 or more (from +5 → +6), failure destroys the gear or drops that track by 1. Other tracks do not matter. Armor uses the normal failure rule.",
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
            = "Max effect = the smaller of this value × max level and the effect cap. Per-level gains follow a 1 → 4x late curve. Also applies to existing gear.",
        ["진화 보너스 적용 전 상한. 보너스 후 안전 한계는 감소율 80%, 인체공학 추가 50입니다."]
            = "Cap before the evolution bonus. After the bonus the safety limits are 80% reduction and +50 ergonomics.",
        ["자세한 로그 남기기"] = "Detailed logging",
        ["체크한 동안 효과음 설정·재생·대체 경로와 강화 창 UI CPU 진단을 LogOutput.log에 남깁니다. 실제 오류·경고는 체크와 관계없이 기록합니다."]
            = "While on, writes sound setup/playback/fallback and window UI CPU diagnostics to LogOutput.log. Real errors and warnings are always logged.",
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
        ["진화 기준가 대비 비용 (%)"] = "Evolution cost of base price (%)",
        ["진화 비용 = 최소 200만 루블과 본체 기준가 × 이 값/100 중 큰 값. 기본3배, 1회 최대1억 루블."]
            = "Evolution cost = the larger of 2,000,000 roubles and base price × this/100. Default 3x, max 100,000,000 roubles per try.",
        ["3차 스타 성공률 (%)"] = "3rd star chance (%)",
        ["2차 스타 성공 및 전 항목 최대인 장비만 시도. 실패 시 보호 없이 파괴합니다."]
            = "Only gear with a 2nd star and every track maxed. Failure destroys it with no protection.",
        ["3차 스타 총 추가 효과 (%)"] = "3rd star total bonus (%)",
        ["3차 스타 성공 시 기본 강화 효과의3배(+200%). 2차 배율에 중첩하지 않습니다. 반동15%→45%, 인체공학+10→+30. 감소율80%/추가인체공학50 상한, 빨간색 ★★★."]
            = "On 3rd star success, 3x the base effect (+200%). Does not stack with the 2nd star. Recoil 15% → 45%, ergonomics +10 → +30. Capped at 80% reduction / +50 ergonomics, red ★★★.",
    };
}
