using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using GearEnhance;
using Xunit;

namespace GearEnhanceTests;

internal static class SealSetup
{
    // Tests share one profile id; the outside vault store is only used where a test passes its own folder.
    [ModuleInitializer]
    internal static void NoStore() => StateSeal.StoreDirectory = null;
}

// 1.4.0 seal of the enhancement records in the profile file (StateSeal.cs).
public sealed class SealTests
{
    private const string Profile = "111111111111111111111111";

    private static string Sealed(string records)
    {
        var node = (JsonObject)JsonNode.Parse(records)!;
        var seal = StateSeal.Sign(node);
        var json = node.ToJsonString();
        node[StateSeal.SealKey] = seal;
        node[StateSeal.VaultKey] = StateSeal.Pack(seal, json);
        return node.ToJsonString();
    }

    [Fact]
    public void Order_and_number_spelling_do_not_change_the_seal_but_values_do()
    {
        var a = StateSeal.Sign(JsonNode.Parse("{\"a\":5,\"b\":[1,2],\"c\":{\"x\":\"한\",\"y\":null}}"));
        Assert.Equal(a, StateSeal.Sign(JsonNode.Parse("{\"c\":{\"x\":\"\\ud55c\"},\"b\":[2,1.0],\"a\":5.0}")));
        Assert.NotEqual(a, StateSeal.Sign(JsonNode.Parse("{\"a\":6,\"b\":[1,2],\"c\":{\"x\":\"한\"}}")));
    }

    [Fact]
    public void A_valid_file_opens_as_is_and_an_edited_one_from_its_vault()
    {
        var file = Sealed("{\"Levels\":{},\"Tracks\":{\"g\":{\"Recoil\":5}}}");
        var (json, verdict) = StateSeal.Open(file, Profile, null);
        Assert.Equal(SealVerdict.Valid, verdict);
        Assert.Equal(5, (int)JsonNode.Parse(json)!["Tracks"]!["g"]!["Recoil"]!);
        Assert.Null(JsonNode.Parse(json)![StateSeal.SealKey]);
        var edited = file.Replace("\"Recoil\":5", "\"Recoil\":10");
        (json, verdict) = StateSeal.Open(edited, Profile, null);
        Assert.Equal(SealVerdict.Restored, verdict);
        Assert.Equal(5, (int)JsonNode.Parse(json)!["Tracks"]!["g"]!["Recoil"]!);
    }

    [Fact]
    public void Old_unsealed_files_load_but_a_sealed_profile_cannot_drop_its_seal()
    {
        Assert.Equal(SealVerdict.Legacy, StateSeal.Open("{\"Levels\":{}}", Profile, null).Verdict);
        var store = Path.Combine(Path.GetTempPath(), "gear-seal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = Sealed("{\"Levels\":{\"g\":3}}");
            Assert.Equal(SealVerdict.Valid, StateSeal.Open(file, Profile, store).Verdict);
            // Seal and vault both deleted and the level raised: the copy kept outside the file wins.
            var (json, verdict) = StateSeal.Open("{\"Levels\":{\"g\":9}}", Profile, store);
            Assert.Equal(SealVerdict.Restored, verdict);
            Assert.Equal(3, (int)JsonNode.Parse(json)!["Levels"]!["g"]!);
            // Another profile is still a plain old file.
            Assert.Equal(SealVerdict.Legacy, StateSeal.Open("{\"Levels\":{}}", "222222222222222222222222", store).Verdict);
        }
        finally { if (Directory.Exists(store)) Directory.Delete(store, true); }
    }

    [Fact]
    public void An_edited_file_with_a_broken_vault_is_cleared()
    {
        var node = (JsonObject)JsonNode.Parse(Sealed("{\"Levels\":{\"g\":3}}"))!;
        node["Levels"]!["g"] = 9;
        node[StateSeal.VaultKey] = "AAAA";
        Assert.Equal(("{}", SealVerdict.Reset), StateSeal.Open(node.ToJsonString(), Profile, null));
        // A vault cannot be edited either: its own seal no longer matches.
        var vault = StateSeal.Pack(StateSeal.Sign(JsonNode.Parse("{\"Levels\":{\"g\":3}}")), "{\"Levels\":{\"g\":9}}");
        Assert.Null(StateSeal.Unpack(vault));
    }
}
