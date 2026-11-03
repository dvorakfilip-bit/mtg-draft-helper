using System.Globalization;
using MtgDraftHelper.Core.Csv;

namespace MtgDraftHelper.Core.Ratings;

/// <summary>
/// Agregace <c>draft_data_public.*.csv</c> (jeden pick na řádek) na ALSA a ATA.
/// Předpokládá, že řádky jednoho draftu jdou za sebou (tak 17Lands data publikuje).
/// </summary>
public sealed class DraftDataAggregator
{
    private readonly double _topThreshold;

    public DraftDataAggregator(double topWinRateThreshold = RatingsDefaults.TopWinRateThreshold) =>
        _topThreshold = topWinRateThreshold;

    public sealed class Result
    {
        public required IReadOnlyList<string> CardNames { get; init; }

        public required IReadOnlyDictionary<PlayerGroup, DraftCounts[]> Cards { get; init; }

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

        var draftCol = Required("draft_id");
        var packCol = Required("pack_number");
        var pickNumberCol = Required("pick_number");
        var pickCol = Required("pick");
        var pick2Col = column.GetValueOrDefault("pick_2", -1);
        var bucketCol = column.GetValueOrDefault("user_game_win_rate_bucket", -1);

        const string packPrefix = "pack_card_";
        var cardNames = header.Where(h => h.StartsWith(packPrefix, StringComparison.Ordinal)).Select(h => h[packPrefix.Length..]).ToArray();
        var packCols = cardNames.Select(n => Required(packPrefix + n)).ToArray();
        var indexByName = cardNames.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);

        var all = new DraftCounts[cardNames.Length];
        var top = new DraftCounts[cardNames.Length];

        // Poslední pick (od 0), kdy byla karta v daném balíčku aktuálního draftu viděna.
        var lastSeen = new Dictionary<(int Pack, int Card), int>();
        string? currentDraft = null;
        var currentIsTop = false;
        long rows = 0;

        void FlushDraft()
        {
            foreach (var ((_, card), pick) in lastSeen)
            {
                Add(ref all[card], pick);
                if (currentIsTop)
                    Add(ref top[card], pick);
            }
            lastSeen.Clear();

            static void Add(ref DraftCounts c, int pick)
            {
                c.SeenCount++;
                c.SeenPickSum += pick + 1;
            }
        }

        void AddPick(ReadOnlySpan<char> name, int pick, bool isTop)
        {
            if (name.IsEmpty || !indexByName.TryGetValue(name.ToString(), out var card))
                return;
            all[card].PickCount++;
            all[card].PickSum += pick + 1;
            if (isTop)
            {
                top[card].PickCount++;
                top[card].PickSum += pick + 1;
            }
        }

        while (csv.Read())
        {
            if ((++rows & 0xFFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var draft = csv.GetSpan(draftCol);
            if (currentDraft is null || !draft.SequenceEqual(currentDraft))
            {
                FlushDraft();
                currentDraft = draft.ToString();
                currentIsTop = bucketCol >= 0
                    && double.TryParse(csv.GetSpan(bucketCol), NumberStyles.Float, CultureInfo.InvariantCulture, out var bucket)
                    && bucket >= _topThreshold - 1e-9;
            }

            var pack = csv.GetInt(packCol);
            var pick = csv.GetInt(pickNumberCol);

            AddPick(csv.GetSpan(pickCol), pick, currentIsTop);
            if (pick2Col >= 0)
                AddPick(csv.GetSpan(pick2Col), pick, currentIsTop);

            for (var i = 0; i < packCols.Length; i++)
                if (csv.GetInt(packCols[i]) > 0)
                    lastSeen[(pack, i)] = pick;
        }
        FlushDraft();

        return new Result
        {
            CardNames = cardNames,
            Cards = new Dictionary<PlayerGroup, DraftCounts[]> { [PlayerGroup.All] = all, [PlayerGroup.Top] = top },
            Rows = rows,
        };
    }
}
