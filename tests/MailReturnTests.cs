using System.Reflection;
using GearEnhance;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Spt.Dialog;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using Xunit;

// SPT's JsonUtil builds its converter cache lazily and not thread-safely; parallel
// test classes occasionally hit a NullReferenceException inside System.Text.Json.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace GearEnhanceTests;

// Insurance returns and expired flea offers arrive as SPT mail with new ids.
// The id replacement below is SPT's own ReplaceIDs, applied the way MailSendService does.
[Collection("MailReturnPatch")]
public sealed class MailReturnTests
{
    private static readonly MongoId Session = new("111111111111111111111111");
    private static readonly MongoId Stash = new("222222222222222222222222");
    private static readonly MongoId Gun = new("333333333333333333333333");
    private static readonly MongoId Scope = new("444444444444444444444444");
    private static readonly MongoId Other = new("888888888888888888888888");
    private static readonly MongoId GunTpl = new("555555555555555555555555");
    private static readonly MongoId ScopeTpl = new("777777777777777777777777");
    private static readonly JsonUtil Json = new([new SptJsonConverterRegistrator()]);

    private sealed class Backend(SptProfile saved) : SptEnhanceBackend(null!, null!, null!, null!, null!, null!)
    {
        public override SptProfile Profile(MongoId session) => session == Session ? saved : null!;
        public override bool Eligible(MongoId template) => true;
        public override bool IsWeapon(MongoId template) => true;
        public override (double, string) Price(MongoId template, PriceBasis basis) => (100000, "북밸류");
        public override HashSet<string> SlotNames(MongoId template) => new();
    }

    private sealed class Rig
    {
        public readonly SptProfile Saved;
        public readonly PmcData Pmc = new()
        {
            Id = Session, Inventory = new() { Stash = Stash, Items = [] }, InsuredItems = [],
            RagfairInfo = new() { Offers = [] }
        };
        public readonly EnhanceService Service;
        public Rig()
        {
            Pmc.Inventory!.Items!.Add(Weapon(Gun, GunTpl));
            Pmc.Inventory.Items.Add(new Item { Id = Scope, Template = ScopeTpl, ParentId = Gun.ToString(), SlotId = "mod_scope",
                Upd = new() { Repairable = new() { Durability = 50, MaxDurability = 75 } } });
            Pmc.Inventory.Items.Add(Weapon(Other, GunTpl));
            Saved = new SptProfile { CharacterData = new() { PmcData = Pmc }, InsuranceList = [], DialogueRecords = new() };
            Saved.ExtensionData = new()
            {
                ["rfGearEnhance"] = new EnhanceState
                {
                    Tracks = new() { [Gun.ToString()] = new() { [EnhancePath.Recoil] = 10, [EnhancePath.Ergonomics] = 10, [EnhancePath.Precision] = 10 } },
                    EvolvedItems = [Gun.ToString()], ThirdStarItems = [Gun.ToString()]
                }
            };
            Service = new(new Backend(Saved), Json);
        }
        private static Item Weapon(MongoId id, MongoId template) => new()
        {
            Id = id, Template = template, ParentId = Stash.ToString(), SlotId = "hideout",
            Upd = new() { Repairable = new() { Durability = 80, MaxDurability = 100 } }
        };
        public EnhanceState State => (EnhanceState)Saved.ExtensionData!["rfGearEnhance"]!;
        // Lost in raid (or listed on flea): the gun and its scope leave the inventory.
        public List<Item> TakeGun()
        {
            var tree = Pmc.Inventory!.Items!.Where(i => i.Id == Gun || i.Id == Scope).ToList();
            Pmc.Inventory.Items!.RemoveAll(tree.Contains);
            return tree;
        }
        public void TouchOtherItem() => Service.Quote(Session, Other.ToString(), new() { AlwaysSucceed = true });
        // MailSendService.SendMessageToPlayer with the patch's prefix and postfix around
        // SPT's own clone + ReplaceIDs, then the mail is stored like the real dialogue.
        public List<Item> Mail(List<Item> attachments)
        {
            MailReturnPatch.Service = Service;
            var details = new SendMessageDetails { RecipientId = Session, Items = attachments };
            MailReturnPatch.Prefix(details);
            details.Items = Json.Deserialize<List<Item>>(Json.Serialize(attachments)!)!.ReplaceIDs().ToList();
            MailReturnPatch.Postfix(details);
            var dialogue = new Dialogue { Id = new MongoId(), Messages = [new Message { Id = new MongoId(), Items = new() { Data = details.Items } }] };
            Saved.DialogueRecords![dialogue.Id] = dialogue;
            return details.Items;
        }
        public void Collect(List<Item> mailed)
        {
            foreach (var item in mailed)
            {
                if (item.Template == GunTpl) { item.ParentId = Stash.ToString(); item.SlotId = "hideout"; }
                Pmc.Inventory!.Items!.Add(item);
            }
            Saved.DialogueRecords!.Clear();
        }
    }

    [Fact]
    public void Insurance_return_keeps_every_track_and_star_on_the_new_ids()
    {
        var rig = new Rig();
        var lost = rig.TakeGun();
        rig.Saved.InsuranceList!.Add(new Insurance { TraderId = new MongoId(), Items = lost });
        // Opening the window on another item used to prune the lost gun's records.
        rig.TouchOtherItem();
        Assert.True(rig.State.Tracks.ContainsKey(Gun.ToString()));

        var mailed = rig.Mail(lost);
        rig.Saved.InsuranceList.Clear();
        var newGun = mailed.Single(i => i.Template == GunTpl).Id.ToString();
        Assert.NotEqual(Gun.ToString(), newGun);
        Assert.False(rig.State.Tracks.ContainsKey(Gun.ToString()));
        Assert.DoesNotContain(Gun.ToString(), rig.State.EvolvedItems);
        rig.TouchOtherItem();
        // Still in mail: kept, and already sent to the client for the badge.
        Assert.Contains(rig.Service.Snapshot(Session).Items, r => r.ItemId == newGun && r.StarLevel == 3);

        rig.Collect(mailed);
        var snapshot = rig.Service.Snapshot(Session).Items.Where(r => r.ItemId == newGun).ToArray();
        Assert.Equal(3, snapshot.Length);
        Assert.All(snapshot, r => { Assert.Equal(10, r.Level); Assert.Equal(3, r.StarLevel); });
        // The server reads the moved tracks for the new id: every track is already at maximum.
        var error = Assert.Throws<InvalidOperationException>(() => rig.Service.Quote(Session, newGun, new() { AlwaysSucceed = true }, EnhancePath.Recoil));
        Assert.Contains("최대 단계", error.Message);
        Assert.Equal("This track is at the maximum level. Choose another track.", Loc.EnglishOf(error));
    }

    [Fact]
    public void Expired_flea_offer_returns_with_its_records()
    {
        var rig = new Rig();
        var listed = rig.TakeGun();
        rig.Pmc.RagfairInfo!.Offers!.Add(new RagfairOffer { User = new() { Id = Session }, Items = listed });
        rig.TouchOtherItem();
        Assert.True(rig.State.Tracks.ContainsKey(Gun.ToString()));

        rig.Pmc.RagfairInfo.Offers.Clear();
        var mailed = rig.Mail(listed);
        rig.Collect(mailed);
        var newGun = mailed.Single(i => i.Template == GunTpl).Id.ToString();
        Assert.Equal(30, rig.Service.Snapshot(Session).Items.Where(r => r.ItemId == newGun).Sum(r => r.Level));
    }

    // 1.0.0 pruned an insured gun's records the moment any enhancement window opened
    // while it waited for the trader. Updated before the return mail, the receipts rebuild it.
    [Fact]
    public void Insured_gun_pruned_by_1_0_0_is_rebuilt_from_its_receipts_on_return()
    {
        var rig = new Rig();
        var gun = Gun.ToString();
        rig.State.Tracks.Clear(); rig.State.EvolvedItems.Clear(); rig.State.ThirdStarItems.Clear();
        rig.State.Receipts.AddRange(new EnhanceReceipt[]
        {
            new() { ItemId = gun, Path = EnhancePath.Recoil, Level = 1, Success = true },
            new() { ItemId = Other.ToString(), Path = EnhancePath.Recoil, Level = 9, Success = true },
            new() { ItemId = gun, Path = EnhancePath.Recoil, Level = 2, Success = true },
            new() { ItemId = gun, Path = EnhancePath.Ergonomics, Level = 2, Success = true },
            new() { ItemId = gun, Path = EnhancePath.Ergonomics, Level = 1, FailureMode = FailureMode.Downgrade },
            new() { ItemId = gun, Path = EnhancePath.Precision, Level = 1, Success = true },
            new() { ItemId = gun, Path = EnhancePath.Precision, Level = 0, FailureMode = FailureMode.Downgrade },
            new() { ItemId = gun, Path = EnhancePath.Recoil, Level = 2, FailureMode = FailureMode.KeepItem, Evolved = true, StarLevel = 2 }
        });
        var lost = rig.TakeGun();
        rig.Saved.InsuranceList!.Add(new Insurance { TraderId = new MongoId(), Items = lost });
        var mailed = rig.Mail(lost);
        var newGun = mailed.Single(i => i.Template == GunTpl).Id.ToString();
        var tracks = rig.State.Tracks[newGun];
        Assert.Equal(2, tracks[EnhancePath.Recoil]);
        Assert.Equal(1, tracks[EnhancePath.Ergonomics]);
        Assert.False(tracks.ContainsKey(EnhancePath.Precision));
        Assert.Contains(newGun, rig.State.EvolvedItems);
        Assert.DoesNotContain(newGun, rig.State.ThirdStarItems);
        Assert.False(rig.State.Tracks.ContainsKey(gun));
        Assert.False(rig.State.Tracks.ContainsKey(Other.ToString()));
    }

    [Fact]
    public void Receipts_never_revive_destroyed_or_fully_downgraded_gear()
    {
        var state = new EnhanceState();
        state.Receipts.Add(new() { ItemId = "a", Path = EnhancePath.Recoil, Level = 6, Success = true });
        state.Receipts.Add(new() { ItemId = "a", Path = EnhancePath.Recoil, Level = 6, FailureMode = FailureMode.Destroy });
        state.Receipts.Add(new() { ItemId = "b", Path = EnhancePath.Recoil, Level = 1, Success = true });
        state.Receipts.Add(new() { ItemId = "b", Path = EnhancePath.Recoil, Level = 0, FailureMode = FailureMode.Downgrade });
        Assert.False(EnhanceService.RecoverFromReceipts(state, "a"));
        Assert.False(EnhanceService.RecoverFromReceipts(state, "b"));
        Assert.False(EnhanceService.RecoverFromReceipts(state, "never"));
        Assert.Empty(state.Tracks);
    }

    [Fact]
    public void Item_that_never_comes_back_is_still_pruned()
    {
        var rig = new Rig();
        rig.TakeGun();
        rig.TouchOtherItem();
        Assert.False(rig.State.Tracks.ContainsKey(Gun.ToString()));
        Assert.Empty(rig.State.EvolvedItems);
    }

    [Fact]
    public void Mail_copy_of_an_owned_item_does_not_duplicate_records()
    {
        var rig = new Rig();
        var copy = rig.Pmc.Inventory!.Items!.Where(i => i.Id == Gun || i.Id == Scope).ToList();
        var mailed = rig.Mail(copy);
        Assert.True(rig.State.Tracks.ContainsKey(Gun.ToString()));
        Assert.DoesNotContain(mailed, i => rig.State.Tracks.ContainsKey(i.Id.ToString()));
    }

    [Fact]
    public void Unexpected_mail_list_shape_is_ignored()
    {
        var before = new[] { (Gun.ToString(), GunTpl.ToString()), (Scope.ToString(), ScopeTpl.ToString()) };
        var original = new List<Item> { new() { Id = Gun, Template = GunTpl }, new() { Id = Scope, Template = ScopeTpl } };
        Assert.Empty(MailReturnPatch.Pairs(before, original, original));
        Assert.Empty(MailReturnPatch.Pairs(before, original, [new() { Id = new MongoId(), Template = GunTpl }]));
        Assert.Empty(MailReturnPatch.Pairs(before, original, [new() { Id = new MongoId(), Template = ScopeTpl }, new() { Id = new MongoId(), Template = GunTpl }]));
        Assert.Empty(MailReturnPatch.Pairs(before, original, null));
        Assert.Equal(2, MailReturnPatch.Pairs(before, original, [new() { Id = new MongoId(), Template = GunTpl }, new() { Id = new MongoId(), Template = ScopeTpl }]).Count);
    }

    public class Silent : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.ReturnType is { IsValueType: true } type && type != typeof(void) ? Activator.CreateInstance(type) : null;
    }

    [Fact]
    public async Task Loader_binds_the_patch_to_the_real_spt_mail_method()
    {
        var logger = DispatchProxy.Create<ISptLogger<MailReturnLoader>, Silent>();
        await new MailReturnLoader(new Rig().Service, logger).OnLoadAsync(CancellationToken.None);
        var target = typeof(MailSendService).GetMethod(nameof(MailSendService.SendMessageToPlayer))!;
        var info = Harmony.GetPatchInfo(target);
        Assert.NotNull(info);
        Assert.Contains(info!.Prefixes, p => p.PatchMethod.DeclaringType == typeof(MailReturnPatch));
        Assert.Contains(info.Postfixes, p => p.PatchMethod.DeclaringType == typeof(MailReturnPatch));
        var raidEnd = Harmony.GetPatchInfo(typeof(SPTarkov.Server.Core.Services.InRaid.LocationLifecycleService).GetMethod("HandlePostRaidPmc",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!);
        Assert.NotNull(raidEnd);
        Assert.Contains(raidEnd!.Prefixes, p => p.PatchMethod.DeclaringType == typeof(RaidEndPatch));
        Assert.Contains(raidEnd.Postfixes, p => p.PatchMethod.DeclaringType == typeof(RaidEndPatch));
    }

    [Fact]
    public void Real_spt_mail_path_replaces_attachment_ids_inside_the_patched_method()
    {
        using var core = AssemblyDefinition.ReadAssembly(typeof(MailSendService).Assembly.Location);
        var mail = core.MainModule.GetType(typeof(MailSendService).FullName);
        var send = Assert.Single(mail.Methods, m => m.Name == "SendMessageToPlayer");
        Assert.Equal("messageDetails", Assert.Single(send.Parameters).Name);
        Assert.Contains(send.Body.Instructions, i => i.OpCode == OpCodes.Call
            && i.Operand is MethodReference m && m.Name == "ProcessItemsBeforeAddingToMail");
        var process = Assert.Single(mail.Methods, m => m.Name == "ProcessItemsBeforeAddingToMail");
        var calls = process.Body.Instructions.Where(i => i.Operand is MethodReference).Select(i => ((MethodReference)i.Operand).Name).ToList();
        var replace = calls.IndexOf("ReplaceIDs");
        Assert.True(replace >= 0);
        Assert.Equal("set_Items", calls.Skip(replace).First(n => n.StartsWith("set_") || n == "GetEnumerator"));
    }
}

public sealed class ErrorLanguageTests
{
    [Fact]
    public async Task Query_errors_carry_korean_and_english_text()
    {
        var json = new JsonUtil([new SptJsonConverterRegistrator()]);
        var queries = new EnhanceQueries(json, null!);
        var request = json.Serialize(new StateRequest { ProtocolVersion = EnhanceProtocol.Version - 1 })!;
        var raw = (string)await queries.HandleStaticAsync("/rf/gear-enhance/state", request, new MongoId(), "");
        var reply = json.Deserialize<ApiReply<EnhanceSnapshot>>(raw)!;
        Assert.False(reply.Ok);
        Assert.Contains("업데이트", reply.Error);
        Assert.Equal($"Update both the Gear Enhance client and server to {EnhanceProtocol.ModVersion}.", reply.ErrorEnglish);
    }
}
