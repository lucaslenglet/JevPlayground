using System.Reflection;

namespace JevPlay.Server;

/// <summary>The front end, embedded in the assembly (see the .csproj).</summary>
public static class Assets
{
    private const string Prefix = "web/";

    private static readonly Dictionary<string, Asset> Files = Load();

    public static bool TryGet(string path, out Asset asset) => Files.TryGetValue(Normalize(path), out asset);

    private static string Normalize(string path)
    {
        var trimmed = path.Trim('/');
        return trimmed.Length == 0 ? "index.html" : trimmed;
    }

    private static Dictionary<string, Asset> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var files = new Dictionary<string, Asset>(StringComparer.Ordinal);

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);

            var relative = name[Prefix.Length..];
            files[relative] = new Asset(memory.ToArray(), ContentType(relative));
        }

        return files;
    }

    private static string ContentType(string path) => Path.GetExtension(path) switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".ico" => "image/x-icon",
        _ => "application/octet-stream",
    };
}

public readonly record struct Asset(byte[] Content, string ContentType);
