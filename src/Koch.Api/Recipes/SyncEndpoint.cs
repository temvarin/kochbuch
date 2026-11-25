using System.Text.Json;
using Koch.Api.Storage;
using Koch.Contracts;
using Koch.Domain.Recipes;

namespace Koch.Api.Recipes;

public static class SyncEndpoint
{
    public static async Task<IResult> Handle(
        HttpRequest http,
        [FromKeyedServices("recipes")] IDocumentRows store,
        CancellationToken cancellationToken)
    {
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
        if (json.Protocol != SyncRequestJson.CurrentProtocol) return Results.Conflict();

        BookChanges changes;
        IReadOnlyList<Recipe> bases;
        try
        {
            changes = json.ToDomain();
            bases = json.BasesToDomain();
        }
        catch (ArgumentException)
        {
            return Results.BadRequest();
        }

        var rows = await store.WriteAndReadAsync(Rows.For(changes, bases), cancellationToken);
        return Results.Json(Rows.ToBook(rows).ToJson(), RecipesJson.Default.SyncResponseJson);
    }
}
