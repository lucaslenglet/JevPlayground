using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace JevPlay.Jev;

/// <summary>Browser JSON to the neutral model, and back.</summary>
public static class JevJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // Non-ASCII text goes out as UTF-8 rather than \uXXXX escapes, which
        // keeps both the wire body and the "raw JSON" panel readable. Safe
        // here: this JSON is never served as HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private const int MinCriteria = 2;
    private const int MaxCriteria = 50;

    /// <exception cref="JevRequestException">When the payload is unreadable or invalid.</exception>
    public static JevAsk ParseAsk(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new JevRequestException($"Unreadable request: {ex.Message}");
        }

        if (root is not JsonObject obj)
        {
            throw new JevRequestException("Unreadable request: a JSON object is expected.");
        }

        var state = obj["state"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(state))
        {
            throw new JevRequestException("The context is empty.");
        }

        if (obj["questions"] is not JsonObject questions || questions.Count == 0)
        {
            throw new JevRequestException("There is no question to send.");
        }

        var parsed = new List<JevQuestion>(questions.Count);
        foreach (var (id, node) in questions)
        {
            if (node is not JsonObject question)
            {
                throw new JevRequestException($"Question “{id}” is malformed.");
            }

            parsed.Add(ParseQuestion(id, question));
        }

        return new JevAsk { State = state, Questions = parsed };
    }

    private static JevQuestion ParseQuestion(string id, JsonObject question)
    {
        var instructions = question["instructions"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(instructions))
        {
            throw new JevRequestException($"Question “{id}” has no instructions.");
        }

        var type = question["type"]?.GetValue<string>() switch
        {
            "choice" => JevQuestionType.Choice,
            "score" => JevQuestionType.Score,
            "noul" => JevQuestionType.Noul,
            var other => throw new JevRequestException(
                $"Unknown type for question “{id}”: “{other}”. Expected choice, score or noul."),
        };

        var criteria = question["criteria"];
        return type switch
        {
            JevQuestionType.Choice => new JevQuestion
            {
                Id = id,
                Type = type,
                Instructions = instructions,
                Choices = ReadDescriptions(id, criteria, "candidates", required: true),
            },
            JevQuestionType.Score => new JevQuestion
            {
                Id = id,
                Type = type,
                Instructions = instructions,
                Levels = ReadLevels(id, criteria),
            },
            _ => new JevQuestion
            {
                Id = id,
                Type = type,
                Instructions = instructions,
                NoulHints = ReadDescriptions(id, criteria, "keys", required: false),
            },
        };
    }

    private static List<KeyValuePair<string, string?>>? ReadDescriptions(
        string id, JsonNode? criteria, string what, bool required)
    {
        if (criteria is null)
        {
            return required
                ? throw new JevRequestException($"Question “{id}” has no criteria.")
                : null;
        }

        if (criteria is not JsonObject map)
        {
            throw new JevRequestException($"Criteria for question “{id}” must be an object.");
        }

        if (required && (map.Count < MinCriteria || map.Count > MaxCriteria))
        {
            throw new JevRequestException(
                $"Question “{id}” takes between {MinCriteria} and {MaxCriteria} {what}, it has {map.Count}.");
        }

        var list = new List<KeyValuePair<string, string?>>(map.Count);
        foreach (var (key, value) in map)
        {
            list.Add(new KeyValuePair<string, string?>(
                key, value?.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null));
        }

        return list;
    }

    private static List<string> ReadLevels(string id, JsonNode? criteria)
    {
        if (criteria is not JsonArray array)
        {
            throw new JevRequestException($"Levels for question “{id}” must be an array.");
        }

        if (array.Count is < MinCriteria or > MaxCriteria)
        {
            throw new JevRequestException(
                $"Question “{id}” takes between {MinCriteria} and {MaxCriteria} levels, it has {array.Count}.");
        }

        return [.. array.Select(node => node?.GetValue<string>() ?? string.Empty)];
    }

    public static JsonObject RenderReply(JevReply reply)
    {
        var answers = new JsonObject();
        foreach (var answer in reply.Answers)
        {
            answers[answer.Id] = RenderAnswer(answer);
        }

        var body = new JsonObject
        {
            ["model"] = reply.Model,
            ["answers"] = answers,
        };

        if (reply.InputTokens is not null || reply.OutputTokens is not null)
        {
            body["usage"] = new JsonObject
            {
                ["input_tokens"] = reply.InputTokens ?? 0,
                ["output_tokens"] = reply.OutputTokens ?? 0,
            };
        }

        return body;
    }

    private static JsonObject RenderAnswer(JevAnswer answer)
    {
        var node = new JsonObject
        {
            ["type"] = TypeName(answer.Type),
        };

        if (answer.Choice is not null)
        {
            node["choice"] = answer.Choice;
        }

        if (answer.Score is not null)
        {
            node["score"] = answer.Score;
        }

        if (answer.Noul is not null)
        {
            node["noul"] = answer.Noul;
        }

        if (answer.Confidence is not null)
        {
            node["confidence"] = answer.Confidence;
        }

        if (answer.Probabilities is not null)
        {
            var probabilities = new JsonObject();
            foreach (var (key, value) in answer.Probabilities)
            {
                probabilities[key] = value;
            }

            node["probabilities"] = probabilities;
        }

        if (answer.Legend is not null)
        {
            var legend = new JsonObject();
            foreach (var (key, value) in answer.Legend)
            {
                legend[key] = value;
            }

            node["legend"] = legend;
        }

        return node;
    }

    public static string TypeName(JevQuestionType type) => type switch
    {
        JevQuestionType.Choice => "choice",
        JevQuestionType.Score => "score",
        _ => "noul",
    };
}
