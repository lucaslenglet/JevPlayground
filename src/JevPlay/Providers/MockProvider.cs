using System.Globalization;
using System.Text.Json.Nodes;
using JevPlay.Jev;

namespace JevPlay.Providers;

/// <summary>
/// Simulated answers, with no network and no quota. The draw is deterministic:
/// the same request always yields the same answer.
/// </summary>
public sealed class MockProvider(string? model) : IJevProvider
{
    public const string DefaultModel = "jev-mock";

    public string Name => ProviderCatalog.Mock;

    public string Model => model ?? DefaultModel;

    public string Endpoint => "none (answers are simulated locally)";

    public bool IsOffline => true;

    public Task<JevReply> AskAsync(JevAsk ask, CancellationToken cancellationToken)
    {
        var sent = BuildBody(ask);

        return Task.FromResult(new JevReply
        {
            Model = Model,
            Answers = [.. ask.Questions.Select(question => Answer(ask.State, question))],
            // Rough approximation: 4 characters per token.
            InputTokens = (int)Math.Ceiling(sent.ToJsonString().Length / 4.0),
            OutputTokens = 0,
            SentBody = sent,
        });
    }

    private JsonObject BuildBody(JevAsk ask)
    {
        var questions = new JsonObject();
        foreach (var question in ask.Questions)
        {
            var node = new JsonObject
            {
                ["type"] = JevJson.TypeName(question.Type),
                ["instructions"] = question.Instructions,
            };

            switch (question.Type)
            {
                case JevQuestionType.Choice:
                    node["criteria"] = Descriptions(question.Choices);
                    break;

                case JevQuestionType.Score:
                    node["criteria"] = new JsonArray([.. (question.Levels ?? []).Select(level => (JsonNode?)level)]);
                    break;

                default:
                    if (question.NoulHints is not null)
                    {
                        node["criteria"] = Descriptions(question.NoulHints);
                    }

                    break;
            }

            questions[question.Id] = node;
        }

        return new JsonObject
        {
            ["model"] = Model,
            ["state"] = ask.State,
            ["questions"] = questions,
        };
    }

    private static JsonObject Descriptions(IReadOnlyList<KeyValuePair<string, string?>>? entries)
    {
        var node = new JsonObject();
        foreach (var (key, description) in entries ?? [])
        {
            node[key] = description is null ? null : JsonValue.Create(description);
        }

        return node;
    }

    private static JevAnswer Answer(string state, JevQuestion question)
    {
        switch (question.Type)
        {
            case JevQuestionType.Choice:
            {
                var probabilities = Distribute(state, question.Id, [.. (question.Choices ?? []).Select(c => c.Key)]);
                var top = Top(probabilities);

                return new JevAnswer
                {
                    Id = question.Id,
                    Type = question.Type,
                    Choice = top.Key,
                    Confidence = top.Value,
                    Probabilities = probabilities,
                };
            }

            case JevQuestionType.Score:
            {
                var levels = question.Levels ?? [];
                var probabilities = Distribute(state, question.Id, [.. Enumerable.Range(0, levels.Count).Select(Key)]);

                return new JevAnswer
                {
                    Id = question.Id,
                    Type = question.Type,
                    Score = probabilities.Select((entry, index) => index * entry.Value).Sum(),
                    Confidence = probabilities.Count == 0 ? 0 : probabilities.Max(entry => entry.Value),
                    Probabilities = probabilities,
                    Legend = [.. levels.Select((label, index) => new KeyValuePair<string, string>(Key(index), label))],
                };
            }

            default:
                return new JevAnswer
                {
                    Id = question.Id,
                    Type = question.Type,
                    Noul = Math.Clamp(Seed(state, question.Id, "noul"), 0.01, 0.99),
                };
        }
    }

    private static string Key(int index) => index.ToString(CultureInfo.InvariantCulture);

    private static List<KeyValuePair<string, double>> Distribute(string state, string id, IReadOnlyList<string> keys)
    {
        var raw = keys.Select(key => 0.05 + Seed(state, id, key)).ToList();
        var total = raw.Sum();

        return total == 0
            ? []
            : [.. keys.Select((key, index) => new KeyValuePair<string, double>(key, raw[index] / total))];
    }

    private static KeyValuePair<string, double> Top(List<KeyValuePair<string, double>> probabilities)
    {
        var top = new KeyValuePair<string, double>(string.Empty, double.NegativeInfinity);
        foreach (var entry in probabilities)
        {
            if (entry.Value > top.Value)
            {
                top = entry;
            }
        }

        return top;
    }

    /// <summary>
    /// FNV-1a over the three parts, brought back into [0, 1). Ported from the
    /// Node prototype: JavaScript and C# string walks agree, except that the
    /// low half of a surrogate pair has to be skipped.
    /// </summary>
    private static double Seed(string state, string id, string text)
    {
        var source = $"{state}|{id}|{text}";
        var hash = 2166136261u;

        for (var i = 0; i < source.Length; i++)
        {
            hash = unchecked((hash ^ source[i]) * 16777619u);
            if (char.IsHighSurrogate(source[i]) && i + 1 < source.Length && char.IsLowSurrogate(source[i + 1]))
            {
                i++;
            }
        }

        return hash % 1000 / 1000.0;
    }
}
