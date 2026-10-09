using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Toucan.Connector.Rest;

/// <summary>A translation as the service returns it.</summary>
public sealed record RemoteUnit(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("revision")] long Revision);

public enum PushOutcome
{
    Sent,

    /// <summary>The service holds a newer revision than the one this connector read; nothing was written.</summary>
    Conflict,
}

/// <summary>
/// The service protocol: <c>GET {base}/translations</c> returns <c>[{key, language, text, revision}]</c>;
/// <c>PUT {base}/translations/{key}/{language}</c> with <c>{ "text": … }</c> and <c>If-Match: "{revision}"</c> writes one
/// translation and answers 412 when the revision is stale (a new translation is written with <c>If-Match: "0"</c>).
/// </summary>
public sealed class RestRemote(HttpClient client, Uri baseAddress, string token)
{
    public async Task<IReadOnlyList<RemoteUnit>> PullAsync(CancellationToken cancellationToken)
    {
        using var request = Create(HttpMethod.Get, "translations");
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<RemoteUnit>>(cancellationToken).ConfigureAwait(false) ?? [];
    }

    public async Task<PushOutcome> PushAsync(string key, string language, string text, long expectedRevision, CancellationToken cancellationToken)
    {
        using var request = Create(HttpMethod.Put, $"translations/{Uri.EscapeDataString(key)}/{Uri.EscapeDataString(language)}");
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{expectedRevision}\""));
        request.Content = JsonContent.Create(new { text });
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.PreconditionFailed) return PushOutcome.Conflict;
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return PushOutcome.Sent;
    }

    private HttpRequestMessage Create(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    // Says what to do about the failure; the body is never included, since a service may echo the token back.
    private static Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken _)
    {
        if (response.IsSuccessStatusCode) return Task.CompletedTask;
        throw new RemoteException(response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "The server refused the API token. Check it in Settings → Plugins.",
            HttpStatusCode.Forbidden => "The API token has no access to these translations.",
            HttpStatusCode.NotFound => "The server has no translations at this address. Check the server address in Settings → Plugins.",
            HttpStatusCode.TooManyRequests => "The server asked to slow down. Try again in a minute.",
            _ => $"The server answered {(int)response.StatusCode} {response.ReasonPhrase}.",
        });
    }
}

/// <summary>A failure to talk to the service, with a message fit to show the user.</summary>
public sealed class RemoteException(string message) : Exception(message);
