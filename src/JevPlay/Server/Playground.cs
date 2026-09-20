using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using JevPlay.Jev;
using JevPlay.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JevPlay.Server;

/// <summary>
/// The local server: it serves the front end and relays its requests to the
/// provider. The browser never calls the remote API directly, which avoids
/// CORS and keeps the API key out of the page.
/// </summary>
public static class Playground
{
    private const long MaxRequestBytes = 1_000_000;

    public static async Task RunAsync(IJevProvider provider, int port, string keySource)
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaxRequestBytes);

        // Port 0: the OS picks a free port, read back once started.
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            await next();
        });

        app.MapGet("/api/config", () => Json(new JsonObject
        {
            ["provider"] = provider.Name,
            ["model"] = provider.Model,
            ["endpoint"] = provider.Endpoint,
            ["mock"] = provider.IsOffline,
            ["hasApiKey"] = keySource.Length > 0,
        }));

        // The cast marks this as a route handler, whose result is written to the
        // response, rather than a RequestDelegate, which would discard it.
        app.MapPost("/api/classify", (Delegate)((HttpContext context) => ClassifyAsync(provider, context)));

        app.MapGet("/{*path}", (HttpContext context) =>
            Assets.TryGet(context.Request.Path.Value ?? string.Empty, out var asset)
                ? Results.Bytes(asset.Content, asset.ContentType)
                : Results.NotFound("Not found"));

        await app.StartAsync();

        Announce(provider, app.Urls.First(), keySource);

        await app.WaitForShutdownAsync();
    }

    private static async Task<IResult> ClassifyAsync(IJevProvider provider, HttpContext context)
    {
        string body;
        try
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            body = await reader.ReadToEndAsync(context.RequestAborted);
        }
        catch (BadHttpRequestException)
        {
            return Json(
                new JsonObject { ["error"] = "Request body is too large." },
                StatusCodes.Status413PayloadTooLarge);
        }

        JevAsk ask;
        try
        {
            ask = JevJson.ParseAsk(body);
        }
        catch (JevRequestException ex)
        {
            return Json(new JsonObject { ["error"] = ex.Message }, StatusCodes.Status400BadRequest);
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            var reply = await provider.AskAsync(ask, context.RequestAborted);

            return Json(new JsonObject
            {
                ["request"] = reply.SentBody?.DeepClone(),
                ["latencyMs"] = Elapsed(started),
                ["response"] = JevJson.RenderReply(reply),
            });
        }
        catch (JevProviderException ex)
        {
            return Json(
                new JsonObject
                {
                    ["error"] = ex.Message,
                    ["detail"] = ex.Detail?.DeepClone(),
                    ["latencyMs"] = Elapsed(started),
                },
                ex.StatusCode);
        }
    }

    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    private static IResult Json(JsonNode node, int statusCode = StatusCodes.Status200OK) =>
        Results.Text(node.ToJsonString(JevJson.Options), "application/json", Encoding.UTF8, statusCode);

    private static void Announce(IJevProvider provider, string url, string keySource)
    {
        Console.WriteLine($"Jev Playground: {url}");
        Console.WriteLine($"  provider: {provider.Name}");
        Console.WriteLine($"  model:    {provider.Model}");
        Console.WriteLine($"  endpoint: {provider.Endpoint}");
        Console.WriteLine($"  key:      {(keySource.Length > 0 ? keySource : "none (this provider does not need one)")}");
        Console.WriteLine();
        Console.WriteLine("Open the URL above in a browser. Press Ctrl+C to stop.");
    }
}
