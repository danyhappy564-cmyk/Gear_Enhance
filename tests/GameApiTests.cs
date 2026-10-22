using Mono.Cecil;
using Xunit;

namespace GearEnhanceTests;

public sealed class GameAssemblyFactAttribute : FactAttribute
{
    public GameAssemblyFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("GEAR_GAME_ASSEMBLY")))
            Skip = "GEAR_GAME_ASSEMBLY에 SPT 4.1 Assembly-CSharp.dll 경로를 지정해야 하는 API 검사";
    }
}

// Metadata validation only; these checks do not claim to run Unity or render UI.
public sealed class GameApiTests
{
    private static AssemblyDefinition Game() => AssemblyDefinition.ReadAssembly(
        Environment.GetEnvironmentVariable("GEAR_GAME_ASSEMBLY")!);

    [GameAssemblyFact]
    public void Context_menu_runtime_bindings_exist_in_actual_game_assembly()
    {
        using var game = Game();
        var context = game.MainModule.GetType("EFT.UI.ItemUiContext");
        var method = Assert.Single(context.Methods, m => m.Name == "GetItemContextInteractions" && m.Parameters.Count == 2);
        Assert.Equal("itemContext", method.Parameters[0].Name);
        Assert.Contains(context.Fields, f => f.Name == "_inventoryController");
        var interaction = game.MainModule.GetType("EFT.UI.DynamicContextInteraction");
        var ctor = Assert.Single(interaction.Methods, m => m.IsConstructor && m.Parameters.Count == 4);
        Assert.Equal("System.Action", ctor.Parameters[2].ParameterType.FullName);
        var interactions = game.MainModule.GetType("EFT.UI.ContextInteractions`1");
        Assert.Contains(interactions.Fields, f => f.Name == "_dynamicInteractions");
        Assert.NotNull(game.MainModule.GetType("EFT.InventoryLogic.BackEndInventoryController"));
        var status = game.MainModule.GetType("EFT.InGameStatus");
        Assert.True(status.Properties.Any(p => p.Name == "InRaid") || status.Fields.Any(f => f.Name == "InRaid"));
    }

    [GameAssemblyFact]
    public void Native_session_supports_queue_flush_and_custom_operation_callback()
    {
        using var game = Game();
        var session = game.MainModule.GetType("EFT.IClientSession");
        Assert.Contains(session.Methods, m => m.Name == "FlushOperationQueue" && m.Parameters.Count == 0);
        var send = Assert.Single(session.Methods, m => m.Name == "SendOperationRightNow" && m.Parameters.Count == 2);
        Assert.Equal("System.Object", send.Parameters[0].ParameterType.FullName);
        Assert.EndsWith("Callback", send.Parameters[1].ParameterType.FullName);
        var backend = game.MainModule.GetType("EFT.ClientBackendSession");
        Assert.Contains(backend.Properties, p => p.Name == "Profile");
    }

    [GameAssemblyFact]
    public void Item_template_ancestry_and_ids_are_available_without_guessing_obfuscated_fields()
    {
        using var game = Game();
        var item = game.MainModule.GetType("EFT.InventoryLogic.Item");
        Assert.Contains(item.Properties, p => p.Name == "Id");
        Assert.Contains(item.Properties, p => p.Name == "Template");
        Assert.Contains(item.Properties, p => p.Name == "ShortName");
        var template = game.MainModule.GetType("EFT.InventoryLogic.ItemTemplate");
        Assert.True(template.Properties.Any(p => p.Name == "_id") || template.Fields.Any(f => f.Name == "_id"));
        Assert.True(template.Properties.Any(p => p.Name == "Parent") || template.Fields.Any(f => f.Name == "Parent"));
    }

    [GameAssemblyFact]
    public void Enhancement_getters_feed_real_recoil_accuracy_and_ergonomics_calculations()
    {
        using var game = Game();
        var weapon = game.MainModule.GetType("EFT.InventoryLogic.Weapon");
        foreach (var name in new[] { "RecoilDelta", "ErgonomicsTotal", "TotalShotgunDispersion" })
            Assert.Equal("System.Single", weapon.Properties.Single(p => p.Name == name).GetMethod.ReturnType.FullName);
        var firearm = game.MainModule.GetType("EFT.Player").NestedTypes.Single(t => t.Name == "FirearmController");
        AssertCalls(firearm.Methods.Single(m => m.Name == "TotalErgonomicsFunc"), "get_ErgonomicsTotal");
        AssertCalls(firearm.Methods.Single(m => m.Name == "WeaponModified"), "GetTotalCenterOfImpact");
        AssertCalls(firearm.Methods.Single(m => m.Name == "WeaponModified"), "get_TotalShotgunDispersion");
        AssertCalls(game.MainModule.GetType("ShotEffector").Methods.Single(m => m.Name == "RecoilDelta"), "get_RecoilDelta");
    }

    [GameAssemblyFact]
    public void Native_sound_and_gold_item_marker_bindings_exist_in_the_real_game()
    {
        using var game = Game();
        var sounds = game.MainModule.GetType("EFT.UI.GUISounds");
        var play = Assert.Single(sounds.Methods, m => m.Name == "PlayUISound" && m.Parameters.Count == 1);
        Assert.Equal("EFT.UI.EUISoundType", play.Parameters[0].ParameterType.FullName);
        var cues = game.MainModule.GetType("EFT.UI.EUISoundType");
        foreach (var name in new[] { "MenuInstallModFunc", "RepairComplete", "ErrorMessage", "MenuWeaponDisassemble" })
            Assert.Contains(cues.Fields, field => field.Name == name && field.HasConstant);
        var grid = game.MainModule.GetType("EFT.UI.DragAndDrop.GridItemView");
        Assert.Single(grid.Methods, m => m.Name == "GetItemName" && m.Parameters.Count == 1);
        AssertCalls(grid.Methods.Single(m => m.Name == "UpdateInfo" && m.Parameters.Count == 0), "UpdateColor");
        var basic = game.MainModule.GetType("EFT.UI.DragAndDrop.ItemView");
        Assert.Equal("UnityEngine.Color", basic.Fields.Single(f => f.Name == "BackgroundColor").FieldType.FullName);
        Assert.Single(basic.Methods, m => m.Name == "Kill" && m.Parameters.Count == 0);
        Assert.Single(basic.Methods, m => m.Name == "UpdateColor" && m.Parameters.Count == 0);
        // Loading-state checks read BindableState<bool>.Value, whose override has no
        // getter; GameBridge.Get must fall through to BaseBindable<T> (0.3.1 fix).
        Assert.StartsWith("Diz.Binding.BindableState`1",
            basic.Properties.Single(p => p.Name == "IsBeingLoadedAmmo").PropertyType.FullName);
        var state = game.MainModule.GetType("Diz.Binding.BindableState`1");
        Assert.Null(state.Properties.Single(p => p.Name == "Value").GetMethod);
        Assert.NotNull(state.BaseType.Resolve().Properties.Single(p => p.Name == "Value").GetMethod);
        AssertCalls(game.MainModule.GetType("EFT.UI.DragAndDrop.ItemView").Methods.Single(m => m.Name == "Init" && m.Parameters.Count == 0), "UpdateInfo");
    }

    [GameAssemblyFact]
    public void Armor_hooks_cover_actual_durability_subtraction_and_equipment_penalties()
    {
        using var game = Game();
        var armor = game.MainModule.GetType("EFT.InventoryLogic.ArmorComponent");
        var damage = armor.Methods.Single(m => m.Name == "ApplyDurabilityDamage");
        Assert.Equal(2, damage.Parameters.Count);
        Assert.Equal("System.Single", damage.Parameters[0].ParameterType.FullName);
        AssertCalls(armor.Methods.Single(m => m.Name == "ApplyDamage"), "ApplyDurabilityDamage");
        AssertCalls(armor.Methods.Single(m => m.Name == "ApplyExplosionDurabilityDamage"), "ApplyDurabilityDamage");
        var equipment = game.MainModule.GetType("EFT.Player").Methods.Single(m => m.Name == "RecalculateEquipmentParams");
        foreach (var getter in new[] { "get_SpeedPenalty", "get_MousePenalty", "get_WeaponErgonomicPenalty" })
            AssertCalls(equipment, getter);
        var profiles = game.MainModule.GetType("EFT.EftClientBackendSession").Methods.Single(m => m.Name == "GetProfiles");
        Assert.Equal("System.Threading.Tasks.Task", profiles.ReturnType.FullName);
        var icon = game.MainModule.GetType("EFT.UI.DragAndDrop.ItemViewFactory").Methods.Single(m => m.Name == "LoadItemIcon");
        Assert.Equal(3, icon.Parameters.Count);
        Assert.Equal("ItemIcon", icon.ReturnType.FullName);
    }

    private static void AssertCalls(MethodDefinition caller, string target) =>
        Assert.Contains(caller.Body.Instructions, i => i.Operand is MethodReference method && method.Name == target);
}
