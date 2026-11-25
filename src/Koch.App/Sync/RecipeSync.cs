using System.Net.Http.Json;
using System.Text.Json;
using Koch.App.Account;
using Koch.App.Shell;
using Koch.Contracts;
using Koch.Domain.Recipes;

namespace Koch.App.Sync;

/// <summary>The recipe book with the Recipes service.</summary>
public sealed class RecipeSync(RecipeState state, AccountService account, HttpClient http, TimeProvider clock)
    : SyncService<RecipeBook, BookChanges>(state, account, http, clock, new Channel())
{
    private sealed class Channel : SyncChannel<RecipeBook, BookChanges>
    {
        public override string Path => "api/recipes/sync";

        // With the bases, so the service can merge with what the other phone changed meanwhile (docs/adr/0012).
        public override HttpContent Request(BookChanges changes, RecipeBook? confirmed) =>
            JsonContent.Create(changes.ToJson(changes.BasesIn(confirmed)), RecipesJson.Default.SyncRequestJson);

        public override async Task<RecipeBook> ReadAnswerAsync(HttpContent content)
        {
            var json = await content.ReadFromJsonAsync(RecipesJson.Default.SyncResponseJson) ?? throw new JsonException("No answer.");
            try
            {
                return json.ToDomain();
            }
            catch (ArgumentException e)
            {
                throw new JsonException("The answer breaks the domain's rules.", e);
            }
        }
    }
}
