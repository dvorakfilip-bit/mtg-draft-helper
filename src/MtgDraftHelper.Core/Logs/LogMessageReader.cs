using System.Globalization;
using System.Text.RegularExpressions;

namespace MtgDraftHelper.Core.Logs;

/// <summary>
/// Skládá řádky Player.log do zpráv. Bezstavové kromě čekající odpovědi
/// (hlavička <c>&lt;== Metoda(id)</c>, jejíž JSON přijde na dalším řádku).
/// </summary>
public sealed partial class LogMessageReader
{
    private const string Prefix = "[UnityCrossThreadLogger]";

    private string? _pendingResponse;
    private DateTime? _lastTimestamp;

    [GeneratedRegex(@"^<==\s*(?<method>[A-Za-z_.0-9]+)\((?<id>[^)]*)\)")]
    private static partial Regex ResponseHeaderRegex();

    [GeneratedRegex(@"^==>\s*(?<method>[A-Za-z_.0-9]+)\s")]
    private static partial Regex RequestRegex();

    [GeneratedRegex(@"V\d+$")]
    private static partial Regex VersionSuffixRegex();

    private static readonly string[] TimestampFormats =
    [
        "dd.MM.yyyy HH:mm:ss", "d.M.yyyy H:mm:ss",
        "M/d/yyyy h:mm:ss tt", "M/d/yyyy H:mm:ss",
        "dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy/MM/dd HH:mm:ss",
    ];

    /// <summary>Normalizovaný název metody: bez podtržítek a bez přípony verze.</summary>
    public static string NormalizeMethod(string method) =>
        VersionSuffixRegex().Replace(method.Replace("_", ""), "");

    /// <summary>Zpracuje jeden řádek; vrací zprávu, pokud řádek nějakou dokončil.</summary>
    public LogMessage? ReadLine(string line)
    {
        if (_pendingResponse is { } method)
        {
            _pendingResponse = null;
            // "[" samotné nestačí – tak začínají i řádky "[UnityCrossThreadLogger]...".
            if (line.StartsWith('{') || (line.Length > 1 && line[0] == '[' && !char.IsLetter(line[1])))
                return new LogMessage(LogMessageKind.Response, method, LogJson.ParseFrom(line), _lastTimestamp);
            // JSON nepřišel – řádek zpracujeme normálně.
        }

        if (line.StartsWith("DETAILED LOGS:", StringComparison.Ordinal))
        {
            var enabled = line.Contains("ENABLED", StringComparison.Ordinal) && !line.Contains("DISABLED", StringComparison.Ordinal);
            return new LogMessage(LogMessageKind.DetailedLogs, "DetailedLogs", null, _lastTimestamp, enabled);
        }

        var body = line.StartsWith(Prefix, StringComparison.Ordinal) ? line[Prefix.Length..] : line;

        if (body.StartsWith("<==", StringComparison.Ordinal))
        {
            var match = ResponseHeaderRegex().Match(body);
            if (!match.Success)
                return null;
            var name = NormalizeMethod(match.Groups["method"].Value);
            var jsonStart = match.Index + match.Length;
            if (body.IndexOf('{', jsonStart) >= 0)
                return new LogMessage(LogMessageKind.Response, name, LogJson.ParseFrom(body, jsonStart), _lastTimestamp);
            _pendingResponse = name;
            return null;
        }

        if (body.StartsWith("==>", StringComparison.Ordinal))
        {
            var match = RequestRegex().Match(body);
            if (!match.Success)
                return null;
            return new LogMessage(LogMessageKind.Request, NormalizeMethod(match.Groups["method"].Value),
                LogJson.ParseFrom(body, match.Length), _lastTimestamp);
        }

        if (body.StartsWith("Draft.Notify", StringComparison.Ordinal))
            return new LogMessage(LogMessageKind.Notify, "DraftNotify", LogJson.ParseFrom(body), _lastTimestamp);

        if (line.StartsWith(Prefix, StringComparison.Ordinal) && body.Length > 0 && char.IsDigit(body[0]))
            _lastTimestamp = ParseTimestamp(body) ?? _lastTimestamp;

        return null;
    }

    internal static DateTime? ParseTimestamp(string text)
    {
        text = text.Trim().TrimEnd(':');
        if (DateTime.TryParseExact(text, TimestampFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var exact))
            return exact;
        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed : null;
    }
}
