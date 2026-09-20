namespace JevPlay.Providers;

/// <summary>The only place that knows the list of providers.</summary>
public static class ProviderCatalog
{
    public const string TypeSafe = "typesafe";
    public const string SimpleJev = "simple-jev";
    public const string Mock = "mock";

    public const string Default = TypeSafe;

    public static readonly string[] Names = [TypeSafe, SimpleJev, Mock];

    public static bool NeedsApiKey(string name) => name == TypeSafe;

    public static bool IsKnown(string name) => Names.Contains(name);

    public static IJevProvider Create(string name, string? apiKey, string? model) => name switch
    {
        TypeSafe => new TypeSafeProvider(apiKey ?? throw new ArgumentNullException(nameof(apiKey)), model),
        SimpleJev => new SimpleJevProvider(apiKey, model),
        Mock => new MockProvider(model),
        _ => throw new ArgumentException($"Unknown provider: {name}", nameof(name)),
    };
}
