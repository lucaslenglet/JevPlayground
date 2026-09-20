using System.Text.Json.Nodes;

namespace JevPlay.Jev;

public enum JevQuestionType
{
    /// <summary>One candidate among 2 to 50.</summary>
    Choice,

    /// <summary>A level on an ordered scale of 2 to 50 steps.</summary>
    Score,

    /// <summary>A shaded true / false, returned between 0.01 and 0.99.</summary>
    Noul,
}

/// <summary>
/// A question in the neutral shape the interface speaks. Each provider
/// translates it into the HTTP body it expects.
/// </summary>
public sealed class JevQuestion
{
    public required string Id { get; init; }

    public required JevQuestionType Type { get; init; }

    public required string Instructions { get; init; }

    /// <summary>choice: candidate -> description, null when there is none.</summary>
    public IReadOnlyList<KeyValuePair<string, string?>>? Choices { get; init; }

    /// <summary>score: the levels, lowest first.</summary>
    public IReadOnlyList<string>? Levels { get; init; }

    /// <summary>noul: optional, "true" and/or "false" keys -> description.</summary>
    public IReadOnlyList<KeyValuePair<string, string?>>? NoulHints { get; init; }
}

public sealed class JevAsk
{
    public required string State { get; init; }

    public required IReadOnlyList<JevQuestion> Questions { get; init; }
}

public sealed class JevAnswer
{
    public required string Id { get; init; }

    public required JevQuestionType Type { get; init; }

    public string? Choice { get; init; }

    /// <summary>score: a fractional mean index, from 0 to N-1.</summary>
    public double? Score { get; init; }

    /// <summary>noul: from 0.01 to 0.99, with no confidence attached.</summary>
    public double? Noul { get; init; }

    public double? Confidence { get; init; }

    public IReadOnlyList<KeyValuePair<string, double>>? Probabilities { get; init; }

    /// <summary>score: index -> level label.</summary>
    public IReadOnlyList<KeyValuePair<string, string>>? Legend { get; init; }
}

public sealed class JevReply
{
    public required string Model { get; init; }

    public required IReadOnlyList<JevAnswer> Answers { get; init; }

    public int? InputTokens { get; init; }

    public int? OutputTokens { get; init; }

    /// <summary>The body actually put on the wire, echoed back to the interface.</summary>
    public JsonNode? SentBody { get; init; }
}

public sealed class JevProviderException(string message, int statusCode = 502, JsonNode? detail = null)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public JsonNode? Detail { get; } = detail;
}

/// <summary>A browser request rejected before the model is ever called.</summary>
public sealed class JevRequestException(string message) : Exception(message);
