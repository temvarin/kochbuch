using System.Text.Json;
using Koch.Recipes.Contracts;
using Koch.Recipes.Domain.Cooking;
using Koch.Recipes.Domain.Sync;
using Koch.Recipes.Domain.Wire;
using Koch.Recipes.Outbox;
using Koch.Recipes.Storage;
using Koch.ServiceDefaults.Documents;

namespace Koch.Recipes.Sync;

/// <summary>
/// <c>POST /sync</c> (<c>/api/recipes/sync</c> at the gateway) saves a phone's changes and answers with the whole book.
/// A changed recipe sent with its base is merged with the stored one part by part; where both phones changed the same
/// part, the request that arrives last wins (docs/adr/0004, 0012).
/// </summary>
public static class SyncEndpoint
{
    public static async Task<IResult> Handle(HttpRequest http, IDocumentRows store, OutboxWakeUp outbox, CancellationToken cancellationToken)
    {
        // Against cross-site request forgery: a form another site posts can't send JSON, and the gateway allows no
        // CORS. The gateway forwards the body untouched, so the check belongs here, with the endpoint.
        if (!http.HasJsonContentType()) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        SyncRequestJson? json;
        try
        {
            json = await http.ReadFromJsonAsync(RecipesJson.Default.SyncRequestJson, cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }
        if (json is null) return Results.BadRequest();
        if (json.Protocol != SyncRequestJson.CurrentProtocol) return Results.Conflict(); // the app asks to be reloaded

        BookChanges changes;
        IReadOnlyList<Recipe> bases;
        try
        {
            changes = json.ToDomain(); // every recipe and ingredient through the domain's rules
            bases = json.BasesToDomain();
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(); // the app itself never sends these
        }

        var rows = await store.WriteAndReadAsync(Rows.For(changes, bases), cancellationToken);
        outbox.Ring(); // photo events saved with the rows go out now, not at the relay's next round
        return Results.Json(Rows.ToBook(rows).ToJson(), RecipesJson.Default.SyncResponseJson);
    }
}
