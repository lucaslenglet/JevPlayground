using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JevPlay.Jev;

namespace JevPlay.Providers;

/// <summary>
/// Transport shared by the remote providers: send JSON, read JSON, turn
/// failures into <see cref="JevProviderException"/>. It knows no body shape.
/// </summary>
public abstract class HttpJevProvider : IJevProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    public abstract string Name { get; }

    public abstract string Model { get; }

    public abstract string Endpoint { get; }

    public bool IsOffline => false;

    public abstract Task<JevReply> AskAsync(JevAsk ask, CancellationToken cancellationToken);

    /// <summary>Posts <paramref name="body"/> and returns what was sent along with the parsed reply.</summary>
    protected static async Task<(JsonNode Sent, TResponse Received)> PostAsync<TBody, TResponse>(
        string url, TBody body, string? bearer, CancellationToken cancellationToken)
    {
        var sent = JsonSerializer.SerializeToNode(body, JevJson.Options)
            ?? throw new JevProviderException("Request body could not be serialized.");

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(sent.ToJsonString(JevJson.Options), Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrEmpty(bearer))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new JevProviderException($"Call failed: {ex.Message}");
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                parsed = null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new JevProviderException(
                    $"The API answered {status}.", status, parsed ?? JsonValue.Create(Shorten(text)));
            }

            if (parsed is null)
            {
                throw new JevProviderException($"Non-JSON answer from the API (HTTP {status}): {Shorten(text)}");
            }

            try
            {
                return (sent, parsed.Deserialize<TResponse>(JevJson.Options)
                    ?? throw new JevProviderException("The API returned an empty answer."));
            }
            catch (JsonException ex)
            {
                throw new JevProviderException($"Unexpected answer from the API: {ex.Message}", 502, parsed);
            }
        }
    }

    private static string Shorten(string text) =>
        text.Length <= 500 ? text : string.Concat(text.AsSpan(0, 500), "...");
}
