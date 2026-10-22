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
}
