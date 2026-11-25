using System.Net;
using Yarp.ReverseProxy.Forwarder;

namespace Koch.Services.Tests;

public sealed record ReceivedRequest(string Url, IReadOnlyDictionary<string, string[]> Headers)
{
    public string[] Header(string name) =>
        Headers.FirstOrDefault(header => header.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value ?? [];
}

public sealed class FakeServices : HttpMessageHandler, IForwarderHttpClientFactory
{
    private readonly List<ReceivedRequest> received = [];

    public IReadOnlyList<ReceivedRequest> Received
    {
        get { lock (received) return [.. received]; }
    }

    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(this, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(header => header.Key, header => header.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
        lock (received) received.Add(new ReceivedRequest(request.RequestUri!.ToString(), headers));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        });
    }
}
