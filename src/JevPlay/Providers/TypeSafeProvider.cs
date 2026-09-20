using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JevPlay.Jev;

namespace JevPlay.Providers;

/// <summary>
/// The official TypeSafe API.
///
/// WARNING -- the body shape below is NOT VERIFIED. The official documentation
/// sits behind a waitlist and could not be read. What follows mirrors Simple
/// Jev, whose repository says it takes after the TypeSafe interface: that is
/// an inference, not a source. If the real API differs, everything to fix is
/// in this file -- the DTOs at the bottom and the two methods BuildBody and
/// ReadReply. No other file in the project knows the body shape.
/// </summary>
public sealed class TypeSafeProvider(string apiKey, string? model) : HttpJevProvider
{
    public const string Url = "https://api.typesafe.ai/v1/systemone";
    public const string DefaultModel = "jev-latest";

    public override string Name => ProviderCatalog.TypeSafe;

    public override string Model => model ?? DefaultModel;

    public override string Endpoint => Url;

    public override async Task<JevReply> AskAsync(JevAsk ask, CancellationToken cancellationToken)
    {
        var (sent, received) = await PostAsync<RequestBody, ResponseBody>(
            Url, BuildBody(ask), apiKey, cancellationToken);

        return ReadReply(ask, sent, received);
    }

    private RequestBody BuildBody(JevAsk ask)
    {
        var questions = new Dictionary<string, QuestionBody>(ask.Questions.Count);
        foreach (var question in ask.Questions)
        {
            questions[question.Id] = new QuestionBody(
                JevJson.TypeName(question.Type), question.Instructions, BuildCriteria(question));
        }

        return new RequestBody(Model, ask.State, questions);
    }

    private static JsonNode? BuildCriteria(JevQuestion question) => question.Type switch
    {
        JevQuestionType.Choice => Descriptions(question.Choices),
        JevQuestionType.Score => new JsonArray([.. (question.Levels ?? []).Select(level => (JsonNode?)level)]),
        _ => Descriptions(question.NoulHints),
    };

    private static JsonObject? Descriptions(IReadOnlyList<KeyValuePair<string, string?>>? entries)
    {
        if (entries is null)
        {
            return null;
        }

        var node = new JsonObject();
        foreach (var (key, description) in entries)
        {
            node[key] = description is null ? null : JsonValue.Create(description);
        }

        return node;
    }

    private JevReply ReadReply(JevAsk ask, JsonNode sent, ResponseBody received)
    {
        var answers = new List<JevAnswer>(ask.Questions.Count);
        foreach (var question in ask.Questions)
        {
            if (received.Answers?.TryGetValue(question.Id, out var answer) is true)
            {
                answers.Add(ReadAnswer(question, answer));
            }
        }

        return new JevReply
        {
            Model = received.Model ?? Model,
            Answers = answers,
            InputTokens = received.Usage?.InputTokens,
            OutputTokens = received.Usage?.OutputTokens,
            SentBody = sent,
        };
    }

    private static JevAnswer ReadAnswer(JevQuestion question, AnswerBody answer) => new()
    {
        Id = question.Id,
        Type = question.Type,
        Choice = answer.Choice,
        Score = answer.Score,
        Noul = answer.Noul,
        Confidence = answer.Confidence,
        Probabilities = answer.Probabilities is null ? null : [.. answer.Probabilities],
        Legend = answer.Legend is null ? null : [.. answer.Legend],
    };

    private sealed record RequestBody(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("questions")] Dictionary<string, QuestionBody> Questions);

    private sealed record QuestionBody(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("instructions")] string Instructions,
        [property: JsonPropertyName("criteria")] JsonNode? Criteria);

    private sealed record ResponseBody(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("answers")] Dictionary<string, AnswerBody>? Answers,
        [property: JsonPropertyName("usage")] UsageBody? Usage);

    private sealed record AnswerBody(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("choice")] string? Choice,
        [property: JsonPropertyName("score")] double? Score,
        [property: JsonPropertyName("noul")] double? Noul,
        [property: JsonPropertyName("confidence")] double? Confidence,
        [property: JsonPropertyName("probabilities")] Dictionary<string, double>? Probabilities,
        [property: JsonPropertyName("legend")] Dictionary<string, string>? Legend);

    private sealed record UsageBody(
        [property: JsonPropertyName("input_tokens")] int? InputTokens,
        [property: JsonPropertyName("output_tokens")] int? OutputTokens);
}
