using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using HarmonyLib;
using Newtonsoft.Json;

namespace GearEnhance;

// Runtime bindings are confined here. No game DLLs, stubs, certificate bypasses,
// or copies of another mod's interaction implementation are shipped.
internal static class GameBridge
{
    private static readonly ConcurrentDictionary<(Type, string), MemberInfo> members = new ConcurrentDictionary<(Type, string), MemberInfo>();
    internal static object Get(object instance, string name)
    {
        if (instance == null) return null;
        var type = instance.GetType();
        if (!members.TryGetValue((type, name), out var member))
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            // Standard reflection also works in the .NET test host; Harmony's
            // runtime helpers are only needed when installing patches in Mono.
            for (var current = type; current != null && member == null; current = current.BaseType)
                member = (MemberInfo)current.GetProperty(name, flags) ?? current.GetField(name, flags);
            if (member != null) members.TryAdd((type, name), member);
        }
        return member is PropertyInfo property ? property.GetValue(instance)
            : member is FieldInfo field ? field.GetValue(instance) : null;
    }

    internal static MethodInfo RequiredMethod(Type type, string name, int count) =>
        type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .SingleOrDefault(m => m.Name == name && m.GetParameters().Length == count)
        ?? throw new MissingMethodException(type?.FullName, name);

    internal static bool InStash(object context)
    {
        var status = AccessTools.TypeByName("EFT.InGameStatus");
        var inRaid = status == null ? null : AccessTools.Property(status, "InRaid")?.GetValue(null)
            ?? AccessTools.Field(status, "InRaid")?.GetValue(null);
        return inRaid is false && AccessTools.TypeByName("EFT.InventoryLogic.BackEndInventoryController")
            ?.IsInstanceOfType(Get(context, "_inventoryController")) == true;
    }

    internal static bool Eligible(object item)
    {
        // Read the game's already-linked template ancestry. Cycles are bounded.
        var template = Get(item, "Template");
        var seen = new System.Collections.Generic.HashSet<object>();
        var bases = new[] { "5422acb9af1c889c16000029", "5448e54d4bdc2dcc718b4568",
            "5448e5284bdc2dcb718b4567", "5a341c4086f77401f2541505",
            "57bef4c42459772e8d35a53b", "5a341c4686f77469e155819e", "5448e5724bdc2ddf718b4568" };
        for (var n = 0; template != null && n < 64 && seen.Add(template); n++)
        {
            if (bases.Contains(Get(template, "_id")?.ToString())) return true;
            template = Get(template, "Parent");
        }
        return false;
    }

    internal static bool IsWeapon(object item) => AccessTools.TypeByName("EFT.InventoryLogic.Weapon")?.IsInstanceOfType(item) == true;
    internal static string ProfileId(object session) => Get(Get(session, "Profile"), "Id")?.ToString() ?? "";

    internal static object ParentItem(object item)
    {
        var address = Get(item, "CurrentAddress");
        return Get(Get(address, "Container"), "ParentItem");
    }

    internal static void RefreshGameplay()
    {
        var worldType = AccessTools.TypeByName("EFT.GameWorld");
        if (worldType == null) return;
        var singleton = AccessTools.TypeByName("Comfort.Common.Singleton`1")?.MakeGenericType(worldType);
        var world = singleton == null ? null : AccessTools.Property(singleton, "Instance")?.GetValue(null);
        var player = Get(world, "MainPlayer");
        if (player == null) return;
        RequiredMethod(player.GetType(), "RecalculateEquipmentParams", 0).Invoke(player, null);
        var hands = Get(player, "HandsController");
        if (hands != null && AccessTools.TypeByName("EFT.Player+FirearmController")?.IsInstanceOfType(hands) == true)
            RequiredMethod(hands.GetType(), "WeaponModified", 0).Invoke(hands, null);
        var shot = Get(Get(player, "ProceduralWeaponAnimation"), "Shootingg");
        if (shot != null) RequiredMethod(shot.GetType(), "OnWeaponParametersChanged", 0).Invoke(shot, null);
    }

    internal static void AddInteraction(object interactions, Action action)
    {
        var type = AccessTools.TypeByName("EFT.UI.DynamicContextInteraction")
            ?? throw new TypeLoadException("SPT 4.1 DynamicContextInteraction");
        var ctor = type.GetConstructors().Single(c => c.GetParameters().Length == 4);
        const string caption = "장비 강화";
        var interaction = ctor.Invoke(new object[] { "rf.gear.enhance", caption, action, null });
        var dictionary = Get(interactions, "_dynamicInteractions") as IDictionary
            ?? throw new MissingFieldException("ContextInteractions._dynamicInteractions");
        dictionary[caption] = interaction;
    }

    internal static async Task Flush(object session)
    {
        var task = (Task)RequiredMethod(session.GetType(), "FlushOperationQueue", 0).Invoke(session, null);
        if (await Task.WhenAny(task, Task.Delay(15000)) != task)
            throw new TimeoutException("기존 인벤토리 작업의 응답 시간이 초과됐습니다.");
        await task;
        var result = Get(task, "Result");
        if (!Succeeded(result)) throw new InvalidOperationException("기존 인벤토리 작업을 완료하지 못했습니다.");
    }

    internal static Task<bool> Execute(object session, string quoteId, bool destruction)
    {
        var method = RequiredMethod(session.GetType(), "SendOperationRightNow", 2);
        var completion = new CallbackCompletion();
        var callback = Delegate.CreateDelegate(method.GetParameters()[1].ParameterType, completion,
            typeof(CallbackCompletion).GetMethod(nameof(CallbackCompletion.Complete)));
        method.Invoke(session, new object[] {
            new { Action = "GearEnhance", quoteId, confirmDestruction = destruction }, callback });
        return completion.Source.Task;
    }

    private sealed class CallbackCompletion
    {
        internal readonly TaskCompletionSource<bool> Source = new TaskCompletionSource<bool>();
        public void Complete(object result) => Source.TrySetResult(Succeeded(result));
    }

    private static bool Succeeded(object result)
    {
        if (Get(result, "Succeed") is bool success) return success;
        var contract = result?.GetType().GetInterfaces().FirstOrDefault(i => i.Name == "IResult");
        return contract?.GetProperty("Succeed")?.GetValue(result) is bool value && value;
    }

    internal static async Task<T> Query<T>(string path, object body) where T : class
    {
        var json = JsonConvert.SerializeObject(body);
        var raw = await Task.Run(() =>
        {
            var type = AccessTools.TypeByName("SPT.Common.Http.RequestHandler")
                ?? throw new TypeLoadException("SPT.Common.Http.RequestHandler가 없습니다.");
            var method = type.GetMethods(BindingFlags.Public | BindingFlags.Static).SingleOrDefault(m =>
                m.Name == "PostJson" && m.GetParameters().Length >= 2
                && m.GetParameters()[0].ParameterType == typeof(string)
                && m.GetParameters()[1].ParameterType == typeof(string)
                && m.GetParameters().Skip(2).All(p => p.IsOptional))
                ?? throw new MissingMethodException("SPT RequestHandler.PostJson");
            var args = method.GetParameters().Select(p => p.DefaultValue).ToArray();
            args[0] = path; args[1] = json;
            return method.Invoke(null, args) as string;
        });
        var response = JsonConvert.DeserializeObject<ApiReply<T>>(raw ?? "");
        if (response?.Ok != true || response.Data == null)
            throw new InvalidOperationException(response?.Error ?? "서버 응답을 읽을 수 없습니다.");
        return response.Data;
    }

    internal static async Task<T> WithTimeout<T>(Task<T> task, int milliseconds = 15000)
    {
        if (await Task.WhenAny(task, Task.Delay(milliseconds)) != task)
            throw new TimeoutException("서버 응답 시간이 초과됐습니다.");
        return await task;
    }
}
