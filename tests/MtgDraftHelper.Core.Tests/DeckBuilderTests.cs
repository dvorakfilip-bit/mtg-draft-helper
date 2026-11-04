using MtgDraftHelper.Core.Cards;
using MtgDraftHelper.Core.DeckBuilding;
using MtgDraftHelper.Core.Ratings;

namespace MtgDraftHelper.Core.Tests;

public class DeckBuilderTests
{
    private static int _id = 50000;

    private static CardInfo Card(string name, string manaCost, string types = "Creature", string text = "", string supertypes = "")
    {
        var colors = ManaCost.ParseColors(new string(manaCost.Where(char.IsLetter).Where(c => "WUBRG".Contains(c)).ToArray()));
        var mv = manaCost.Split('{', '}').Where(s => s.Length > 0)
            .Sum(s => int.TryParse(s, out var n) ? n : s.StartsWith("2/") ? 2 : 1);
        return new CardInfo(_id++, name, "TST", Rarity.Common, colors, manaCost, mv,
            types.Split(' '), [], supertypes.Length > 0 ? [supertypes] : [], null, null, text, true, []);
    }

    /// <summary>Pool s jasně nejsilnějším UB, slabým R a pár bílými kartami.</summary>
    private static (List<CardInfo> Pool, FakeRatings Ratings) UbPool()
    {
        var ratings = new FakeRatings();
        var pool = new List<CardInfo>();
        void Add(CardInfo c, double score)
        {
            pool.Add(c);
            ratings.Base[c.Name] = score;
        }

        var costs = new[] { "{1}{U}", "{1}{B}", "{2}{U}", "{2}{B}", "{3}{U}", "{3}{B}", "{U}{B}", "{4}{B}" };
        for (var i = 0; i < 16; i++)
            Add(Card($"UB creature {i}", costs[i % costs.Length]), 70 + i % 5);
        for (var i = 0; i < 9; i++)
            Add(Card($"UB spell {i}", i % 2 == 0 ? "{1}{U}" : "{2}{B}", "Instant", "Draw a card."), 65 + i % 4);
        for (var i = 0; i < 8; i++)
            Add(Card($"Red {i}", "{2}{R}"), 40);
        for (var i = 0; i < 6; i++)
            Add(Card($"White {i}", "{1}{W}"), 45);
        return (pool, ratings);
    }

    [Fact]
    public void PicksStrongestColorPair()
    {
        var (pool, ratings) = UbPool();
        var variants = new DeckBuilder(ratings).Build(pool);

        var best = variants[0];
        Assert.Equal(ManaColors.U | ManaColors.B, best.MainColors);
        Assert.True(best.IsComplete);
        Assert.Equal(23, best.Spells.Count);
        Assert.Equal(17, best.LandCount);
        Assert.Equal(40, best.Spells.Count + best.LandCount);
        Assert.All(best.Spells, s => Assert.True((s.Card.Colors & ~(ManaColors.U | ManaColors.B)) == ManaColors.None));
        Assert.True(variants.Count <= 3);
        Assert.Equal(variants.Count, variants.Select(v => v.MainColors).Distinct().Count());
    }

    [Fact]
    public void BasicLandsFollowManaSymbols()
    {
        var (pool, ratings) = UbPool();
        var best = new DeckBuilder(ratings).Build(pool)[0];

        Assert.Equal(17, best.BasicLands.Sum(b => b.Count));
        Assert.Contains(best.BasicLands, b => b.Name == "Island");
        Assert.Contains(best.BasicLands, b => b.Name == "Swamp");
        Assert.All(best.BasicLands, b => Assert.True(b.Count >= 3));
    }

    [Fact]
    public void EnforcesMinimumCreatures()
    {
        var ratings = new FakeRatings();
        var pool = new List<CardInfo>();
        // 20 silných kouzel a 15 slabších creatures v GB.
        for (var i = 0; i < 20; i++)
        {
            var c = Card($"Spell {i}", i % 2 == 0 ? "{1}{G}" : "{1}{B}", "Sorcery");
            pool.Add(c);
            ratings.Base[c.Name] = 75;
        }
        for (var i = 0; i < 15; i++)
        {
            var c = Card($"Bear {i}", i % 2 == 0 ? "{1}{G}" : "{2}{B}");
            pool.Add(c);
            ratings.Base[c.Name] = 62;
        }

        var deck = new DeckBuilder(ratings).BuildVariant(pool, ManaColors.B | ManaColors.G, ManaColors.None);

        Assert.True(deck.Creatures >= DeckBuilder.MinCreatures);
    }

    [Fact]
    public void AggressiveLowCurveDeckPlaysSixteenLands()
    {
        var ratings = new FakeRatings();
        var pool = new List<CardInfo>();
        for (var i = 0; i < 26; i++)
        {
            var c = Card($"Goblin {i}", i % 3 == 0 ? "{R}" : i % 2 == 0 ? "{1}{R}" : "{1}{W}");
            pool.Add(c);
            ratings.Base[c.Name] = 70;
        }

        var deck = new DeckBuilder(ratings).BuildVariant(pool, ManaColors.R | ManaColors.W, ManaColors.None);

        Assert.Equal(16, deck.LandCount);
        Assert.Equal(24, deck.Spells.Count);
    }

    [Fact]
    public void SplashNeedsFixing()
    {
        var (pool, ratings) = UbPool();
        var bomb = Card("Red Bomb", "{3}{R}", "Sorcery", "Destroy target creature.");
        pool.Add(bomb);
        ratings.Base[bomb.Name] = 99;

        var withoutFixing = new DeckBuilder(ratings).BuildVariant(pool, ManaColors.U | ManaColors.B, ManaColors.R);
        Assert.DoesNotContain(withoutFixing.Spells, s => s.Card.Name == "Red Bomb");

        var dual = Card("Red-Blue Dual", "", "Land", "{oT}: Add {oU} or {oR}.");
        pool.Add(dual);
        ratings.Base[dual.Name] = 50;
        var withFixing = new DeckBuilder(ratings).BuildVariant(pool, ManaColors.U | ManaColors.B, ManaColors.R);

        Assert.Contains(withFixing.Spells, s => s.Card.Name == "Red Bomb");
        Assert.Equal(ManaColors.R, withFixing.SplashColor);
        Assert.Contains(withFixing.NonbasicLands, l => l.Card.Name == "Red-Blue Dual");
        Assert.Contains(withFixing.BasicLands, b => b.Name == "Mountain");
        Assert.Equal(17, withFixing.LandCount);
    }

    [Fact]
    public void DoubleColoredSplashCardsAreNotSplashed()
    {
        var (pool, ratings) = UbPool();
        var heavy = Card("Heavy Red", "{R}{R}", "Sorcery");
        var dual = Card("Dual", "", "Land", "{oT}: Add {oB} or {oR}.");
        pool.AddRange([heavy, dual]);
        ratings.Base[heavy.Name] = 99;

        var deck = new DeckBuilder(ratings).BuildVariant(pool, ManaColors.U | ManaColors.B, ManaColors.R);
        Assert.DoesNotContain(deck.Spells, s => s.Card.Name == "Heavy Red");
    }

    [Fact]
    public void SuggestsCloseSwaps()
    {
        var (pool, ratings) = UbPool();
        var deck = new DeckBuilder(ratings).Build(pool)[0];

        Assert.NotEmpty(deck.Swaps);
        Assert.All(deck.Swaps, s => Assert.True(s.Out.Value - s.In.Value <= 8));
    }

    [Fact]
    public void ArchetypeWinRateIsUsed()
    {
        var (pool, ratings) = UbPool();
        var colorStats = new List<ColorStats> { new("UB", "", 10000, 6000), new("WR", "", 10000, 5000) };
        var deck = new DeckBuilder(ratings, colorStats).Build(pool)[0];
        Assert.Equal(0.6, deck.ArchetypeWinRate);
    }

    [Theory]
    [InlineData("{1}{U}{U}", ManaColors.U, 2)]
    [InlineData("{W/U}", ManaColors.U, 0.5)]
    [InlineData("{1}{B/P}", ManaColors.B, 1)]
    [InlineData("{2/R}", ManaColors.R, 1)]
    [InlineData("{3}", ManaColors.G, 0)]
    public void CountsPips(string cost, ManaColors color, double expected) =>
        Assert.Equal(expected, DeckBuilder.Pips(Card("X", cost), color));

    [Fact]
    public void DetectsProducedColorsOfLands()
    {
        Assert.Equal(ManaColors.W | ManaColors.G, DeckBuilder.ProducedColors(Card("L", "", "Land", "{oT}: Add {oG} or {oW}.")));
        Assert.Equal(ManaColors.None, DeckBuilder.ProducedColors(Card("L", "", "Land", "{oT}: Add {oC}.")));
    }
}
