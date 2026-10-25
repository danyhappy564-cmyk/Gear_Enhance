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
        foreach (var name in new[] { "MenuInstallModFunc", "RepairComplete", "ErrorMessage", "MenuWeaponDisassemble", "QuestCompleted", "ButtonClick", "MenuCheckBox", "MenuInspectorWindowOpen", "MenuInspectorWindowClose" })
            Assert.Contains(cues.Fields, field => field.Name == name && field.HasConstant);
        Assert.Equal("UnityEngine.Audio.AudioMixerGroup",
            Assert.Single(sounds.Methods, m => m.Name == "GetCommonSoundsMixerGroup" && m.Parameters.Count == 0).ReturnType.FullName);
        var clipPlay = Assert.Single(sounds.Methods, m => m.Name == "PlaySound" && m.Parameters.Count == 4);
        Assert.Equal(new[] { "UnityEngine.AudioClip", "System.Boolean", "System.Boolean", "System.Single" },
            clipPlay.Parameters.Select(p => p.ParameterType.FullName).ToArray());
        AssertCalls(clipPlay, "PlayOneShot");
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

    [GameAssemblyFact]
    public void Plates_and_visors_sit_in_slots_that_name_their_parent_armor()
    {
        using var game = Game();
        var item = game.MainModule.GetType("EFT.InventoryLogic.Item");
        Assert.Contains(item.Properties, p => p.Name == "CurrentAddress");
        var address = game.MainModule.GetType("EFT.InventoryLogic.ItemAddress");
        Assert.Contains(address.Fields, f => f.Name == "Container");
        var slot = game.MainModule.GetType("EFT.InventoryLogic.Slot");
        Assert.Contains(slot.Properties, p => p.Name == "ParentItem");
        // Armor plate slots are ArmorSlot : Slot; grids (pockets) are not slots.
        Assert.Equal("EFT.InventoryLogic.Slot", game.MainModule.GetType("ArmorSlot").BaseType.FullName);
        for (TypeDefinition? type = game.MainModule.GetType("EFT.InventoryLogic.Grid"); type != null; type = type.BaseType?.Resolve())
            Assert.NotEqual("EFT.InventoryLogic.Slot", type.FullName);
    }

    // 1.3.0 transcendence hooks (TranscendPatches): names, argument counts and the IL the
    // transpilers rewrite, against the real game. Whether the JIT inlines a getter is seen in game only.
    [GameAssemblyFact]
    public void Transcend_recoil_hooks_rebuild_the_backward_kick_and_angle_range()
    {
        using var game = Game();
        var shot = game.MainModule.GetType("ShotEffector");
        AssertCalls(shot.Methods.Single(m => m.Name == "OnWeaponParametersChanged" && m.Parameters.Count == 0), "RecalculateRecoilParamsOnChangeWeapon");
        AssertCalls(shot.Methods.Single(m => m.Name == "SubscribeToWeapon" && m.Parameters.Count == 4), "CalculateBaseRecoilParameters");
        AssertCalls(shot.Methods.Single(m => m.Name == "CalculateRecoilParameters" && m.Parameters.Count == 0), "CalculateBaseRecoilParameters");
        Assert.Contains(shot.Fields, f => f.Name == "_weapon");
        Assert.Contains(shot.Properties, p => p.Name == "CurrentRecoilEffect");
        Assert.Contains(game.MainModule.GetType("EFT.InventoryLogic.IWeapon").Properties, p => p.Name == "Item");
        // Weapon implements it explicitly, so TranscendPatches.ShotWeapon falls back to the weapon itself.
        Assert.DoesNotContain(game.MainModule.GetType("EFT.InventoryLogic.Weapon").Properties, p => p.Name == "Item");
        Assert.Contains(game.MainModule.GetType("EFT.InventoryLogic.Weapon").Properties, p => p.Name == "EFT.InventoryLogic.IWeapon.Item");
        // FirearmController.Item (malfunction hook) is a plain property.
        Assert.Contains(game.MainModule.GetType("EFT.Player").NestedTypes.Single(t => t.Name == "FirearmController").Properties, p => p.Name == "Item");
        foreach (var name in new[] { "EFT.Animations.NewRecoil.NewRecoilShotEffect", "EFT.Animations.OldRecoilShotEffect" })
        {
            var effect = game.MainModule.GetType(name);
            foreach (var field in new[] { "BasicPlayerRecoilPositionStrength", "BasicPlayerRecoilDegreeRange", "BasicRecoilRadian" })
                Assert.Equal("UnityEngine.Vector2", effect.Fields.Single(f => f.Name == field).FieldType.FullName);
        }
        // The backward kick is written only on weapon change, from the template force.
        var recalc = game.MainModule.GetType("EFT.Animations.NewRecoil.NewRecoilShotEffect").Methods.Single(m => m.Name == "RecalculateRecoilParamsOnChangeWeapon");
        Assert.Contains(recalc.Body.Instructions, i => i.OpCode.Code == Mono.Cecil.Cil.Code.Stfld && i.Operand is FieldReference f && f.Name == "BasicPlayerRecoilPositionStrength");
    }

    [GameAssemblyFact]
    public void Transcend_weapon_hooks_fire_rate_overheat_malfunction_and_display_exist()
    {
        using var game = Game();
        var weapon = game.MainModule.GetType("EFT.InventoryLogic.Weapon");
        Assert.Equal("System.Int32", weapon.Properties.Single(p => p.Name == "FireRate").GetMethod.ReturnType.FullName);
        Assert.Contains(weapon.Properties, p => p.Name == "WeapFireType");
        var modes = weapon.NestedTypes.Single(t => t.Name == "EFireMode");
        Assert.Contains(modes.Fields, f => f.Name == "fullauto");
        Assert.Contains(modes.Fields, f => f.Name == "burst");
        var overheat = Assert.Single(weapon.Methods, m => m.Name == "GetShotOverheat" && m.Parameters.Count == 3);
        Assert.Equal("System.Single", overheat.ReturnType.FullName);
        var firearm = game.MainModule.GetType("EFT.Player").NestedTypes.Single(t => t.Name == "FirearmController");
        var malfunction = Assert.Single(firearm.Methods, m => m.Name == "GetTotalMalfunctionChance" && m.Parameters.Count == 7);
        Assert.Equal("System.Single", malfunction.ReturnType.FullName);
        Assert.Equal(new[] { "ammoToFire", "overheat", "durabilityMalfChance", "magMalfChance", "ammoMalfChance", "overheatMalfChance", "weaponDurability" },
            malfunction.Parameters.Select(p => p.Name).ToArray());
        Assert.Equal(new[] { "System.Double&", "System.Single&", "System.Single&", "System.Single&" },
            malfunction.Parameters.Skip(2).Take(4).Select(p => p.ParameterType.FullName).ToArray());
        // Shots read the getter (automatic fire interval) rather than the template field. The
        // getter is inlined in game, so TranscendPatches rewrites these reads in the callers.
        var automatic = firearm.NestedTypes.Single(t => t.Name == "AutomaticFireOperation");
        AssertCalls(automatic.Methods.Single(m => m.IsConstructor && !m.IsStatic), "get_FireRate");
        AssertCalls(automatic.Methods.Single(m => m.Name == "Update"), "get_FireRate");
        var rate = weapon.Properties.Single(p => p.Name == "FireRate").GetMethod;
        Assert.True(rate.Body.CodeSize < 20, "FireRate getter is no longer tiny: " + rate.Body.CodeSize);
        var ctor = Assert.Single(weapon.Methods, m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 2 && m.Parameters[0].ParameterType.FullName == "System.String");
        Assert.Contains(ctor.Body.Instructions, i => i.Operand is MethodReference r && r.DeclaringType.Name == "RangedItemAttribute" && r.Name == ".ctor");
        var ids = game.MainModule.GetType("EFT.InventoryLogic.EItemAttributeId");
        Assert.Contains(ids.Fields, f => f.Name == "RecoilBack");
        Assert.Contains(ids.Fields, f => f.Name == "FireRate");
        var attribute = game.MainModule.GetType("EFT.InventoryLogic.ItemAttribute");
        foreach (var field in new[] { "Id", "Base", "StringValue" }) Assert.Contains(attribute.Fields, f => f.Name == field);
        Assert.Contains(game.MainModule.GetType("EFT.InventoryLogic.RangedItemAttribute").Fields, f => f.Name == "Delta");
        Assert.Contains(game.MainModule.GetType("EFT.InventoryLogic.Item").Fields, f => f.Name == "Attributes");
    }

    [GameAssemblyFact]
    public void Transcend_armor_hooks_read_one_blunt_value_and_the_class_getter()
    {
        using var game = Game();
        var armor = game.MainModule.GetType("EFT.InventoryLogic.ArmorComponent");
        Assert.Equal("System.Int32", armor.Properties.Single(p => p.Name == "ArmorClass").GetMethod.ReturnType.FullName);
        var damage = Assert.Single(armor.Methods, m => m.Name == "ApplyDamage" && m.Parameters.Count == 7);
        AssertCalls(damage, "get_ArmorClass");
        AssertCalls(Assert.Single(armor.Methods, m => m.Name == "SetPenetrationStatus"), "get_ArmorClass");
        AssertCalls(game.MainModule.GetType("EFT.Player").Methods.Single(m => m.Name == "TryGetArmorResistData"), "get_ArmorClass");
        foreach (var name in new[] { "EFT.InventoryLogic.ArmorHolderComponent", "EFT.InventoryLogic.ArmorPlatesFormatter", "EFT.UI.MultiLineInfo" })
            Assert.NotNull(game.MainModule.GetType(name));
        var code = damage.Body.Instructions.ToList();
        static bool Reads(Mono.Cecil.Cil.Instruction i, string name) => i.Operand is FieldReference f && f.Name == name && i.OpCode.Code == Mono.Cecil.Cil.Code.Ldfld
            || i.Operand is MethodReference m && m.Name == "get_" + name;
        Assert.Equal(1, Enumerable.Range(0, code.Count - 1).Count(n => Reads(code[n], "Template") && Reads(code[n + 1], "BluntThroughput")));
        Assert.Contains(armor.Fields.Concat(armor.BaseType.Resolve().Fields), f => f.Name == "Template");
        // Templates implement the class explicitly; TranscendPatches.TemplateClass reads it via the interface.
        Assert.Contains(game.MainModule.GetType("EFT.InventoryLogic.IArmorComponentTemplate").Properties, p => p.Name == "ArmorClass");
        foreach (var name in new[] { "EFT.InventoryLogic.VestTemplate", "EFT.InventoryLogic.ArmoredEquipmentTemplate" })
        {
            var template = game.MainModule.GetType(name);
            Assert.Contains(template.Properties, p => p.Name == "EFT.InventoryLogic.IArmorComponentTemplate.ArmorClass");
            Assert.Contains(template.Fields, f => f.Name == "armorClass");
        }
    }

    // 1.3.0 plate auto-register: the slot insert hook, the item event sender and the notice.
    [GameAssemblyFact]
    public void Plate_insert_hook_sender_and_notice_exist()
    {
        using var game = Game();
        var address = game.MainModule.GetType("EFT.InventoryLogic.SlotItemAddress");
        var raise = Assert.Single(address.Methods, m => m.Name == "RaiseAddEvent" && m.Parameters.Count == 3);
        Assert.Equal(new[] { "EFT.InventoryLogic.Item", "EFT.InventoryLogic.CommandStatus" },
            raise.Parameters.Take(2).Select(p => p.ParameterType.FullName).ToArray());
        Assert.Contains(game.MainModule.GetType("EFT.InventoryLogic.CommandStatus").Fields, f => f.Name == "Succeed");
        Assert.Contains(address.Fields, f => f.Name == "Slot");
        Assert.Contains(game.MainModule.GetType("EFT.InventoryLogic.Slot").Properties, p => p.Name == "ParentItem");
        var session = game.MainModule.GetType("EFT.ClientBackendSession");
        Assert.Single(session.Methods, m => m.Name == "SendOperationRightNow" && m.Parameters.Count == 2);
        Assert.Single(session.Methods, m => m.Name == "FlushOperationQueue" && m.Parameters.Count == 0);
        var notice = Assert.Single(game.MainModule.GetType("EFT.Communications.NotificationManager").Methods,
            m => m.Name == "DisplayWarningNotification" && m.Parameters.Count == 2);
        Assert.Equal("System.String", notice.Parameters[0].ParameterType.FullName);
        Assert.True(notice.Parameters[1].IsOptional);
    }

    [GameAssemblyFact]
    public void Transcend_trader_price_reads_repairable_fields()
    {
        using var game = Game();
        var price = game.MainModule.GetType("EFT.Trading.PriceCalculator");
        var repairable = game.MainModule.GetType("EFT.InventoryLogic.RepairableComponent");
        Assert.Equal("System.Single", repairable.Fields.Single(f => f.Name == "MaxDurability").FieldType.FullName);
        Assert.Equal("System.Single", repairable.Fields.Single(f => f.Name == "Durability").FieldType.FullName);
        Assert.Contains(game.MainModule.GetType("EFT.InventoryLogic.ItemComponent").Fields, f => f.Name == "Item");
        foreach (var (name, count) in new[] { ("CalculateBasePriceForSingleItem", 3), ("CalculateBuyoutBasePriceForSingleItem", 4) })
        {
            var method = Assert.Single(price.Methods, m => m.Name == name && m.Parameters.Count == count);
            foreach (var field in new[] { "MaxDurability", "Durability" })
                Assert.Contains(method.Body.Instructions, i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldfld && i.Operand is FieldReference f
                    && f.Name == field && f.DeclaringType.FullName == "EFT.InventoryLogic.RepairableComponent");
        }
    }

    // 1.4.0 mythic hooks (MythicPatches): every hooked method is large or virtual, so the JIT
    // cannot inline it into its callers (kb 2.61), and the members read by name exist as named.
    [GameAssemblyFact]
    public void Mythic_hooks_bind_to_real_members_that_are_not_inlined()
    {
        using var game = Game();
        var calculator = game.MainModule.GetType("EFT.Ballistics.BallisticsCalculator");
        var create = Assert.Single(calculator.Methods, m => m.Name == "CreateShot" && m.Parameters.Count == 8);
        Assert.Equal(["ammo", "origin", "direction", "fireIndex", "player", "weapon", "speedFactor", "fragmentIndex"], create.Parameters.Select(p => p.Name));
        Assert.Equal("System.String", create.Parameters[4].ParameterType.FullName);
        Assert.Equal("EFT.Ballistics.Shot", create.ReturnType.FullName);
        Assert.True(create.Body.CodeSize >= 20);
        var shot = game.MainModule.GetType("EFT.Ballistics.Shot");
        foreach (var field in new[] { "Damage", "PenetrationPower" })
            Assert.Equal("System.Single", shot.Fields.Single(f => f.Name == field).FieldType.FullName);
        var armor = game.MainModule.GetType("EFT.InventoryLogic.ArmorComponent");
        var matches = Assert.Single(armor.Methods, m => m.Name == "ShotMatches" && m.Parameters.Count == 2);
        Assert.True(matches.IsVirtual);
        Assert.Equal("EBodyPartColliderType", matches.Parameters[0].ParameterType.FullName);
        Assert.Contains(armor.Properties, p => p.Name == "ArmorPlateCollidersMask");
        var health = game.MainModule.GetType("EFT.HealthSystem.ActiveHealthController");
        var damage = Assert.Single(health.Methods, m => m.Name == "ApplyDamage" && m.Parameters.Count == 3);
        Assert.Equal(["bodyPart", "damage", "damageInfo"], damage.Parameters.Select(p => p.Name));
        Assert.Equal("System.Single", damage.Parameters[1].ParameterType.FullName);
        Assert.True(damage.Body.CodeSize >= 20);
        Assert.Contains(health.Fields, f => f.Name == "Player");
        var info = game.MainModule.GetType(damage.Parameters[2].ParameterType.FullName);
        Assert.True(info.Fields.Any(f => f.Name == "DamageType") || info.Properties.Any(p => p.Name == "DamageType"));
        // EDamageType flags the reduction counts as hits (MythicRules.HitDamageTypes).
        var types = game.MainModule.GetType("EFT.EDamageType").Fields.Where(f => f.HasConstant).ToDictionary(f => f.Name, f => (int)f.Constant);
        var hits = new[] { "Explosion", "GrenadeFragment", "Bullet", "Melee", "Landmine", "Sniper", "Blunt", "Btr", "Artillery", "ThermobaricExplosion" };
        Assert.Equal(GearEnhance.MythicRules.HitDamageTypes, hits.Sum(name => types[name]));
        Assert.Equal(2, types["Fall"]);
        // Owner root is an interface property: read through IItemOwner, never by name (kb 2.60).
        Assert.Contains(game.MainModule.GetType("EFT.InventoryLogic.Item").Properties, p => p.Name == "Owner");
        Assert.Contains(game.MainModule.GetType("EFT.InventoryLogic.IItemOwner").Properties, p => p.Name == "RootItem");
        Assert.Contains(game.MainModule.GetType("EFT.GameWorld").Fields, f => f.Name == "MainPlayer");
        var player = game.MainModule.GetType("EFT.Player");
        foreach (var name in new[] { "ProfileId", "IsYourPlayer", "Inventory" }) Assert.Contains(player.Properties, p => p.Name == name);
        Assert.Contains(game.MainModule.GetType("EFT.InventoryLogic.Inventory").Fields, f => f.Name == "Equipment");
        Assert.Single(game.MainModule.GetType("EFT.InventoryLogic.InventoryEquipment").Methods, m => m.Name == "GetSlot" && m.Parameters.Count == 1);
        var slots = game.MainModule.GetType("EFT.InventoryLogic.EquipmentSlot").Fields.Select(f => f.Name).ToHashSet();
        foreach (var name in new[] { "ArmorVest", "TacticalVest", "Headwear", "FaceCover", "Eyewear" }) Assert.Contains(name, slots);
        // Zone names the server draws are real collider names.
        var colliders = game.MainModule.GetType("EBodyPartColliderType").Fields.Select(f => f.Name).ToHashSet();
        foreach (var zone in GearEnhance.MythicRules.BodyZones.Concat(GearEnhance.MythicRules.HeadZones)) Assert.Contains(zone, colliders);
    }

    // 1.4.0 status panel reads (Plugin.Status.cs) by these names.
    [GameAssemblyFact]
    public void Status_panel_reads_existing_members()
    {
        using var game = Game();
        var template = game.MainModule.GetType("EFT.InventoryLogic.WeaponTemplate");
        foreach (var field in new[] { "RecoilForceUp", "RecoilForceBack" }) Assert.Contains(template.Fields, f => f.Name == field);
        var weapon = game.MainModule.GetType("EFT.InventoryLogic.Weapon");
        foreach (var name in new[] { "RecoilDelta", "ErgonomicsTotal", "FireRate", "WeapFireType", "Template" }) Assert.Contains(weapon.Properties, p => p.Name == name);
        Assert.Single(weapon.Methods, m => m.Name == "GetTotalCenterOfImpact" && m.Parameters.Count == 1);
        var armor = game.MainModule.GetType("EFT.InventoryLogic.ArmorComponent");
        foreach (var name in new[] { "ArmorClass", "SpeedPenalty", "MousePenalty", "WeaponErgonomicPenalty", "ArmorColliders" })
            Assert.Contains(armor.Properties, p => p.Name == name);
        Assert.Contains(game.MainModule.GetType("EFT.InventoryLogic.RepairableComponent").Properties, p => p.Name == "TemplateDurability");
        Assert.Contains(game.MainModule.GetType("EFT.InventoryLogic.Item").Fields, f => f.Name == "Components");
        Assert.Contains(game.MainModule.GetType("EFT.InventoryLogic.CompoundItem").Properties, p => p.Name == "AllSlots");
        Assert.Contains(game.MainModule.GetType("EFT.InventoryLogic.Slot").Properties, p => p.Name == "ContainedItem");
    }

    private static void AssertCalls(MethodDefinition caller, string target) =>
        Assert.Contains(caller.Body.Instructions, i => i.Operand is MethodReference method && method.Name == target);
}
