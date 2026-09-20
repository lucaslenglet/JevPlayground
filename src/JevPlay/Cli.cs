using System.Reflection;
using JevPlay.Providers;
using JevPlay.Server;

namespace JevPlay;

/// <summary>
/// Argument parsing, by hand: three flags do not warrant a dependency. Nothing
/// else in the project reads <c>args</c> or the environment.
/// </summary>
internal static class Cli
{
    public const string ApiKeyVariable = "JEV_API_KEY";

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        switch (args[0])
        {
            case "-h" or "--help" or "help":
                PrintHelp();
                return 0;

            case "--version":
                Console.WriteLine(Version());
                return 0;

            case "serve":
                return await ServeAsync(args[1..]);

            default:
                Console.Error.WriteLine($"Unknown command: “{args[0]}”. Try: jevplay --help");
                return 2;
        }
    }

    private static async Task<int> ServeAsync(string[] args)
    {
        var provider = ProviderCatalog.Default;
        string? key = null;
        string? model = null;
        var port = 0;

        for (var i = 0; i < args.Length; i++)
        {
            var (name, inlineValue) = Split(args[i]);

            switch (name)
            {
                case "--key":
                    if (!TryValue(args, ref i, inlineValue, name, out key))
                    {
                        return 2;
                    }

                    break;

                case "--model" or "-m":
                    if (!TryValue(args, ref i, inlineValue, name, out model))
                    {
                        return 2;
                    }

                    if (string.IsNullOrWhiteSpace(model))
                    {
                        Console.Error.WriteLine($"The {name} option needs a non-empty value.");
                        return 2;
                    }

                    break;

                case "--provider":
                    if (!TryValue(args, ref i, inlineValue, name, out provider!))
                    {
                        return 2;
                    }

                    if (!ProviderCatalog.IsKnown(provider))
                    {
                        Console.Error.WriteLine(
                            $"Unknown provider: “{provider}”. One of: {string.Join(", ", ProviderCatalog.Names)}.");
                        return 2;
                    }

                    break;

                case "--port" or "-p":
                    if (!TryValue(args, ref i, inlineValue, name, out var raw))
                    {
                        return 2;
                    }

                    if (!int.TryParse(raw, out port) || port is < 0 or > 65535)
                    {
                        Console.Error.WriteLine($"Invalid port: “{raw}”. Expected an integer from 0 to 65535.");
                        return 2;
                    }

                    break;

                case "-h" or "--help":
                    PrintHelp();
                    return 0;

                default:
                    Console.Error.WriteLine($"Unknown option: “{args[i]}”. Try: jevplay --help");
                    return 2;
            }
        }

        // Priority: --key, then the environment variable.
        var keySource = string.Empty;
        if (!string.IsNullOrWhiteSpace(key))
        {
            keySource = "from --key";
        }
        else if (Environment.GetEnvironmentVariable(ApiKeyVariable) is { Length: > 0 } fromEnvironment)
        {
            key = fromEnvironment;
            keySource = $"from ${ApiKeyVariable}";
        }

        if (ProviderCatalog.NeedsApiKey(provider) && string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine($"Provider “{provider}” needs an API key, and none was found.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("There are two ways to supply one:");
            Console.Error.WriteLine("  jevplay serve --key YOUR_KEY");
            Console.Error.WriteLine($"  export {ApiKeyVariable}=YOUR_KEY   then   jevplay serve");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Otherwise “--provider simple-jev” (public demo) and “--provider mock”");
            Console.Error.WriteLine("(offline) work without a key.");
            return 1;
        }

        await Playground.RunAsync(ProviderCatalog.Create(provider, key, model), port, keySource);
        return 0;
    }

    /// <summary>
    /// The informational version, which alone keeps a prerelease suffix: the assembly
    /// version drops it, so a 1.2.3-rc.1 package would report itself as 1.2.3.
    /// </summary>
    private static string Version()
    {
        var informational = typeof(Cli).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrEmpty(informational))
        {
            return typeof(Cli).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        // The SDK appends "+<commit sha>" when the repository is known.
        var build = informational.IndexOf('+');
        return build < 0 ? informational : informational[..build];
    }

    /// <summary>Accepts both <c>--key value</c> and <c>--key=value</c>.</summary>
    private static (string Name, string? Value) Split(string argument)
    {
        var separator = argument.IndexOf('=');
        return separator < 0
            ? (argument, null)
            : (argument[..separator], argument[(separator + 1)..]);
    }

    private static bool TryValue(string[] args, ref int index, string? inlineValue, string name, out string value)
    {
        if (inlineValue is not null)
        {
            value = inlineValue;
            return true;
        }

        if (index + 1 >= args.Length)
        {
            Console.Error.WriteLine($"The {name} option needs a value.");
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return true;
    }

    private static void PrintHelp() => Console.WriteLine(
        $"""
        jevplay - a local playground for Jev (System One) models.

        Usage:
          jevplay serve [options]

        Options:
          --key <value>       API key. Falls back to ${ApiKeyVariable}.
          --model, -m <name>  Model to send. Defaults to the provider's own.
          --port, -p <n>      Port to listen on. Defaults to a free port picked by the OS.
          --provider <name>   {string.Join(" | ", ProviderCatalog.Names)}  (default: {ProviderCatalog.Default})
          -h, --help          Show this help.
          --version           Show the version.

        Providers:
          typesafe     Official TypeSafe API, {TypeSafeProvider.Url}
                       Model {TypeSafeProvider.DefaultModel}. Key required.
                       Body shape unverified: see the README.
          simple-jev   Public Simple Jev demo, {SimpleJevProvider.Url}
                       Model {SimpleJevProvider.DefaultModel}.
                       No key. Limits: 2k context tokens, 2 requests/s.
          mock         Deterministic simulated answers, no network call.

        Examples:
          jevplay serve --key sk-...
          jevplay serve --provider simple-jev
          jevplay serve --provider mock --port 3000
          jevplay serve --provider simple-jev -m featherless-ai/another-classifier
        """);
}
