using System.Text.RegularExpressions;
using MtgDraftHelper.Core.Cards;
using MtgDraftHelper.Core.Ratings;
using MtgDraftHelper.Core.Recommendation;

namespace MtgDraftHelper.Core.DeckBuilding;

public sealed record DeckCardChoice(CardInfo Card, double Value);

public sealed record BasicLand(ManaColors Color, string Name, int Count);

/// <summary>Karta na hraně: slabá karta v balíčku a srovnatelná náhrada ze sideboardu.</summary>
public sealed record SwapSuggestion(DeckCardChoice Out, DeckCardChoice In);

public sealed record DeckVariant(
    ManaColors MainColors,
    ManaColors SplashColor,
    double Score,
    IReadOnlyList<DeckCardChoice> Spells,
    IReadOnlyList<DeckCardChoice> NonbasicLands,
    IReadOnlyList<BasicLand> BasicLands,
    IReadOnlyList<SwapSuggestion> Swaps,
    double? ArchetypeWinRate,
    bool IsComplete)
{
    public int LandCount => NonbasicLands.Count + BasicLands.Sum(b => b.Count);
    public int Creatures => Spells.Count(s => s.Card.IsCreature);
    public string Colors => ManaCost.ToLetters(MainColors) + (SplashColor == ManaColors.None ? "" : "+" + ManaCost.ToLetters(SplashColor));
}

/// <summary>
/// Návrh 40karetního balíčku z poolu (PRD 4.5): pro každou dvojici barev (a případný splash)
/// vybere 23 karet podle hodnoty s ohledem na počet creatures a křivku, doplní základní země
/// podle mana symbolů a varianty seřadí podle průměrné síly a síly archetypu.
/// </summary>
public sealed partial class DeckBuilder
{
    public const int DeckSize = 40;
    public const int MinCreatures = 14;
    public const int MaxCreatures = 17;

    private static readonly Dictionary<ManaColors, string> BasicNames = new()
    {
        [ManaColors.W] = "Plains",
        [ManaColors.U] = "Island",
        [ManaColors.B] = "Swamp",
        [ManaColors.R] = "Mountain",
        [ManaColors.G] = "Forest",
    };

    private readonly ICardRatings _ratings;
    private readonly PickRecommender _recommender;
    private readonly IReadOnlyList<ColorStats> _colorStats;

    public DeckBuilder(ICardRatings ratings, IReadOnlyList<ColorStats>? colorStats = null)
    {
        _ratings = ratings;
        _recommender = new PickRecommender(ratings);
        _colorStats = colorStats ?? [];
    }

    /// <summary>Nejlepší varianty balíčku, nejvýš <paramref name="count"/>.</summary>
    public IReadOnlyList<DeckVariant> Build(IReadOnlyList<CardInfo> pool, int count = 3)
    {
        var playable = pool.Where(c => !(c.IsLand && c.Supertypes.Contains("Basic"))).ToList();
        var variants = new List<DeckVariant>();

        var colors = PickRecommender.AllColors;
        for (var i = 0; i < colors.Length; i++)
        {
            for (var j = i + 1; j < colors.Length; j++)
            {
                var pair = colors[i] | colors[j];
                var plain = BuildVariant(playable, pair, ManaColors.None);
                variants.Add(plain);

                foreach (var splash in colors.Where(c => !pair.HasFlag(c)))
                {
                    var splashed = BuildVariant(playable, pair, splash);
                    // Splash má smysl, jen když balíček opravdu zlepší.
                    if (splashed.Spells.Any(s => s.Card.Colors.HasFlag(splash)) && splashed.Score > plain.Score + 0.5)
                        variants.Add(splashed);
                }
            }
        }

        return variants
            .OrderByDescending(v => v.IsComplete)
            .ThenByDescending(v => v.Score)
            .DistinctBy(v => v.MainColors)
            .Take(count)
            .ToList();
    }

    public DeckVariant BuildVariant(IReadOnlyList<CardInfo> pool, ManaColors pair, ManaColors splash)
    {
        double Value(CardInfo card) => CardValue(card, pair);

        var allowed = pair | splash;
        var lands = pool.Where(c => c.IsLand).ToList();
        var fixingSources = CountFixingSources(pool, splash);
        var splashLimit = splash == ManaColors.None ? 0 : Math.Min(3, fixingSources + 1);

        var spells = pool
            .Where(c => !c.IsLand && IsSubset(c.Colors, pair))
            .Select(c => new DeckCardChoice(c, Value(c)))
            .OrderByDescending(c => c.Value)
            .ToList();

        // Splash: silné karty s jediným symbolem barvy splashe, jen při dostatku fixingu.
        var splashCards = splash == ManaColors.None || fixingSources < 1
            ? []
            : pool.Where(c => !c.IsLand && c.Colors.HasFlag(splash) && IsSubset(c.Colors, allowed) && Pips(c, splash) <= 1)
                .Select(c => new DeckCardChoice(c, Value(c) - 5)) // splash něco stojí (konzistence)
                .Where(c => c.Value >= 70)
                .OrderByDescending(c => c.Value)
                .Take(splashLimit)
                .ToList();

        var candidates = spells.Concat(splashCards).OrderByDescending(c => c.Value).ToList();

        // Nezákladní země bereme, když vyrábí aspoň dvě barvy balíčku (duály, "any color").
        var nonbasic = lands
            .Where(l => PickRecommender.AllColors.Count(c => (ProducedColors(l) & allowed).HasFlag(c)) >= 2)
            .Select(l => new DeckCardChoice(l, Value(l)))
            .Take(3)
            .ToList();

        // Agresivní balíček s nízkou křivkou hraje 16 zemí a 24 kouzel.
        var preliminary = candidates.Take(23).ToList();
        var aggressive = preliminary.Count == 23
                         && preliminary.Average(c => c.Card.ManaValue) <= 2.6
                         && preliminary.Count(c => c.Card.IsCreature) >= 16;
        var landCount = aggressive ? 16 : 17;
        var spellCount = DeckSize - landCount;

        var deck = candidates.Take(spellCount).ToList();
        var bench = candidates.Skip(spellCount).ToList();
        Rebalance(deck, bench);

        var basics = SplitBasics(deck, nonbasic, landCount, pair, splash, fixingSources);
        var swaps = SwapSuggestions(deck, bench);

        var average = deck.Count > 0 ? deck.Average(c => c.Value) : 0;
        var archetype = ArchetypeWinRate(pair);
        var archetypeBonus = archetype is { } wr ? (wr - AverageArchetypeWinRate()) * 100 : 0;
        var score = average + Math.Clamp(archetypeBonus, -5, 5);

        return new DeckVariant(pair, splashCards.Count > 0 && deck.Any(d => d.Card.Colors.HasFlag(splash)) ? splash : ManaColors.None,
            Math.Round(score, 1),
            deck.OrderBy(c => c.Card.ManaValue).ThenByDescending(c => c.Value).ToList(),
            nonbasic, basics, swaps, archetype, IsComplete: deck.Count == spellCount);
    }

    /// <summary>Hodnota karty v balíčku dané dvojice barev: základ + síla v archetypu.</summary>
    public double CardValue(CardInfo card, ManaColors pair)
    {
        var value = _recommender.BaseScoreOf(card);
        var pairKey = ManaCost.ToLetters(pair);
        var inPair = _ratings.StatsForColors(card.Name, pairKey);
        var overall = _ratings.Stats(card.Name);
        if (inPair is { Game.InHandGames: >= 100 } && overall?.Game.GihWr is { } mean)
        {
            var delta = CardRatings.SmoothedGihWr(inPair.Game, mean) - mean;
            value += Math.Clamp(delta * 150, -6, 6);
        }
        return Math.Clamp(value, 0, 100);
    }

    /// <summary>Výměny, aby balíček měl 14–17 creatures a rozumnou křivku.</summary>
    private static void Rebalance(List<DeckCardChoice> deck, List<DeckCardChoice> bench)
    {
        // Málo creatures: nahradit nejslabší ne-creature nejlepší creature z lavičky (rozdíl max. 15 bodů).
        while (deck.Count(c => c.Card.IsCreature) < MinCreatures
               && Swap(deck, bench, outFilter: c => !c.Card.IsCreature, inFilter: c => c.Card.IsCreature, maxLoss: 15))
        {
        }

        // Moc creatures: opak (rozdíl max. 5 bodů).
        while (deck.Count(c => c.Card.IsCreature) > MaxCreatures
               && Swap(deck, bench, outFilter: c => c.Card.IsCreature, inFilter: c => !c.Card.IsCreature, maxLoss: 5))
        {
        }

        // Moc drahých karet (MV 5+): nahradit levnější kartou se stejnou rolí (creature za creature).
        while (deck.Count(c => c.Card.ManaValue >= 5) > 5
               && Swap(deck, bench, outFilter: c => c.Card.ManaValue >= 5, inFilter: c => c.Card.ManaValue <= 3, maxLoss: 10,
                   sameCreatureType: true))
        {
        }

        // Málo 2-dropů.
        while (deck.Count(c => c.Card.IsCreature && c.Card.ManaValue == 2) < 4
               && Swap(deck, bench, outFilter: c => c.Card.ManaValue >= 4, inFilter: c => c.Card.IsCreature && c.Card.ManaValue == 2,
                   maxLoss: 6))
        {
        }
    }

    private static bool Swap(List<DeckCardChoice> deck, List<DeckCardChoice> bench,
        Func<DeckCardChoice, bool> outFilter, Func<DeckCardChoice, bool> inFilter, double maxLoss, bool sameCreatureType = false)
    {
        var candidateIn = bench.Where(inFilter).OrderByDescending(c => c.Value).FirstOrDefault();
        if (candidateIn is null)
            return false;
        var candidateOut = deck
            .Where(outFilter)
            .Where(c => !sameCreatureType || c.Card.IsCreature == candidateIn.Card.IsCreature)
            .OrderBy(c => c.Value)
            .FirstOrDefault();
        if (candidateOut is null || candidateOut.Value - candidateIn.Value > maxLoss)
            return false;

        deck.Remove(candidateOut);
        deck.Add(candidateIn);
        bench.Remove(candidateIn);
        bench.Add(candidateOut);
        bench.Sort((a, b) => b.Value.CompareTo(a.Value));
        return true;
    }

    private static List<SwapSuggestion> SwapSuggestions(List<DeckCardChoice> deck, List<DeckCardChoice> bench)
    {
        var weakest = deck.OrderBy(c => c.Value).Take(3).ToList();
        var best = bench.OrderByDescending(c => c.Value).Take(3).ToList();
        var result = new List<SwapSuggestion>();
        for (var i = 0; i < Math.Min(weakest.Count, best.Count); i++)
            if (weakest[i].Value - best[i].Value <= 8)
                result.Add(new SwapSuggestion(weakest[i], best[i]));
        return result;
    }

    /// <summary>
    /// Základní země podle mana symbolů kouzel. Barva splashe dostane tolik zemí, aby měla
    /// spolu s fixingem zhruba 3 zdroje.
    /// </summary>
    private static List<BasicLand> SplitBasics(List<DeckCardChoice> deck, List<DeckCardChoice> nonbasic, int landCount,
        ManaColors pair, ManaColors splash, int fixingSources)
    {
        var basicsTotal = Math.Max(0, landCount - nonbasic.Count);
        var result = new Dictionary<ManaColors, int>();

        var splashUsed = splash != ManaColors.None && deck.Any(c => c.Card.Colors.HasFlag(splash));
        if (splashUsed)
        {
            result[splash] = Math.Clamp(3 - fixingSources, 1, 3);
            basicsTotal -= result[splash];
        }

        var mainColors = PickRecommender.AllColors.Where(c => pair.HasFlag(c)).ToList();
        var pips = mainColors.ToDictionary(c => c, c => deck.Sum(d => Pips(d.Card, c)));
        var totalPips = pips.Values.Sum();

        // Podle podílu symbolů, zaokrouhlení metodou největšího zbytku; každá barva aspoň 3 zdroje.
        var shares = mainColors.ToDictionary(c => c, c => totalPips > 0 ? basicsTotal * pips[c] / totalPips : basicsTotal / (double)mainColors.Count);
        var counts = shares.ToDictionary(x => x.Key, x => (int)Math.Floor(x.Value));
        foreach (var c in shares.OrderByDescending(x => x.Value - Math.Floor(x.Value)).Select(x => x.Key))
        {
            if (counts.Values.Sum() >= basicsTotal)
                break;
            counts[c]++;
        }
        foreach (var c in mainColors.Where(c => pips[c] > 0 && counts[c] < 3))
        {
            var donor = counts.OrderByDescending(x => x.Value).First().Key;
            var move = Math.Min(3 - counts[c], counts[donor] - 3);
            if (move > 0)
            {
                counts[donor] -= move;
                counts[c] += move;
            }
        }
        foreach (var (c, n) in counts)
            result[c] = n;

        return PickRecommender.AllColors
            .Where(c => result.GetValueOrDefault(c) > 0)
            .Select(c => new BasicLand(c, BasicNames[c], result[c]))
            .ToList();
    }

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex SymbolRegex();

    [GeneratedRegex(@"\{o?([WUBRG])\}|mana of any (one )?color", RegexOptions.IgnoreCase)]
    private static partial Regex ProducedManaRegex();

    /// <summary>Počet mana symbolů barvy v mana cost (hybrid = 0,5).</summary>
    public static double Pips(CardInfo card, ManaColors color)
    {
        var letter = ManaCost.ToLetters(color);
        double pips = 0;
        foreach (Match m in SymbolRegex().Matches(card.ManaCost))
        {
            var parts = m.Groups[1].Value.Split('/');
            var colored = parts.Where(p => p.Length == 1 && "WUBRG".Contains(p)).ToList();
            if (colored.Contains(letter))
                pips += 1.0 / colored.Count;
        }
        return pips;
    }

    /// <summary>Barvy many, které země vyrábí (z textu schopností).</summary>
    public static ManaColors ProducedColors(CardInfo land)
    {
        var colors = ManaColors.None;
        foreach (Match m in ProducedManaRegex().Matches(land.RulesText))
        {
            if (m.Groups[1].Success)
                colors |= ManaCost.FromLetter(m.Groups[1].Value[0]);
            else
                colors |= ManaColors.W | ManaColors.U | ManaColors.B | ManaColors.R | ManaColors.G;
        }
        return colors;
    }

    /// <summary>Zdroje barvy splashe mimo základní země: duální země a fixing v poolu.</summary>
    private static int CountFixingSources(IReadOnlyList<CardInfo> pool, ManaColors splash)
    {
        if (splash == ManaColors.None)
            return 0;
        return pool.Count(c =>
            (c.IsLand && !c.Supertypes.Contains("Basic") && ProducedColors(c).HasFlag(splash))
            || (!c.IsLand && CardRoles.Of(c).HasFlag(CardRole.ManaFixing)));
    }

    private double? ArchetypeWinRate(ManaColors pair)
    {
        var key = ManaCost.ToLetters(pair);
        var rows = _colorStats.Where(c => c.MainColors == key).ToList();
        var games = rows.Sum(r => r.Games);
        return games >= 500 ? (double)rows.Sum(r => r.Wins) / games : null;
    }

    private double AverageArchetypeWinRate()
    {
        var pairs = _colorStats.Where(c => c.MainColors.Length == 2).ToList();
        var games = pairs.Sum(r => r.Games);
        return games > 0 ? (double)pairs.Sum(r => r.Wins) / games : 0.55;
    }

    private static bool IsSubset(ManaColors colors, ManaColors allowed) => (colors & ~allowed) == ManaColors.None;
}
