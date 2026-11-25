using System.Net.Http.Json;
using System.Text.Json;
using Koch.App.Account;
using Koch.App.Shell;
using Koch.Contracts;
using Koch.Domain.Shopping;

namespace Koch.App.Sync;

/// <summary>The shopping lists with the Shopping service.</summary>
public sealed class ShoppingSync(ShoppingState state, AccountService account, HttpClient http, TimeProvider clock)
    : SyncService<ShoppingBook, ShoppingChanges>(state, account, http, clock, new Channel())
{
    private sealed class Channel : SyncChannel<ShoppingBook, ShoppingChanges>
    {
        public override string Path => "api/shopping/sync";

        public override HttpContent Request(ShoppingChanges changes, ShoppingBook? confirmed) =>
            JsonContent.Create(changes.ToJson(), ShoppingJson.Default.ShoppingSyncRequestJson);

        public override async Task<ShoppingBook> ReadAnswerAsync(HttpContent content)
        {
            var json = await content.ReadFromJsonAsync(ShoppingJson.Default.ShoppingSyncResponseJson) ?? throw new JsonException("No answer.");
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
