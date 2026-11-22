using System.Text.Json;
using Koch.ServiceDefaults.Documents;
using Koch.Shopping.Contracts;
using Koch.Shopping.Domain;
using Koch.Shopping.Storage;

namespace Koch.Shopping.Sync;

/// <summary>
/// <c>POST /sync</c> (<c>/api/shopping/sync</c> at the gateway) saves a phone's changes to the lists and answers with all
/// of them. Like the Recipes service's endpoint on purpose; the two services share hosting plumbing, not endpoint code,
/// so each can change its protocol alone (docs/adr/0005).
/// </summary>
public static class SyncEndpoint
{
    public static async Task<IResult> Handle(HttpRequest http, IDocumentRows store, CancellationToken cancellationToken)
    {
        // Against cross-site request forgery: a form another site posts can't send JSON, and the gateway allows no CORS.
        if (!http.HasJsonContentType()) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        ShoppingSyncRequestJson? json;
        try
        {
            json = await http.ReadFromJsonAsync(ShoppingJson.Default.ShoppingSyncRequestJson, cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }
        if (json is null) return Results.BadRequest();
        if (json.Protocol != ShoppingSyncRequestJson.CurrentProtocol) return Results.Conflict(); // the app asks to be reloaded

        ShoppingChanges changes;
        try
        {
            changes = json.ToDomain(); // every list and item through the domain's rules
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(); // the app itself never sends these
        }

        var rows = await store.WriteAndReadAsync(Rows.For(changes), cancellationToken);
        return Results.Json(Rows.ToBook(rows).ToJson(), ShoppingJson.Default.ShoppingSyncResponseJson);
    }
}
