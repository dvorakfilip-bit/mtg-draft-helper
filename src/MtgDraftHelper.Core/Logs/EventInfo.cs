using System.Text.RegularExpressions;

namespace MtgDraftHelper.Core.Logs;

public enum LimitedFormat
{
    /// <summary>Není limited event (constructed, Jump In, ...).</summary>
    None,
    PremierDraft,
    TradDraft,
    QuickDraft,
    PickTwoDraft,
    Sealed,
    TradSealed,
    CubeDraft,

    /// <summary>Limited event, jehož konkrétní typ se nepodařilo určit (speciální eventy).</summary>
    OtherDraft,
    OtherSealed,
}

/// <summary>
/// Formát a edice podle interního názvu eventu, např. <c>PremierDraft_FRA_20260929</c>.
/// </summary>
public sealed partial record EventInfo(string EventName, LimitedFormat Format, string? SetCode)
{
    public bool IsLimited => Format != LimitedFormat.None;

    public bool IsDraft => Format is LimitedFormat.PremierDraft or LimitedFormat.TradDraft or LimitedFormat.QuickDraft
        or LimitedFormat.PickTwoDraft or LimitedFormat.CubeDraft or LimitedFormat.OtherDraft;

    public bool IsSealed => Format is LimitedFormat.Sealed or LimitedFormat.TradSealed or LimitedFormat.OtherSealed;

    /// <summary>Hodnota formátu pro datasety 17Lands, nebo null.</summary>
    public string? SeventeenLandsFormat => Format switch
    {
        LimitedFormat.PremierDraft or LimitedFormat.OtherDraft => "PremierDraft",
        LimitedFormat.TradDraft => "TradDraft",
        LimitedFormat.QuickDraft => "QuickDraft",
        LimitedFormat.PickTwoDraft => "PickTwoDraft",
        LimitedFormat.Sealed or LimitedFormat.OtherSealed => "Sealed",
        LimitedFormat.TradSealed => "TradSealed",
        _ => null,
    };

    [GeneratedRegex("^[A-Z][A-Z0-9]{2,4}$")]
    private static partial Regex SetCodeRegex();

    [GeneratedRegex(@"^Y\d{2}$")]
    private static partial Regex AlchemyYearRegex();

    private static readonly HashSet<string> NotSetCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TRAD", "BOT", "QUICK", "PICK", "TWO", "DRAFT", "SEALED", "CUBE", "COMP", "BO1", "BO3", "DAY1", "DAY2",
        "PLAY", "LADDER", "ARENA", "OPEN", "DIRECT", "MIDWEEK", "PREMIER",
    };

    /// <summary>
    /// Určí formát a edici. <paramref name="knownSetCodes"/> (např. edice z databáze karet)
    /// zpřesní výběr edice; bez něj se bere první část, která vypadá jako kód edice.
    /// </summary>
    public static EventInfo Parse(string eventName, IReadOnlySet<string>? knownSetCodes = null)
    {
        var parts = eventName.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var lower = eventName.ToLowerInvariant();
        bool Has(string token) => parts.Any(p => string.Equals(p, token, StringComparison.OrdinalIgnoreCase));

        var format = parts switch
        {
            _ when lower.Contains("picktwo") => LimitedFormat.PickTwoDraft,
            _ when lower.Contains("quickdraft") || lower.Contains("botdraft") || (Has("Bot") && Has("Draft")) => LimitedFormat.QuickDraft,
            _ when lower.Contains("premierdraft") => LimitedFormat.PremierDraft,
            _ when lower.Contains("traddraft") || (Has("Trad") && Has("Draft")) => LimitedFormat.TradDraft,
            _ when lower.Contains("tradsealed") || (Has("Trad") && lower.Contains("sealed")) => LimitedFormat.TradSealed,
            _ when lower.Contains("cube") && !lower.Contains("sealed") => LimitedFormat.CubeDraft,
            _ when lower.StartsWith("sealed") => LimitedFormat.Sealed,
            _ when lower.Contains("sealed") => LimitedFormat.OtherSealed,
            _ when lower.Contains("draft") => LimitedFormat.OtherDraft,
            _ => LimitedFormat.None,
        };

        string? setCode = null;
        if (format != LimitedFormat.None)
        {
            var candidates = parts
                .Where(p => SetCodeRegex().IsMatch(p) && !NotSetCodes.Contains(p) && !p.All(char.IsDigit))
                .OrderBy(p => AlchemyYearRegex().IsMatch(p)) // Y26_FRA_... – skutečná edice je FRA
                .ToList();
            setCode = knownSetCodes is null
                ? candidates.FirstOrDefault()
                : candidates.FirstOrDefault(knownSetCodes.Contains) ?? candidates.FirstOrDefault();
        }

        return new EventInfo(eventName, format, setCode);
    }
}
