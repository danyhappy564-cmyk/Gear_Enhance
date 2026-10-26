using System;
using System.Collections.Generic;
using UnityEngine;

namespace GearEnhance;

// The character's prestige stage from the last quote or state sync (Max = 0: an older server sent none).
internal readonly struct PrestigeState
{
    public readonly int Level, Max;
    public PrestigeState(int level, int max) { Level = Math.Max(0, level); Max = Math.Max(0, max); }
    public PrestigeState(EnhanceSnapshot snapshot) : this(snapshot?.PrestigeLevel ?? 0, snapshot?.PrestigeMax ?? 0) { }
    public bool AtMax => Max > 0 && Level >= Max;
    public string Progress => AtMax ? Loc.L($"{Level:D2} · 최대", $"{Level:D2} · MAX") : $"{Level:D2} → {Level + 1:D2}";
}

// 1.4.0 prestige insurance tab (mockup "프레스티지 보험") and the registered-gear lists of both
// insurance tabs. Registered gear comes back by mail only when the character advances a prestige stage.
public sealed partial class Plugin
{
    private GUIStyle policyText, policyNote, policySmall, policyHead, policyNum, policyNumBig, policyValue, policyStatus, policySource;
    private GUIStyle policyNumRight, policyStage, policyCount, policyHint, policyProgress, policyMessage, policySideValue;
    // Rows rebuilt only when the state sync, the language or what they depend on changes (not per frame).
    private List<PolicyRow> prestigeRowCache, insuranceRowCache;
    private (int Version, bool English, bool Max) prestigeRowKey;
    private (int Version, bool English, InsuranceTier Tier) insuranceRowKey;
    private readonly GUIContent policyContent = new GUIContent();
    private static readonly Color MailBorder = Hex(0x6b4760), MailFill = Hex(0x1a1119), IntroBorder = Hex(0x6b5836);

    private struct PolicyRow
    {
        public string Name, Grade, Note, Value, Current, Delta, Payment, Status, Action;
        public Color Color, GradeColor;
        public bool Off;
        public Action Renew;
    }

    private void EnsurePolicyStyles()
    {
        if (ReferenceEquals(policySource, small)) return;
        policySource = small;
        GUIStyle Wrapped(GUIStyle style) { style.wordWrap = true; style.alignment = TextAnchor.UpperLeft; return style; }
        // .policy-box 12 px, .policy-note 11 px orange, small 11 px grey (mockup).
        policyText = Wrapped(TextStyle(12, Soft));
        policyNote = Wrapped(TextStyle(11, Caution));
        policySmall = Wrapped(TextStyle(11, Muted));
        policyHead = TextStyle(13, Heading, FontStyle.Bold);
        policyNum = TextStyle(17, Text, FontStyle.Normal, TextAnchor.MiddleLeft, true);
        policyNumBig = TextStyle(25, LightGold, FontStyle.Bold, TextAnchor.MiddleLeft, true);
        policyValue = TextStyle(30, LightGold, FontStyle.Bold, TextAnchor.MiddleLeft, true);
        policyStatus = TextStyle(21, Gold, FontStyle.Bold);
        policyNumRight = new GUIStyle(policyNum) { alignment = TextAnchor.MiddleRight };
        policyStage = new GUIStyle(policyNum) { alignment = TextAnchor.MiddleRight, fontSize = 15 };
        policyCount = new GUIStyle(policyNum) { fontSize = 15 };
        policyHint = new GUIStyle(policySmall) { alignment = TextAnchor.MiddleRight, wordWrap = false };
        policyProgress = new GUIStyle(policyValue) { fontSize = 34 };
        policyMessage = new GUIStyle(wrap) { fontSize = 12 };
        policySideValue = new GUIStyle(smallRight) { fontSize = 13 };
    }

    private float TextHeight(GUIStyle style, string text, float width)
    {
        policyContent.text = text;
        return style.CalcHeight(policyContent, width);
    }

    // A wrapped paragraph at (x, y); returns its height.
    private float Paragraph(float x, float y, float width, string text, GUIStyle style, Color color)
    {
        var height = TextHeight(style, text, width);
        Label(x, y, width, height, text, style, color);
        return height;
    }

    private static string Cash(long amount) => $"{amount:N0} ₽";

    // Korean particle after a two-digit stage number: 03·06·10 → 으로, the others → 로.
    private static string ToStage(int level) => level % 10 == 3 || level % 10 == 6 || level % 10 == 0 ? "으로" : "로";

    private static string GradeText(string id, int stars, int mythic)
    {
        if (mythic >= 0) return Loc.L($"✦ 신화 {mythic}", $"✦ Mythic {mythic}");
        var text = StarEvolution.Stars(Math.Max(1, stars));
        var transcend = StatPatches.Registry.TranscendTotal(id);
        if (transcend > 0) text += Loc.L($" · 초월 {transcend}", $" · Transcend {transcend}");
        var best = StatPatches.Registry.Find(id);
        if (best != null) text += $" · +{best.Level}";
        return text;
    }

    private static string ReturnEnds => Loc.L("반환되면 등록·납부 기록은 끝납니다. 다음 반환을 받으려면 다시 등록하세요.",
        "Return ends the registration and payment record. Register again for the next return.");
    private static string UnpaidCoverage => Loc.L("차액을 안 낸 장비는 마지막으로 낸 가치까지 보장합니다.",
        "Without paying the difference, coverage is capped at the last paid value.");

    private void DrawPrestigeTab()
    {
        EnsurePolicyStyles();
        var view = new Rect(BodyX, 180, BodyWidth, ButtonY - 10 - 180);
        var scrolls = midHeight > view.height + 1;
        midWidth = scrolls ? BodyWidth - 16 : BodyWidth;
        midScroll = GUI.BeginScrollView(view, midScroll, new Rect(0, 0, midWidth, Math.Max(midHeight, view.height)));
        var shown = insuranceQuote?.Action == EnhanceAction.PrestigeInsure ? insuranceQuote : null;
        var policies = StatPatches.Registry.Policies;
        var stage = shown != null && shown.PrestigeMax > 0 ? new PrestigeState(shown.PrestigeLevel, shown.PrestigeMax) : new PrestigeState(policies);
        var atMax = stage.AtMax;
        var width = midWidth - 28;
        SectionTitle(0, Loc.L("프레스티지 보험", "Prestige insurance"), IntroBorder, Hex(0xf0cf8c));
        var y = 34f;

        var intro = Loc.L("프레스티지를 달성해 다음 단계로 넘어갈 때만, 등록한 장비가 우편으로 돌아옵니다.",
            "Registered gear returns by mail only when you achieve prestige and advance to the next stage.");
        var loss = Loc.L("레이드에서 죽거나 잃어버린 장비는 돌려주지 않습니다.", "Gear lost on death or in a raid is not returned.");
        var introHeight = 24 + TextHeight(policyText, intro, width) + 6 + TextHeight(policyNote, loss, width);
        Box(new Rect(0, y, midWidth, introHeight), Hex(0x171410), IntroBorder);
        var line = Paragraph(14, y + 12, width, intro, policyText, Hex(0xd6cdb8));
        Paragraph(14, y + 18 + line, width, loss, policyNote, Caution);
        y += introHeight + 10;

        if (atMax)
        {
            var warn = Loc.L($"최대 프레스티지({stage.Max})에서는 다음 단계가 없어 보험이 작동하지 않습니다",
                $"At maximum prestige ({stage.Max}), there is no next stage, so this insurance does not work.");
            var warnHeight = 24 + TextHeight(policyText, warn, width);
            Box(new Rect(0, y, midWidth, warnHeight), Hex(0x221a10), Hex(0x7a5a30));
            Paragraph(14, y + 12, width, warn, policyText, Caution);
            y += warnHeight + 10;
        }
        y = DrawPrestigeMail(y, policies);

        // Current gear │ its value (mockup: one box, two columns).
        var registered = shown?.PrestigeRegistered == true;
        var due = shown == null ? 0 : Math.Max(0, shown.PrestigeFee - shown.PrestigePaid);
        var half = (midWidth - 40) / 2;
        var statusColor = atMax ? Muted : registered && due > 0 ? Caution : registered ? Good : Gold;
        var status = shown == null ? "—" : atMax ? Loc.L("작동 불가 · 최대 단계", "Unavailable · maximum stage")
            : registered ? due > 0 ? Loc.L("등록 유지 · 차액 발생", "Registered · difference due") : Loc.L("등록됨 · 납부 완료", "Registered · paid")
            : Loc.L("미등록", "Unregistered");
        var coverage = atMax ? Loc.L("다음 프레스티지 단계가 없습니다.", "There is no next prestige stage.")
            : registered && due > 0 ? UnpaidCoverage
            : registered ? Loc.L("추가 강화해도 등록은 유지됩니다.", "Registration stays after further upgrades.")
            : Loc.L("다음 프레스티지 반환을 받으려면 등록하세요.", "Register for a return at the next prestige.");
        var hint = registered ? Loc.L("마지막 납부 가치 ", "Last paid value ") + Cash(shown.PrestigeValue) : Loc.L("장비·강화·부착물 포함", "Gear, upgrades and attachments included");
        var gearHeight = 24 + 18 + 32 + Math.Max(TextHeight(policySmall, coverage, half), TextHeight(policySmall, hint, half));
        Box(new Rect(0, y, midWidth, gearHeight), Section, Line);
        Label(14, y + 12, half, 18, Loc.L("지금 장비 · ", "Current gear · ") + (itemName ?? ""), small);
        Label(14, y + 32, half, 30, status, policyStatus, statusColor);
        Paragraph(14, y + 64, half, coverage, policySmall, Muted);
        var right = 26 + half;
        Label(right, y + 12, half, 18, Loc.L("강화 가치", "Enhanced value"), small);
        Label(right, y + 30, half, 34, shown == null ? "—" : Cash(shown.InsuredValue), policyValue, LightGold);
        Paragraph(right, y + 64, half, hint, policySmall, Muted);
        y += gearHeight + 10;

        // Already paid │ fee at today's value │ difference due.
        var third = (width - 20) / 3;
        Box(new Rect(0, y, midWidth, 112), Section, Line);
        var rate = Loc.L($"장비 가치 × {prestigeFee.Value:0.##}% · F12 설정", $"Gear value × {prestigeFee.Value:0.##}% · F12 setting");
        var calc = atMax ? Loc.L("다음 단계 없음", "No next stage") : shown == null ? "" : $"{shown.PrestigeFee:N0} − {shown.PrestigePaid:N0}" + Loc.L(" · 최소 0", " · min 0");
        PaymentCell(14, y, third, Loc.L("이미 낸 금액", "Already paid"), shown == null ? "—" : Cash(shown.PrestigePaid), Loc.L("누적 납부 기준", "Cumulative payment"), LightGold);
        PaymentCell(24 + third, y, third, Loc.L("현재 가치 보험료", "Fee at current value"), shown == null || atMax ? "—" : Cash(shown.PrestigeFee), rate, LightGold);
        PaymentCell(34 + third * 2, y, third, Loc.L("지금 낼 차액", "Difference due"), shown == null || atMax ? "—" : Cash(due), calc, Gold);
        Fill(new Rect(14, y + 84, width, 1), Line);
        Label(14, y + 89, width, 16, Loc.L("차액 = 현재 보험료 − 이미 낸 금액 (최소 0 ₽)", "Difference = current fee − amount paid (minimum 0 ₽)"), policySmall, Soft);
        y += 122;

        y = DrawPolicyList(y, Loc.L("장비별 차액만 납부", "Pay only the difference per item"), Loc.L("마지막 납부 가치", "Last paid value"),
            PrestigeRows(policies, atMax), null) + 10;

        var rules = new (string Text, Color Color)[]
        {
            (Loc.L("추가 강화·방탄판 교체에도 등록 유지. 가치가 오르면 차액만 냅니다.", "Registration survives upgrades and plate swaps. Pay only the difference when value rises."), Soft),
            (Loc.L("가치가 내려가면 추가 납부 0 ₽. 이미 낸 금액은 환불하지 않습니다.", "If value falls, the extra fee is 0 ₽. Prior payments are not refunded."), Soft),
            (UnpaidCoverage, Soft),
            (Loc.L($"최대 프레스티지({(stage.Max > 0 ? stage.Max : 6)})에서는 다음 단계가 없어 작동하지 않습니다. 레이드에서 잃은 장비는 강화 보험이 맡습니다.",
                $"It does not work at maximum prestige ({(stage.Max > 0 ? stage.Max : 6)}): there is no next stage. Gear lost in raids is covered by the enhancement insurance."), Soft),
            (ReturnEnds, Gold)
        };
        var rulesHeight = 12 + 24f;
        foreach (var rule in rules) rulesHeight += TextHeight(policyText, rule.Text, width) + 6;
        Box(new Rect(0, y, midWidth, rulesHeight + 6), Section, Line);
        Label(14, y + 10, width, 20, Loc.L("등록 · 반환 규칙", "Registration · return rules"), policyHead, Heading);
        var ry = y + 36;
        foreach (var rule in rules) ry += Paragraph(14, ry, width, rule.Text, policyText, rule.Color) + 6;
        y += rulesHeight + 16;
        Label(2, y, midWidth - 4, 40, insuranceMessage ?? "", policyMessage, Good);
        y += 44;
        if (Event.current.type == EventType.Repaint) midHeight = y;
        GUI.EndScrollView();

        GUI.enabled = !busy && popup == PopupKind.None;
        if (BigButton(new Rect(BodyX, ButtonY, 233, ButtonHeight), Loc.L("닫기", "Close"), ButtonKind.Dark, UiInteraction.Close)) main.Enqueue(Close);
        var can = shown?.CanBuyPrestige == true;
        GUI.enabled = !busy && popup == PopupKind.None && !uncertain && can;
        var caption = shown == null ? Loc.L("견적 확인 중…", "Checking quote…")
            : atMax ? Loc.L("최대 단계 · 등록 불가", "Maximum stage · unavailable")
            : registered && !can ? Loc.L("등록됨 · 추가 납부 없음", "Registered · nothing due")
            : (registered ? Loc.L("차액 내고 갱신 · ", "Pay difference & renew · ") : Loc.L("등록 · ", "Register · ")) + Cash(shown.Cost);
        if (BigButton(new Rect(BodyX + 243, ButtonY, BodyWidth - 243, ButtonHeight), caption, ButtonKind.Gold) && shown != null)
            main.Enqueue(() => RunManaged(shown, registered ? Loc.L("프레스티지 보험 차액 납부", "Prestige insurance difference paid")
                : Loc.L("프레스티지 보험 등록", "Registered for prestige insurance"), false));
        GUI.enabled = true;
    }

    private void PaymentCell(float x, float y, float width, string caption, string amount, string note, Color color)
    {
        Label(x, y + 12, width, 16, caption, policySmall, Muted);
        Label(x, y + 28, width, 32, amount, policyNumBig, color);
        Label(x, y + 62, width, 16, note, policySmall, Muted);
    }

    // Mockup "반환 우편 도착": gear the last prestige mailed back, until it is collected.
    private float DrawPrestigeMail(float y, EnhanceSnapshot policies)
    {
        var mail = policies.PrestigeReturns ?? Array.Empty<PrestigeReturnLink>();
        if (mail.Length == 0) return y;
        var level = mail[0].Level;
        var width = midWidth - 28;
        var intro = Loc.L($"프레스티지 {level:D2}{ToStage(level)} 넘어오기 전에 등록한 장비입니다. 받으면 창고로 들어옵니다.",
            $"Gear registered before advancing to prestige {level:D2}. Collecting puts it in your stash.");
        var height = 12 + 26 + TextHeight(policyText, intro, width) + mail.Length * 38 + 10 + 32 + 8 + TextHeight(policyNote, ReturnEnds, width) + 12;
        Box(new Rect(0, y, midWidth, height), MailFill, MailBorder);
        Label(14, y + 10, width, 20, Loc.L($"반환 우편 도착 · {mail.Length}", $"Return mail arrived · {mail.Length}"), policyHead, Heading);
        Label(14, y + 10, width, 20, Loc.L($"프레스티지 {Math.Max(0, level - 1):D2} → {level:D2}", $"Prestige {Math.Max(0, level - 1):D2} → {level:D2}"),
            policyStage, MythGold);
        var row = y + 38 + Paragraph(14, y + 38, width, intro, policyText, Soft) + 4;
        foreach (var entry in mail)
        {
            if (entry == null) continue;
            Fill(new Rect(14, row, width, 1), Hex(0x2a1f28));
            var detail = GradeText(entry.ItemId, entry.Stars, entry.MythicLevel)
                + (entry.IsWeapon ? Loc.L(" · 조준경·소음기·탄창 보존", " · scope, suppressor, magazine kept") : Loc.L(" · 방탄판 보존", " · plates kept"));
            Label(14, row + 4, width - 60, 18, ItemNameOf(entry.Template), bodyBold, Text);
            Label(14, row + 21, width - 60, 16, detail, policySmall, Muted);
            Label(14, row + 4, width, 30, "×1", policyNumRight, MythGold);
            row += 38;
        }
        // The mockup's collect button: in game the attachments are taken from the message itself.
        var collect = new Rect(14, row + 10, width, 32);
        Box(collect, Hex(0x1f141d), Hex(0x8a3f79));
        Label(collect.x, collect.y, collect.width, collect.height, Loc.L("메시지 → 시스템 우편에서 첨부 장비를 받으세요", "Collect the attachments from Messenger → system mail"), smallCenter, MythGold);
        Paragraph(14, collect.yMax + 8, width, ReturnEnds, policyNote, Caution);
        return y + height + 10;
    }

    private List<PolicyRow> PrestigeRows(EnhanceSnapshot policies, bool atMax)
    {
        var key = (StatPatches.Registry.Version, Loc.English, atMax);
        if (prestigeRowCache != null && prestigeRowKey == key) return prestigeRowCache;
        prestigeRowKey = key;
        var rows = prestigeRowCache = new List<PolicyRow>();
        foreach (var link in policies.Prestige ?? Array.Empty<PrestigeLink>())
        {
            if (link == null) continue;
            var due = Math.Max(0, link.Fee - link.Paid);
            var id = link.ItemId;
            rows.Add(new PolicyRow
            {
                Name = ItemNameOf(link.Template), Grade = GradeText(id, link.Stars, link.MythicLevel), GradeColor = link.MythicLevel >= 0 ? Myth : Muted,
                Note = due > 0 ? Loc.L("마지막으로 낸 가치까지 보장", "Covered up to last paid value")
                    : link.CurrentValue < link.Value ? Loc.L("가치 하락 · 환불 없음", "Value fell · no refund") : Loc.L("현재 가치 보장", "Current value covered"),
                Color = due > 0 ? Caution : Good, Value = Cash(link.Value), Current = Loc.L("현재 ", "Now ") + Cash(link.CurrentValue),
                Delta = Cash(due), Payment = Loc.L("이미 납부 ", "Paid ") + Cash(link.Paid),
                Status = due > 0 ? Loc.L("등록 유지 · 차액 대기", "Registered · difference due") : Loc.L("등록됨", "Registered"),
                Action = due > 0 ? Loc.L("차액 내고 갱신", "Pay difference & renew") : Loc.L("납부 완료", "Paid"),
                Off = atMax || due == 0, Renew = () => RenewFrom(id, EnhanceAction.PrestigeInsure, InsuranceTier.None, due)
            });
        }
        return rows;
    }

    private List<PolicyRow> InsuranceRows()
    {
        var key = (StatPatches.Registry.Version, Loc.English, insuranceTier);
        if (insuranceRowCache != null && insuranceRowKey == key) return insuranceRowCache;
        insuranceRowKey = key;
        var rows = insuranceRowCache = new List<PolicyRow>();
        foreach (var link in StatPatches.Registry.Policies.Insurance ?? Array.Empty<InsuranceLink>())
        {
            if (link == null) continue;
            // The server lists a policy lapsed before 1.4.0 (no tier kept) as Advanced; None only from an older server.
            var tier = link.Tier == InsuranceTier.None ? InsuranceTier.Advanced : link.Tier;
            var due = Math.Max(0, link.Fee - link.Paid);
            var id = link.ItemId;
            rows.Add(new PolicyRow
            {
                Name = ItemNameOf(link.Template), Grade = GradeText(id, link.Stars, link.MythicLevel) + " · " + Loc.TierName(tier),
                GradeColor = link.Lapsed ? Caution : Good, Color = link.Lapsed ? Caution : Good,
                Note = link.Lapsed ? Loc.L("강화 성공·더 비싼 방탄판으로 풀림", "Released by an upgrade or a pricier plate") : "",
                Value = Cash(link.CurrentValue), Current = Loc.L("보험료 ", "Fee ") + Cash(link.Fee),
                Delta = Cash(link.Lapsed ? due : 0), Payment = Loc.L("이전 납부 ", "Paid ") + Cash(link.Paid),
                Status = link.Lapsed ? Loc.L("풀림 · 다시 가입", "Released · re-enroll") : Loc.L("가입 중", "Active"),
                Action = link.Lapsed ? Loc.L("차액 내고 갱신", "Pay difference & renew") : Loc.L("가입 중", "Active"),
                Off = !link.Lapsed, Renew = () => RenewFrom(id, EnhanceAction.Insure, tier, due)
            });
        }
        return rows;
    }

    // Mockup "등록된 장비": gear · tier │ value │ difference │ status / renew.
    private float DrawPolicyList(float y, string hint, string valueHead, List<PolicyRow> rows, string note)
    {
        EnsurePolicyStyles();
        const float RowHeight = 72;
        var width = midWidth - 28;
        var noteHeight = string.IsNullOrEmpty(note) ? 0 : TextHeight(policyNote, note, width) + 8;
        var height = 36 + 24 + (rows.Count == 0 ? 34 : rows.Count * RowHeight) + noteHeight + 8;
        Box(new Rect(0, y, midWidth, height), Section, Line);
        var titleText = Loc.L("등록된 장비 ", "Registered gear ");
        policyContent.text = titleText;
        var titleWidth = policyHead.CalcSize(policyContent).x;
        Label(14, y + 10, titleWidth, 20, titleText, policyHead, Heading);
        Label(14 + titleWidth, y + 10, 60, 20, rows.Count.ToString(), policyCount, Gold);
        Label(14, y + 10, width, 20, hint, policyHint, Muted);
        // grid-template-columns: 1.35fr 1fr .8fr 132px, gap 10.
        var flex = width - 132 - 30;
        var c1 = flex * 1.35f / 3.15f; var c2 = flex / 3.15f; var c3 = flex * 0.8f / 3.15f;
        var x1 = 14 + c1 + 10; var x2 = x1 + c2 + 10; var x3 = x2 + c3 + 10;
        var head = y + 36;
        Label(14, head, c1, 18, Loc.L("장비 · 등급", "Gear · tier"), policySmall, Muted);
        Label(x1, head, c2, 18, valueHead, policySmall, Muted);
        Label(x2, head, c3, 18, Loc.L("차액", "Difference"), policySmall, Muted);
        Label(x3, head, 132, 18, Loc.L("상태 / 갱신", "Status / renew"), policySmall, Muted);
        Fill(new Rect(14, head + 22, width, 1), Line);
        var top = head + 24;
        if (rows.Count == 0) Label(14, top + 8, width, 18, Loc.L("등록된 장비가 없습니다.", "No registered gear."), policyText, Soft);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var ry = top + index * RowHeight + 10;
            if (index > 0) Fill(new Rect(14, ry - 10, width, 1), Hex(0x22282e));
            Label(14, ry, c1, 18, row.Name, bodyBold, Text);
            Label(14, ry + 19, c1, 16, row.Grade, policySmall, row.GradeColor);
            Label(14, ry + 35, c1, 16, row.Note, policySmall, row.Color);
            Label(x1, ry, c2, 22, row.Value, policyNum, Text);
            Label(x1, ry + 24, c2, 16, row.Current, policySmall, Muted);
            Label(x2, ry, c3, 22, row.Delta, policyNum, row.Color);
            Label(x2, ry + 24, c3, 16, row.Payment, policySmall, Muted);
            Label(x3, ry, 132, 16, row.Status, policySmall, row.Color);
            GUI.enabled = !busy && !uncertain && popup == PopupKind.None && !row.Off;
            MiniButton(new Rect(x3, ry + 22, 132, 28), row.Action, row.Renew ?? (() => { }));
            GUI.enabled = true;
        }
        if (noteHeight > 0) Paragraph(14, top + (rows.Count == 0 ? 34 : rows.Count * RowHeight) + 4, width, note, policyNote, Caution);
        return y + height;
    }

    // Side panel of the prestige tab: what a return keeps, and the stage it waits for.
    private void DrawPrestigeSide()
    {
        EnsurePolicyStyles();
        var (offset, alpha) = UiMotion.Panel(Motion, Reduced);
        var x = StatusX + offset;
        var old = GUI.color;
        GUI.color = new Color(old.r, old.g, old.b, old.a * alpha);
        var shown = insuranceQuote?.Action == EnhanceAction.PrestigeInsure ? insuranceQuote : null;
        var stage = shown != null && shown.PrestigeMax > 0 ? new PrestigeState(shown.PrestigeLevel, shown.PrestigeMax) : new PrestigeState(StatPatches.Registry.Policies);
        var mythic = MythicLevel;
        var plates = shown?.InsertedParts ?? 0;
        var preserved = new (string Label, string Value, Color Color)[]
        {
            (Loc.L("강화 기록", "Upgrade record"), Loc.L("그대로 보존", "Kept"), Good),
            (Loc.L("★ 진화", "★ Evolution"), mythic >= 0 ? Loc.L("✦ 신화", "✦ Mythic") : StarEvolution.Stars(StarCount), mythic >= 0 ? Myth : Gold),
            (Loc.L("초월 · 신화", "Transcend · mythic"), Loc.L("달성 기록 보존", "Earned progress kept"), Myth),
            (Loc.L("부착물", "Attachments"), weapon ? Loc.L("함께 반환", "Returned together") : "—", Good),
            (Loc.L("방탄판", "Armor plates"), weapon ? "—" : plates > 0 ? Loc.L($"{plates}장 함께 반환", $"{plates} returned together") : Loc.L("함께 반환", "Returned together"), Good)
        };
        var height = 56 + 14 + 26 + preserved.Length * 31 + 20 + 1 + 20 + 24 + 44 + 40;
        Box(new Rect(x, 0, StatusWidth, height), Panel, Edge);
        Fill(new Rect(x + 1, 1, StatusWidth - 2, 55), HeaderFill);
        Fill(new Rect(x + 1, 56, StatusWidth - 2, 1), Line);
        Label(x + 16, 0, StatusWidth - 70, 56, Loc.L("프레스티지 보험", "Prestige insurance"), new GUIStyle(bodyBold) { fontSize = 15 }, LightGold);
        GUI.enabled = popup == PopupKind.None;
        IconButton(new Rect(x + StatusWidth - 48, 12, 36, 32), refreshIcon, 14, ReplayMotion);
        GUI.enabled = true;
        var y = 70f;
        Label(x + 16, y, StatusWidth - 32, 20, Loc.L("반환 시 보존", "Preserved on return"), policyHead, Heading);
        y += 26;
        for (var index = 0; index < preserved.Length; index++, y += 31)
        {
            var (drop, rowAlpha) = UiMotion.Row(Motion, UiMotion.BaseRowDelay(index), Reduced);
            var before = GUI.color;
            GUI.color = new Color(before.r, before.g, before.b, before.a * rowAlpha);
            Label(x + 16, y + drop, 110, 30, preserved[index].Label, small);
            Label(x + 120, y + drop, StatusWidth - 136, 30, preserved[index].Value, policySideValue, preserved[index].Color);
            Fill(new Rect(x + 16, y + drop + 30, StatusWidth - 32, 1), Hex(0x1d2228));
            GUI.color = before;
        }
        y += 20;
        Fill(new Rect(x + 16, y, StatusWidth - 32, 1), Line);
        y += 20;
        Label(x + 16, y, StatusWidth - 32, 20, Loc.L("프레스티지 진행", "Prestige progress"), policyHead, Heading);
        y += 24;
        Label(x + 16, y, StatusWidth - 32, 44, stage.Max == 0 && stage.Level == 0 ? "—" : stage.Progress, policyProgress, Gold);
        y += 46;
        Paragraph(x + 16, y, StatusWidth - 32, stage.AtMax ? Loc.L("다음 단계 없음 · 작동 불가", "No next stage · unavailable")
            : Loc.L("다음 단계로 넘어갈 때 1회 반환", "One return on advancing to the next stage"), policyNote, Caution);
        GUI.color = old;
    }
}
