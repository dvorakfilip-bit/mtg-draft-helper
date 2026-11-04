using System.Text.Json;
using System.Text.Json.Nodes;

namespace MtgDraftHelper.Core.Logs;

/// <summary>Parsování JSON z logu Areny včetně vícenásobně zakódovaných řetězců.</summary>
public static class LogJson
{
    private static readonly string[] EncodedKeys = ["request", "Request", "payload", "Payload"];

    /// <summary>
    /// Najde první JSON objekt nebo pole v textu a naparsuje ho. Vrací null, pokud se nepodaří.
    /// </summary>
    public static JsonNode? ParseFrom(string text, int startIndex = 0)
    {
        var start = text.IndexOfAny(['{', '['], startIndex);
        if (start < 0)
            return null;

        var json = text[start..];
        var node = TryParse(json);
        if (node is null)
        {
            // Arena občas zapíše vnořený JSON bez escapování: "request":"{"DraftId":...}"
            var repaired = json.Replace("\"request\":\"{", "\"request\":{").Replace("}\"}", "}}");
            node = TryParse(repaired);
        }
        return node is null ? null : Unwrap(node);
    }

    private static JsonNode? TryParse(string json)
    {
        try
        {
            // Za JSONem může na řádku následovat další text – čteme jen první hodnotu.
            var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json), new JsonReaderOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            return JsonNode.Parse(ref reader);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Rekurzivně nahradí řetězce s JSONem v klíčích request/Payload jejich obsahem.</summary>
    public static JsonNode Unwrap(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in EncodedKeys)
                {
                    if (obj[key] is JsonValue value && value.TryGetValue<string>(out var s))
                    {
                        var trimmed = s.TrimStart();
                        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
                        {
                            var inner = TryParse(trimmed);
                            if (inner is not null)
                                obj[key] = Unwrap(inner);
                        }
                    }
                }
                foreach (var (_, child) in obj.ToList())
                    if (child is JsonObject or JsonArray)
                        Unwrap(child);
                break;
            case JsonArray array:
                foreach (var child in array)
                    if (child is not null)
                        Unwrap(child);
                break;
        }
        return node;
    }

    public static string? GetString(this JsonNode? node, string key) =>
        node?[key] is JsonValue v ? v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>(),
            JsonValueKind.Number => v.ToJsonString(),
            _ => null,
        } : null;

    public static int? GetInt(this JsonNode? node, string key)
    {
        if (node?[key] is not JsonValue v)
            return null;
        if (v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var i))
            return i;
        return v.GetValueKind() == JsonValueKind.String && int.TryParse(v.GetValue<string>(), out var j) ? j : null;
    }

    /// <summary>
    /// Seznam GrpId z pole čísel, pole řetězců nebo řetězce s čísly oddělenými čárkou.
    /// </summary>
    public static List<int> GetIds(this JsonNode? node, string key)
    {
        var result = new List<int>();
        switch (node?[key])
        {
            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not JsonValue v)
                        continue;
                    if (v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var i))
                        result.Add(i);
                    else if (v.GetValueKind() == JsonValueKind.String && int.TryParse(v.GetValue<string>(), out var j))
                        result.Add(j);
                }
                break;
            case JsonValue v when v.GetValueKind() == JsonValueKind.String:
                foreach (var part in v.GetValue<string>().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (int.TryParse(part, out var id))
                        result.Add(id);
                break;
            case JsonValue v when v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var single):
                result.Add(single);
                break;
        }
        return result;
    }
}
