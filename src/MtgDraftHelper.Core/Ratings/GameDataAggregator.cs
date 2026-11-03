using System.Globalization;
using MtgDraftHelper.Core.Csv;

namespace MtgDraftHelper.Core.Ratings;

/// <summary>
/// Agregace <c>game_data_public.*.csv</c> (jedna hra na řádek) na statistiky karet
/// a barevných kombinací.
/// </summary>
public sealed class GameDataAggregator
{
    /// <summary>Klíč barev pro statistiky přes všechny balíčky.</summary>
    public const string AllColors = "";

    private readonly double _topThreshold;

    public GameDataAggregator(double topWinRateThreshold = RatingsDefaults.TopWinRateThreshold) =>
        _topThreshold = topWinRateThreshold;

    public sealed class Result
    {
        public required IReadOnlyList<string> CardNames { get; init; }

        /// <summary>Počty podle (skupina, hlavní barvy balíčku) → pole indexované jako <see cref="CardNames"/>.</summary>
        public required IReadOnlyDictionary<(PlayerGroup Group, string Colors), GameCounts[]> Cards { get; init; }

        public required IReadOnlyDictionary<(PlayerGroup Group, string Main, string Splash), (long Games, long Wins)> Colors { get; init; }

        public long Rows { get; init; }
    }

    public Result Aggregate(CsvReader csv, CancellationToken cancellationToken = default)
    {
        if (!csv.Read())
            throw new InvalidDataException("Dataset je prázdný.");

        var header = Enumerable.Range(0, csv.FieldCount).Select(csv.GetString).ToArray();
        var column = header.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i);

        int Required(string name) => column.TryGetValue(name, out var i)
            ? i
            : throw new InvalidDataException($"V datasetu chybí sloupec '{name}'.");

        var wonCol = Required("won");
        var mainCol = Required("main_colors");
        var splashCol = Required("splash_colors");
        var bucketCol = column.GetValueOrDefault("user_game_win_rate_bucket", -1);

        var cardNames = header.Where(h => h.StartsWith("deck_", StringComparison.Ordinal)).Select(h => h["deck_".Length..]).ToArray();
        var deckCols = cardNames.Select(n => Required("deck_" + n)).ToArray();
        var ohCols = cardNames.Select(n => column.GetValueOrDefault("opening_hand_" + n, -1)).ToArray();
        var drawnCols = cardNames.Select(n => column.GetValueOrDefault("drawn_" + n, -1)).ToArray();

        var cards = new Dictionary<(PlayerGroup, string), GameCounts[]>();
        var colors = new Dictionary<(PlayerGroup, string, string), (long, long)>();
        var groups = new PlayerGroup[2];
        long rows = 0;

        while (csv.Read())
        {
            if ((++rows & 0xFFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var won = csv.GetSpan(wonCol) is "True" or "true" or "1";
            var main = csv.GetString(mainCol);
            var splash = csv.GetString(splashCol);

            var groupCount = 0;
            groups[groupCount++] = PlayerGroup.All;
            if (bucketCol >= 0 && double.TryParse(csv.GetSpan(bucketCol), NumberStyles.Float, CultureInfo.InvariantCulture, out var bucket)
                && bucket >= _topThreshold - 1e-9)
                groups[groupCount++] = PlayerGroup.Top;

            for (var g = 0; g < groupCount; g++)
            {
                var group = groups[g];
                var key = (group, main, splash);
                var (games, wins) = colors.GetValueOrDefault(key);
                colors[key] = (games + 1, wins + (won ? 1 : 0));

                Accumulate(GetCounts(cards, group, AllColors, cardNames.Length));
                if (main.Length > 0)
                    Accumulate(GetCounts(cards, group, main, cardNames.Length));
            }

            void Accumulate(GameCounts[] counts)
            {
                var w = won ? 1 : 0;
                for (var i = 0; i < deckCols.Length; i++)
                {
                    if (csv.GetInt(deckCols[i]) <= 0)
                        continue;

                    ref var c = ref counts[i];
                    c.DeckGames++;
                    c.DeckWins += w;

                    var inOpening = ohCols[i] >= 0 && csv.GetInt(ohCols[i]) > 0;
                    var drawn = drawnCols[i] >= 0 && csv.GetInt(drawnCols[i]) > 0;
                    if (inOpening)
                    {
                        c.OpeningHandGames++;
                        c.OpeningHandWins += w;
                    }
                    if (drawn)
                    {
                        c.DrawnGames++;
                        c.DrawnWins += w;
                    }
                    if (inOpening || drawn)
                    {
                        c.InHandGames++;
                        c.InHandWins += w;
                    }
                    else
                    {
                        c.NotDrawnGames++;
                        c.NotDrawnWins += w;
                    }
                }
            }
        }

        return new Result
        {
            CardNames = cardNames,
            Cards = cards,
            Colors = colors,
            Rows = rows,
        };
    }

    private static GameCounts[] GetCounts(Dictionary<(PlayerGroup, string), GameCounts[]> cards, PlayerGroup group, string colors, int cardCount)
    {
        if (!cards.TryGetValue((group, colors), out var counts))
            cards[(group, colors)] = counts = new GameCounts[cardCount];
        return counts;
    }
}
