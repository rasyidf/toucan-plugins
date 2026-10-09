using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Toucan.Connector.Rest.Tests;

/// <summary>The service protocol in memory: bearer auth, revisions, and 412 on a stale If-Match.</summary>
internal sealed class FakeServer : HttpMessageHandler
{
    public const string Token = "tok-123";
    public Dictionary<(string Key, string Language), RemoteUnit> Units { get; } = [];
    public List<string> Requests { get; } = [];
    public HttpStatusCode? FailWith { get; set; }

    /// <summary>Runs once, right after the next list request is answered: what happens on the server while the connector works.</summary>
    public Action? OnNextRead { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
        if (request.Headers.Authorization?.Parameter != Token) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        if (FailWith is { } status) return new HttpResponseMessage(status);

        var segments = request.RequestUri.AbsolutePath.Trim('/').Split('/');
        if (request.Method == HttpMethod.Get && segments[^1] == "translations")
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Units.Values.ToList()) };
            var after = OnNextRead;
            OnNextRead = null;
            after?.Invoke();
            return response;
        }

        if (request.Method == HttpMethod.Put && segments.Length >= 3)
        {
            var key = Uri.UnescapeDataString(segments[^2]);
            var language = Uri.UnescapeDataString(segments[^1]);
            var expected = long.Parse(request.Headers.IfMatch.Single().Tag.Trim('"'));
            var current = Units.TryGetValue((key, language), out var u) ? u.Revision : 0;
            if (current != expected) return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.GetProperty("text").GetString()!;
            Units[(key, language)] = new RemoteUnit(key, language, body, current + 1);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public void Seed(string key, string language, string text, long revision) => Units[(key, language)] = new RemoteUnit(key, language, text, revision);
}
