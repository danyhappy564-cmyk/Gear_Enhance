using System;
using System.Collections.Generic;

namespace GearEnhance;

// 1.4.0 status panel numbers (design 1): the gear's own values read from the game without any
// enhancement, then the same formulas the effect hooks use. No Unity or game types here.
internal sealed class StatRow
{
    internal string Label = "";
    internal string Base = "";
    internal string Now = "";
    // What this attempt would change it to; null when it stays.
    internal string Next;
}

internal sealed class WeaponStats
{
    internal float RecoilUp, RecoilBack, RecoilDelta, Ergonomics, CenterOfImpact;
    internal int FireRate;
    internal bool Automatic;
    internal float Durability, MaxDurability;
}

internal sealed class ArmorStats
{
    internal int ArmorClass = -1, PlateClass, Zones = -1;
    internal float TemplateMax = float.NaN;
    internal float Speed = float.NaN, Turn = float.NaN, Ergonomics = float.NaN;
    internal List<ArmorCondition> Parts;
}

internal struct ArmorCondition
{
    internal float Durability, Max, TemplateMax;
    internal double Grant;
}

// Effect sizes at one moment: what the gear has now, or what it has after the attempt.
internal struct GearLevels
{
    internal double Recoil, Ergonomics, Precision, ArmorDurability, Mobility;
    internal int Second, Third, Mythic, Zones;
}

internal static class StatSheet
{
    // 1 MOA at 100 m is 2.9089 cm; the game's center of impact is in that unit.
    private const double MoaPerUnit = 100 / 2.9089;

    internal static List<StatRow> Weapon(WeaponStats raw, GearLevels now, GearLevels? next, TranscendTuning transcend, MythicTuning mythic)
    {
        string Vertical(GearLevels at) => Number(raw.RecoilUp * (1 + Delta(raw.RecoilDelta, at.Recoil)), "0.#");
        string Horizontal(GearLevels at)
        {
            var extra = TranscendEffects.HorizontalWithCap(at.Recoil, TranscendEffects.HorizontalRecoil(at.Second, transcend));
            return Number(raw.RecoilBack * (1 + Delta(raw.RecoilDelta, at.Recoil)) * (1 - extra / 100), "0.#");
        }
        string Ergonomics(GearLevels at) => Number(at.Ergonomics > 0 ? EnhanceEffects.Ergonomics(raw.Ergonomics, at.Ergonomics) : raw.Ergonomics, "0.#");
        string Accuracy(GearLevels at) => Number((at.Precision > 0 ? EnhanceEffects.Reduce(raw.CenterOfImpact, at.Precision) : raw.CenterOfImpact) * MoaPerUnit, "0.00");
        string FireRate(GearLevels at) => Number(raw.FireRate + (raw.Automatic ? TranscendEffects.FireRateBonus(raw.FireRate, at.Third, transcend) : 0), "0");
        string Bullet(GearLevels at) => Number(100 + MythicEffects.WeaponPercent(at.Mythic, mythic), "0.#") + "%";
        var plain = new GearLevels { Mythic = -1 };
        var rows = new List<StatRow>
        {
            Row(Loc.L("수직 반동", "Vertical recoil"), Vertical, plain, now, next),
            Row(Loc.L("수평 반동", "Horizontal recoil"), Horizontal, plain, now, next),
            Row(Loc.L("인체공학", "Ergonomics"), Ergonomics, plain, now, next),
            Row(Loc.L("정확도 (MOA)", "Accuracy (MOA)"), Accuracy, plain, now, next),
            Row(Loc.L("발사속도", "Fire rate"), FireRate, plain, now, next),
            Row(Loc.L("탄 데미지", "Bullet damage"), Bullet, plain, now, next),
            Row(Loc.L("관통력", "Penetration"), Bullet, plain, now, next)
        };
        var durability = Number(raw.Durability, "0") + "/" + Number(raw.MaxDurability, "0");
        rows.Insert(5, new StatRow { Label = Loc.L("내구도", "Durability"), Base = durability, Now = durability });
        return rows;
    }

    internal static List<StatRow> Armor(ArmorStats raw, GearLevels now, GearLevels? next, TranscendTuning transcend, MythicTuning mythic)
    {
        string Class(GearLevels at)
        {
            var body = raw.ArmorClass > 0 ? TranscendEffects.ArmorClass(at.Third, raw.ArmorClass).ToString() : "—";
            var plate = TranscendEffects.ArmorClass(at.Third, raw.PlateClass);
            return body + (raw.PlateClass > 0 ? Loc.L($" · 판 {plate}", $" · plate {plate}") : "");
        }
        string Zones(GearLevels at) => raw.Zones < 0 ? "—" : Loc.L($"{raw.Zones + at.Zones}곳", $"{raw.Zones + at.Zones}");
        string Max(GearLevels at)
        {
            if (raw.Parts == null) return Number(raw.TemplateMax * (1 + TranscendEffects.MaxDurability(at.Second, transcend) / 100), "0.#");
            // Modular armor: the parts' maximum durability summed (the game's "장갑 내구도" when full).
            // After the attempt: the server's grant rule raises every part's maximum (EnhanceService.Grant).
            double total = 0;
            foreach (var part in raw.Parts)
            {
                var delta = at.Second == now.Second ? 0
                    : Math.Round(part.TemplateMax * TranscendEffects.MaxDurability(at.Second, transcend) / 100, 2) - part.Grant;
                total += Math.Max(Math.Min(1, part.Max), part.Max + delta);
            }
            return Number(total, "0.#");
        }
        string Loss(GearLevels at) => Number(100 - at.ArmorDurability, "0.#") + "%";
        string Blunt(GearLevels at) => Number(100 - TranscendEffects.Blunt(at.Third, Math.Max(1, raw.ArmorClass), transcend), "0.#") + "%";
        string Taken(GearLevels at) => Number(100 - MythicEffects.ArmorPercent(at.Mythic, mythic), "0.#") + "%";
        string Penalty(float value, GearLevels at, string unit, string format) =>
            float.IsNaN(value) || float.IsInfinity(value) ? "—" : Number(value < 0 && at.Mobility > 0 ? EnhanceEffects.Reduce(value, at.Mobility) : value, format) + unit;
        var plain = new GearLevels { Mythic = -1 };
        return new List<StatRow>
        {
            Row(Loc.L("방어 등급", "Armor class"), Class, plain, now, next),
            Row(Loc.L("보호 부위", "Protected zones"), Zones, plain, now, next),
            Row(Loc.L("최대 내구도", "Max durability"), Max, plain, now, next),
            Row(Loc.L("내구도 소모", "Durability loss"), Loss, plain, now, next),
            Row(Loc.L("둔탁상", "Blunt damage"), Blunt, plain, now, next),
            Row(Loc.L("받는 피해", "Damage taken"), Taken, plain, now, next),
            Row(Loc.L("이동 속도", "Move speed"), at => Penalty(raw.Speed, at, "%", "0.##"), plain, now, next),
            Row(Loc.L("회전 속도", "Turn speed"), at => Penalty(raw.Turn, at, "%", "0.##"), plain, now, next),
            Row(Loc.L("인체공학", "Ergonomics"), at => Penalty(raw.Ergonomics, at, "", "0.#"), plain, now, next)
        };
    }

    private static StatRow Row(string label, Func<GearLevels, string> value, GearLevels plain, GearLevels now, GearLevels? next)
    {
        var row = new StatRow { Label = label, Base = value(plain), Now = value(now) };
        if (next.HasValue)
        {
            var after = value(next.Value);
            if (after != row.Now) row.Next = after;
        }
        return row;
    }

    private static double Delta(float delta, double percent) => percent > 0 ? EnhanceEffects.RecoilDelta(delta, percent) : delta;

    private static string Number(double value, string format) =>
        double.IsNaN(value) || double.IsInfinity(value) ? "—" : value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
}
