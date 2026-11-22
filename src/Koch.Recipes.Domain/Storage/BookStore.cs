using System.Text.Json;
using System.Text.Json.Serialization;
using Koch.Kernel;
using Koch.Recipes.Contracts;
using Koch.Recipes.Domain.Sync;
using Koch.Recipes.Domain.Wire;

namespace Koch.Recipes.Domain.Storage;

/// <summary>The recipe book on the phone, with its sync state. Its shape outlives app versions: see <see cref="BookStore.Schema"/>.</summary>
public sealed record StoredBookJson(int Schema, SyncResponseJson Book, LastSyncJson? Sync, SyncRequestJson? Unconfirmed);

/// <param name="Base">The book as the service last confirmed it.</param>
public sealed record LastSyncJson(DateTimeOffset At, SyncResponseJson Base);

// Nulls are written, not left out: reading requires every constructor parameter, so a line without an amount would
// otherwise come back unreadable (found by the contract test of 2026-11-22).
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(StoredBookJson))]
internal sealed partial class LocalJson : JsonSerializerContext;

/// <summary>
/// Saves the <see cref="SyncedBook"/> as JSON under one key. When the shape changes, <see cref="Schema"/> goes up and a
/// migration from the previous one goes here (as in kassensturz's DataStore). Data this version can't read is kept
/// aside under a second key first, because the app starts fresh and its next save would overwrite it.
/// </summary>
public sealed class BookStore(IKeyValueStore store)
{
    internal const string Key = "koch.recipes";
    internal const string UnreadableKey = "koch.recipes.unreadable";

    /// <summary>
    /// 1 since 2026-11-22; 2 since 2026-11-22, when recipes got a photo; 3 since 2026-11-22, when the unanswered
    /// changes are kept as a protocol-3 request. Older schemas read as they are: no photo, protocol 2 (the merge needs
    /// no bases for unanswered changes, docs/adr/0012).
    /// </summary>
    internal const int Schema = 3;

    public SyncedBook Load()
    {
        if (store.Read(Key) is not { } json) return new SyncedBook();
        if (TryRead(json) is { } book) return book;
        if (store.Read(UnreadableKey) is null) store.Write(UnreadableKey, json);
        return new SyncedBook();
    }

    public void Save(SyncedBook book) => store.Write(Key, Serialize(book));

    public static string Serialize(SyncedBook book) => JsonSerializer.Serialize(
        new StoredBookJson(Schema, book.Book.ToJson(),
            book.State is { } state ? new LastSyncJson(state.LastSync, state.Base.ToJson()) : null,
            book.Unconfirmed?.ToJson()),
        LocalJson.Default.StoredBookJson);

    /// <returns>null if it isn't ours, comes from an unknown version, or breaks the rules.</returns>
    public static SyncedBook? TryRead(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, LocalJson.Default.StoredBookJson) switch
            {
                { Schema: 1 or 2 or Schema } stored => new SyncedBook(stored.Book.ToDomain(),
                    stored.Sync is { } sync ? new SyncState<RecipeBook>(sync.At, sync.Base.ToDomain()) : null,
                    stored.Unconfirmed?.ToDomain()),
                _ => null,
            };
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            return null;
        }
    }
}
