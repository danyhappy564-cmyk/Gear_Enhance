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
    public bool ChanceBoost { get; set; }
    public InsuranceTier Tier { get; set; }
    public TranscendTrack Track { get; set; }
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
            return service.Quote(session, body.ItemId, body.Settings, body.Path, body.Protection, body.Action, body.ChanceBoost, body.Tier, body.Track); }, json))),
    new RouteAction<StateRequest>("/rf/gear-enhance/state", (_, body, session, _, _) =>
        ValueTask.FromResult(Reply(() => { EnhanceProtocol.Validate(body.ProtocolVersion); return service.Snapshot(session); }, json))),
    new RouteAction<ReceiptRequest>("/rf/gear-enhance/receipt", (_, body, session, _, _) =>
        ValueTask.FromResult(Reply(() => { EnhanceProtocol.Validate(body.ProtocolVersion);
            return service.Receipt(session, body.QuoteId)
            ?? throw Loc.Invalid("완료 기록이 없습니다. 연결 상태와 서버 로그를 확인해 주세요.", "No completion record. Check the connection and server log."); }, json)))
])
{
    private static string Reply<T>(Func<T> action, JsonUtil json) where T : class
    {
        ApiReply<T> response;
        try { response = new() { Ok = true, Data = action() }; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { response = new() { Error = ex.Message, ErrorEnglish = Loc.EnglishOf(ex) ?? ex.Message }; }
        // Quote/state/receipt never pay. Report anything else as text so the
        // client shows and logs the cause instead of an unreadable response.
        catch (Exception ex)
        {
            response = new() { Error = "Gear Enhance 서버 오류(" + ex.GetType().Name + "): " + ex.Message,
                ErrorEnglish = "Gear Enhance server error (" + ex.GetType().Name + "): " + ex.Message };
        }
        return json.Serialize(response)!;
    }
}

[Injectable]
public sealed class EnhanceEvents(EnhanceService service, HttpResponseUtil responses) : ItemEventRouter(
[
    new ItemRouteAction<EnhanceOperation>("GearEnhance", (_, profile, body, session, output, _) =>
    {
        try { service.Execute(session, profile, body.QuoteId, body.ConfirmDestruction, output); }
        // The native error popup has no language channel, so show both texts.
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { responses.AppendErrorToOutput(output, Loc.EnglishOf(ex) is { } english ? ex.Message + "\n" + english : ex.Message); }
        return ValueTask.FromResult(output);
    })
]);
