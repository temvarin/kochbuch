using System.Text.Json;
using Koch.Api.Storage;
using Koch.Contracts;
using Koch.Domain.Shopping;

namespace Koch.Api.Shopping;

public static class SyncEndpoint
{
    public static async Task<IResult> Handle(
        HttpRequest http,
        [FromKeyedServices("shopping")] IDocumentRows store,
        CancellationToken cancellationToken)
    {
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
        if (json.Protocol != ShoppingSyncRequestJson.CurrentProtocol) return Results.Conflict();

        ShoppingChanges changes;
        try
        {
            changes = json.ToDomain();
        }
        catch (ArgumentException)
        {
            return Results.BadRequest();
        }

        var rows = await store.WriteAndReadAsync(Rows.For(changes), cancellationToken);
        return Results.Json(Rows.ToBook(rows).ToJson(), ShoppingJson.Default.ShoppingSyncResponseJson);
    }
}
