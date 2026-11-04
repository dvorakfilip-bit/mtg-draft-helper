using MtgDraftHelper.Core.Cards;

namespace MtgDraftHelper.Core.Recommendation;

public enum FactorKind
{
    /// <summary>Pro kartu nejsou data 17Lands, základ je odhad z rarity a role.</summary>
    NoData,
    OffColor,
    Archetype,
    NeedCreatures,
    NeedRemoval,
    NeedTwoDrops,
    TooManyExpensive,
    Signal,
}

/// <summary>Složka skóre. <see cref="Colors"/> = barvy, kterých se týká (např. archetyp "WU").</summary>
public sealed record ScoreFactor(FactorKind Kind, double Points, string? Colors = null);

public sealed record PickRecommendation(
    CardInfo Card,
    int Score,
    double BaseScore,
    bool HasData,
    CardRole Roles,
    IReadOnlyList<ScoreFactor> Factors);

/// <summary>Karta z historie draftu: co bylo v balíčku na daném picku.</summary>
public sealed record SeenPack(int Pack, int Pick, IReadOnlyList<CardInfo> Cards);

public sealed record DraftContext(
    IReadOnlyList<CardInfo> Pool,
    IReadOnlyList<SeenPack> SeenPacks,
    int Pack,
    int Pick,
    int PackSize,
    bool IsBotDraft,
    bool SignalDataFromSameFormat);

public sealed record DraftAnalysis(
    int OverallPick,
    int TotalPicks,
    IReadOnlyDictionary<ManaColors, double> ColorWeights,
    ManaColors TopColors,
    double Commitment,
    IReadOnlyDictionary<ManaColors, double> Signals,
    int[] Curve,
    int Creatures,
    int Removal,
    int TwoDrops,
    int Expensive);

/// <summary>
/// Skóre picku 0–100 podle PRD 4.4: základní síla karty, soulad s barvami, archetyp,
/// potřeby balíčku a signály. Váhy jsou výchozí a odladí se na skutečných draftech (M6).
/// </summary>
public sealed class PickRecommender
{
    public static readonly ManaColors[] AllColors = [ManaColors.W, ManaColors.U, ManaColors.B, ManaColors.R, ManaColors.G];

    private readonly ICardRatings _ratings;

    public PickRecommender(ICardRatings ratings) => _ratings = ratings;

    /// <summary>Maximální postih karty úplně mimo barvy při plném rozhodnutí o barvách.</summary>
    public double OffColorPenalty { get; init; } = 30;

    public double MaxArchetypeBonus { get; init; } = 6;

    public double MaxSignalBonus { get; init; } = 6;

    public double BaseScoreOf(CardInfo card) => _ratings.BaseScore(card.Name) ?? EstimateBaseScore(card);

    /// <summary>Odhad síly karty bez dat 17Lands (nová edice, Cube).</summary>
    public static double EstimateBaseScore(CardInfo card)
    {
        if (card.IsLand && card.Supertypes.Contains("Basic"))
            return 0;
        var roles = CardRoles.Of(card);
        double score = card.Rarity switch
        {
            Rarity.Mythic => 65,
            Rarity.Rare => 60,
            Rarity.Uncommon => 48,
            _ => 38,
        };
        if (roles.HasFlag(CardRole.Removal)) score += 12;
        if (roles.HasFlag(CardRole.Evasion)) score += 4;
        if (roles.HasFlag(CardRole.CardDraw)) score += 3;
        return Math.Clamp(score, 0, 100);
    }

    public DraftAnalysis Analyze(DraftContext context)
    {
        var overallPick = (context.Pack - 1) * context.PackSize + context.Pick;
        var totalPicks = context.PackSize * 3;

        var weights = AllColors.ToDictionary(c => c, _ => 0.0);
        foreach (var card in context.Pool.Where(c => !c.IsLand))
        {
            var colors = AllColors.Where(c => card.Colors.HasFlag(c)).ToList();
            if (colors.Count == 0)
                continue;
            // Slabé karty k rozhodnutí o barvách skoro nepřispívají.
            var weight = Math.Max(0, BaseScoreOf(card) - 30) / 70;
            foreach (var c in colors)
                weights[c] += weight / colors.Count;
        }

        var ordered = weights.OrderByDescending(x => x.Value).ToList();
        var top = ordered[0].Value > 0 ? ordered[0].Key : ManaColors.None;
        if (ordered[1].Value > 0)
            top |= ordered[1].Key;

        var total = weights.Values.Sum();
        var concentration = total > 0 ? (ordered[0].Value + ordered[1].Value) / total : 0;
        // Od P1p6 do zhruba P2p6 roste váha barev z 0 na plnou, podle toho, jak je pool soustředěný.
        var ramp = Ramp(overallPick, 6, context.PackSize + 6);
        var commitment = ramp * Math.Clamp((concentration - 0.4) / 0.4, 0, 1);

        var onColor = context.Pool.Where(c => FitsColors(c, top) >= 1).ToList();
        var curve = new int[7];
        foreach (var card in onColor.Where(c => !c.IsLand))
            curve[Math.Clamp(card.ManaValue, 0, 6)]++;
        var roles = onColor.Select(CardRoles.Of).ToList();

        return new DraftAnalysis(
            overallPick,
            totalPicks,
            weights,
            top,
            commitment,
            ComputeSignals(context),
            curve,
            Creatures: roles.Count(r => r.HasFlag(CardRole.Creature)),
            Removal: roles.Count(r => r.HasFlag(CardRole.Removal)),
            TwoDrops: onColor.Count(c => c.IsCreature && c.ManaValue == 2),
            Expensive: onColor.Count(c => !c.IsLand && c.ManaValue >= 5));
    }

    public IReadOnlyList<PickRecommendation> Recommend(IReadOnlyList<CardInfo> pack, DraftContext context) =>
        Recommend(pack, context, Analyze(context));

    public IReadOnlyList<PickRecommendation> Recommend(IReadOnlyList<CardInfo> pack, DraftContext context, DraftAnalysis analysis) =>
        pack.Select(card => Score(card, context, analysis))
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.BaseScore)
            .ToList();

    public PickRecommendation Score(CardInfo card, DraftContext context, DraftAnalysis analysis)
    {
        var factors = new List<ScoreFactor>();
        var ratedBase = _ratings.BaseScore(card.Name);
        var hasData = ratedBase is not null;
        var baseScore = ratedBase ?? EstimateBaseScore(card);
        if (!hasData)
            factors.Add(new ScoreFactor(FactorKind.NoData, 0));

        var roles = CardRoles.Of(card);
        var fit = FitsColors(card, analysis.TopColors);

        // Soulad s barvami.
        if (fit < 1 && analysis.Commitment > 0)
            factors.Add(new ScoreFactor(FactorKind.OffColor, -OffColorPenalty * analysis.Commitment * (1 - fit)));

        // Archetyp: síla karty v balíčcích mých dvou barev oproti všem balíčkům.
        if (fit >= 1 && card.Colors != ManaColors.None && analysis.Commitment >= 0.4 && CountColors(analysis.TopColors) == 2)
        {
            var pair = ManaCost.ToLetters(analysis.TopColors);
            var inPair = _ratings.StatsForColors(card.Name, pair);
            var overall = _ratings.Stats(card.Name);
            if (inPair is { Game.InHandGames: >= 100 } && overall is not null)
            {
                var mean = overall.Game.GihWr ?? 0.55;
                var delta = CardRatings.SmoothedGihWr(inPair.Game, mean) - CardRatings.SmoothedGihWr(overall.Game, mean);
                var points = Math.Clamp(delta * 150, -MaxArchetypeBonus, MaxArchetypeBonus) * analysis.Commitment;
                if (Math.Abs(points) >= 0.5)
                    factors.Add(new ScoreFactor(FactorKind.Archetype, points, pair));
            }
        }

        // Potřeby balíčku – až od poloviny prvního balíčku a jen pro karty v mých barvách.
        var need = Ramp(analysis.OverallPick, context.PackSize, analysis.TotalPicks * 2 / 3.0);
        if (need > 0 && fit >= 1 && !card.IsLand)
        {
            var progress = (double)analysis.OverallPick / analysis.TotalPicks;
            if (roles.HasFlag(CardRole.Creature) && analysis.Creatures < 15 * progress * 0.8)
                factors.Add(new ScoreFactor(FactorKind.NeedCreatures, 5 * need));
            if (roles.HasFlag(CardRole.Removal) && analysis.Removal < 3)
                factors.Add(new ScoreFactor(FactorKind.NeedRemoval, 4 * need));
            if (card.IsCreature && card.ManaValue == 2 && analysis.TwoDrops < 4 * progress)
                factors.Add(new ScoreFactor(FactorKind.NeedTwoDrops, 3 * need));
            if (card.ManaValue >= 5 && analysis.Expensive >= 4)
                factors.Add(new ScoreFactor(FactorKind.TooManyExpensive, -4 * need));
        }

        // Signály – otevřené barvy podle pozdě chodících silných karet.
        var signalWeight = SignalWeight(analysis.OverallPick, context);
        if (signalWeight > 0 && card.Colors != ManaColors.None)
        {
            var average = analysis.Signals.Values.Average();
            if (average > 0)
            {
                var cardSignal = AllColors.Where(c => card.Colors.HasFlag(c)).Average(c => analysis.Signals[c]);
                var points = Math.Clamp((cardSignal / average - 1) * 3, -MaxSignalBonus / 2, MaxSignalBonus) * signalWeight;
                if (Math.Abs(points) >= 0.5)
                    factors.Add(new ScoreFactor(FactorKind.Signal, points, ManaCost.ToLetters(card.Colors)));
            }
        }

        var score = Math.Clamp(baseScore + factors.Sum(f => f.Points), 0, 100);
        return new PickRecommendation(card, (int)Math.Round(score), baseScore, hasData, roles,
            factors.OrderByDescending(f => Math.Abs(f.Points)).ToList());
    }

    /// <summary>
    /// Váha signálů: u botů (Quick Draft) vyšší a déle, pokud máme ALSA přímo z Quick Draftu (PRD 4.4).
    /// </summary>
    private static double SignalWeight(int overallPick, DraftContext context)
    {
        if (context.IsBotDraft && context.SignalDataFromSameFormat)
            return 1.5 * (1 - Ramp(overallPick, 1, context.PackSize * 2 + 4));
        return 1 - Ramp(overallPick, 1, context.PackSize + 6);
    }

    /// <summary>
    /// Síla signálu pro každou barvu: silné karty, které jsou v balíčku později, než obvykle
    /// vydrží (pick &gt; ALSA). Bere se jen to, co poslali sousedé (od 2. picku balíčku).
    /// </summary>
    private Dictionary<ManaColors, double> ComputeSignals(DraftContext context)
    {
        var signals = AllColors.ToDictionary(c => c, _ => 0.0);
        foreach (var seen in context.SeenPacks.Where(p => p.Pick >= 2))
        {
            foreach (var card in seen.Cards)
            {
                var alsa = _ratings.Stats(card.Name)?.Draft.Alsa;
                var baseScore = _ratings.BaseScore(card.Name);
                if (alsa is null || baseScore is null or < 60)
                    continue;
                var lateness = seen.Pick - alsa.Value;
                if (lateness <= 0)
                    continue;
                var colors = AllColors.Where(c => card.Colors.HasFlag(c)).ToList();
                foreach (var c in colors)
                    signals[c] += lateness * (baseScore.Value - 50) / 50 / colors.Count;
            }
        }
        return signals;
    }

    /// <summary>Podíl barev karty, které patří do mých barev (bezbarvá karta = 1).</summary>
    public static double FitsColors(CardInfo card, ManaColors myColors)
    {
        if (card.Colors == ManaColors.None)
            return 1;
        if (myColors == ManaColors.None)
            return 1;
        var colors = AllColors.Where(c => card.Colors.HasFlag(c)).ToList();
        return (double)colors.Count(c => myColors.HasFlag(c)) / colors.Count;
    }

    private static int CountColors(ManaColors colors) => AllColors.Count(c => colors.HasFlag(c));

    /// <summary>Lineární nárůst 0 → 1 mezi <paramref name="from"/> a <paramref name="to"/>.</summary>
    private static double Ramp(double value, double from, double to) =>
        to <= from ? (value >= to ? 1 : 0) : Math.Clamp((value - from) / (to - from), 0, 1);
}
