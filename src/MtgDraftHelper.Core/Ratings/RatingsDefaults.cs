namespace MtgDraftHelper.Core.Ratings;

public static class RatingsDefaults
{
    /// <summary>
    /// Hranice „top hráčů“ podle sloupce user_game_win_rate_bucket. Hodnota 0.60 odpovídá
    /// zhruba nejlepší čtvrtině her (ověřeno na FRA Premier Draft).
    /// </summary>
    public const double TopWinRateThreshold = 0.60;

    /// <summary>
    /// Pořadí formátů, ze kterých se berou data, když pro požadovaný formát dataset neexistuje.
    /// </summary>
    public static IReadOnlyList<string> FormatFallbacks(string format) => format switch
    {
        "PremierDraft" => ["PremierDraft", "TradDraft", "Sealed"],
        "TradDraft" => ["TradDraft", "PremierDraft", "Sealed"],
        "QuickDraft" => ["QuickDraft", "PremierDraft", "TradDraft"],
        "PickTwoDraft" => ["PickTwoDraft", "PremierDraft", "TradDraft"],
        "Sealed" => ["Sealed", "TradSealed", "PremierDraft"],
        "TradSealed" => ["TradSealed", "Sealed", "PremierDraft"],
        _ => [format, "PremierDraft", "TradDraft", "Sealed"],
    };
}
