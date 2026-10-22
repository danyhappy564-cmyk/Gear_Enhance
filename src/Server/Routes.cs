using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace GearEnhance;

public sealed class QuoteRequest : IRequestData
{
    public int ProtocolVersion { get; set; }
    public string ItemId { get; set; } = "";
    public EnhanceSettings Settings { get; set; } = new();
    public EnhancePath? Path { get; set; }
    public EnhanceProtection Protection { get; set; } = new();
    public EnhanceAction Action { get; set; }
}

public sealed class ReceiptRequest : IRequestData
{
    public int ProtocolVersion { get; set; }
    public string QuoteId { get; set; } = "";
}

public sealed class StateRequest : IRequestData
{
    public int ProtocolVersion { get; set; }
}

public sealed record EnhanceOperation : BaseInteractionRequestData
{
    [JsonPropertyName("quoteId")]
    public string QuoteId { get; set; } = "";
    [JsonPropertyName("confirmDestruction")]
    public bool ConfirmDestruction { get; set; }
}

[Injectable]
public sealed class EnhanceQueries(JsonUtil json, EnhanceService service) : StaticRouter(json,
[
    new RouteAction<QuoteRequest>("/rf/gear-enhance/quote", (_, body, session, _, _) =>
        ValueTask.FromResult(Reply(() => { EnhanceProtocol.Validate(body.ProtocolVersion);
            return service.Quote(session, body.ItemId, body.Settings, body.Path, body.Protection, body.Action); }, json))),
    new RouteAction<StateRequest>("/rf/gear-enhance/state", (_, body, session, _, _) =>
        ValueTask.FromResult(Reply(() => { EnhanceProtocol.Validate(body.ProtocolVersion); return service.Snapshot(session); }, json))),
    new RouteAction<ReceiptRequest>("/rf/gear-enhance/receipt", (_, body, session, _, _) =>
        ValueTask.FromResult(Reply(() => { EnhanceProtocol.Validate(body.ProtocolVersion);
            return service.Receipt(session, body.QuoteId)
            ?? throw new InvalidOperationException("완료 기록이 없습니다. 연결 상태와 서버 로그를 확인해 주세요."); }, json)))
])
{
    private static string Reply<T>(Func<T> action, JsonUtil json) where T : class
    {
        ApiReply<T> response;
        try { response = new() { Ok = true, Data = action() }; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { response = new() { Error = ex.Message }; }
        return json.Serialize(response)!;
    }
}

[Injectable]
public sealed class EnhanceEvents(EnhanceService service, HttpResponseUtil responses) : ItemEventRouter(
[
    new ItemRouteAction<EnhanceOperation>("GearEnhance", (_, profile, body, session, output, _) =>
    {
        try { service.Execute(session, profile, body.QuoteId, body.ConfirmDestruction, output); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { responses.AppendErrorToOutput(output, ex.Message); }
        return ValueTask.FromResult(output);
    })
]);
