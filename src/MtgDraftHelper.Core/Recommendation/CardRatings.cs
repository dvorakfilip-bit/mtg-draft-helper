using MtgDraftHelper.Core.Cards;
using MtgDraftHelper.Core.Ratings;
using MtgDraftHelper.Core.Storage;

namespace MtgDraftHelper.Core.Recommendation;

/// <summary>Zdroj hodnocení karet pro doporučování (kvůli testům jako rozhraní).</summary>
public interface ICardRatings
{
    /// <summary>Základní síla karty 0–100 (percentil vyhlazeného GIH WR v edici), null = bez dat.</summary>
    double? BaseScore(string cardName);

    CardStats? Stats(string cardName);

    /// <summary>Statistiky karty v balíčcích s danými hlavními barvami (např. "WU").</summary>
    CardStats? StatsForColors(string cardName, string colors);
}

/// <summary>
/// Hodnocení karet jedné edice a formátu ze <see cref="StatsStore"/>. GIH WR se vyhlazuje
/// k průměru edice (Bayes), aby karty s malým počtem her neměly extrémní hodnoty.
/// </summary>
public sealed class CardRatings : ICardRatings
{
    /// <summary>Váha průměru při vyhlazení, v počtu her.</summary>
    public const int SmoothingGames = 200;

    private readonly StatsStore _store;
    private readonly string _setCode;
    private readonly string _format;
    private readonly PlayerGroup _group;
    private readonly IReadOnlyDictionary<string, CardStats> _all;
    private readonly Dictionary<string, double> _baseScores;
    private readonly Dictionary<string, IReadOnlyDictionary<string, CardStats>> _byColors = new();
    private readonly object _lock = new();

    public CardRatings(StatsStore store, string setCode, string format, PlayerGroup group = PlayerGroup.All)
    {
        _store = store;
        _setCode = setCode;
        _format = format;
        _group = group;
        _all = store.GetCardStats(setCode, format, group);
        MeanGihWr = ComputeMean(_all.Values);
        _baseScores = ComputePercentiles(_all.Values, MeanGihWr);
    }

    public string SetCode => _setCode;
    public string Format => _format;
    public double MeanGihWr { get; }
    public bool HasData => _baseScores.Count > 0;

    public double? BaseScore(string cardName) => _baseScores.TryGetValue(cardName, out var s) ? s : null;

    public CardStats? Stats(string cardName) => _all.GetValueOrDefault(cardName);

    public CardStats? StatsForColors(string cardName, string colors)
    {
        IReadOnlyDictionary<string, CardStats> stats;
        lock (_lock)
        {
            if (!_byColors.TryGetValue(colors, out stats!))
                _byColors[colors] = stats = _store.GetCardStats(_setCode, _format, _group, colors);
        }
        return stats.GetValueOrDefault(cardName);
    }

    public static double SmoothedGihWr(GameCounts counts, double mean) =>
        (counts.InHandWins + SmoothingGames * mean) / (counts.InHandGames + SmoothingGames);

    private static double ComputeMean(IEnumerable<CardStats> stats)
    {
        long wins = 0, games = 0;
        foreach (var s in stats)
        {
            wins += s.Game.InHandWins;
            games += s.Game.InHandGames;
        }
        return games > 0 ? (double)wins / games : 0.55;
    }

    private static Dictionary<string, double> ComputePercentiles(IEnumerable<CardStats> stats, double mean)
    {
        var ranked = stats
            .Where(s => s.Game.InHandGames > 0)
            .Select(s => (s.Name, Wr: SmoothedGihWr(s.Game, mean)))
            .OrderBy(x => x.Wr)
            .ToList();

        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ranked.Count; i++)
            result[ranked[i].Name] = ranked.Count == 1 ? 50 : 100.0 * i / (ranked.Count - 1);
        return result;
    }
}
