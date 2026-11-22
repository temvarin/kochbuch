using System.Net;
using System.Net.Http.Json;
using Koch.Gateway.Contracts;
using Koch.Recipes.Contracts;
using Koch.ServiceDefaults.Documents;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Koch.Recipes.Tests;

/// <summary>The service as the gateway calls it: inside the environment, with the member in a header.</summary>
public class ApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private sealed class FakeDatabase(bool readable) : IDocumentRows
    {
        public Task<bool> CanReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(readable);

        public Task<IReadOnlyList<StoredRow>> WriteAndReadAsync(IReadOnlyList<RowWrite> writes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredRow>>([]);
    }

    private HttpClient Service(bool databaseReadable = true) => factory.WithWebHostBuilder(host =>
    {
        host.UseSetting("ConnectionStrings:Recipes", "Server=unused");
        host.ConfigureTestServices(services => services.AddSingleton<IDocumentRows>(new FakeDatabase(databaseReadable)));
    }).CreateClient();

    private static Task<HttpResponseMessage> Get(HttpClient service, string path, string? user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (user is not null) request.Headers.Add(ForwardedUser.Header, user);
        return service.SendAsync(request);
    }

    [Fact]
    public async Task A_request_that_did_not_come_through_the_gateway_is_refused()
    {
        var response = await Get(Service(), "/status", user: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Status_names_the_forwarded_member_and_whether_the_database_answers(bool readable)
    {
        var response = await Get(Service(readable), "/status", "member-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new RecipesStatus("member-1", readable),
            await response.Content.ReadFromJsonAsync(RecipesJson.Default.RecipesStatus));
    }

    [Fact]
    public async Task The_health_probe_needs_no_user()
    {
        var response = await Get(Service(), "/healthz", user: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
