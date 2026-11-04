using MtgDraftHelper.Core.Cards;
using MtgDraftHelper.Core.Logs;
using MtgDraftHelper.Core.Ratings;
using MtgDraftHelper.Core.Recommendation;

namespace MtgDraftHelper.Core.Tests;

internal sealed class FakeRatings : ICardRatings
{
    public Dictionary<string, double> Base { get; } = new();
    public Dictionary<string, CardStats> All { get; } = new();
    public Dictionary<(string Name, string Colors), CardStats> ByColors { get; } = new();

    public double? BaseScore(string cardName) => Base.TryGetValue(cardName, out var s) ? s : null;
    public CardStats? Stats(string cardName) => All.GetValueOrDefault(cardName);
    public CardStats? StatsForColors(string cardName, string colors) => ByColors.GetValueOrDefault((cardName, colors));
}

internal static class TestCards
{
    private static int _nextId = 1000;

    public static CardInfo Card(string name, string colors = "", int mv = 3, string types = "Creature", string text = "",
        Rarity rarity = Rarity.Common) =>
        new(_nextId++, name, "TST", rarity, ManaCost.ParseColors(colors), "", mv,
            types.Split(' '), [], [], null, null, text, true, []);

    public static CardStats Stats(string name, long gihGames, double gihWr, double? alsa = null) =>
        new(name,
            new GameCounts(gihGames, (long)(gihGames * gihWr), 0, 0, 0, 0, gihGames, (long)(gihGames * gihWr), 0, 0),
            alsa is null ? default : new DraftCounts(100, (long)(alsa * 100), 0, 0));
}

public class CardRolesTests
{
    [Theory]
    [InlineData("Destroy target creature.", CardRole.Removal)]
    [InlineData("Exile target nonland permanent an opponent controls.", CardRole.Removal)]
    [InlineData("This spell deals 3 damage to any target.", CardRole.Removal)]
    [InlineData("Target creature gets -3/-3 until end of turn.", CardRole.Removal)]
    [InlineData("Target creature you control deals damage equal to its power to target creature you don't control.", CardRole.Removal)]
    [InlineData("Enchanted creature can't attack or block.", CardRole.Removal)]
    [InlineData("Counter target spell.", CardRole.Counterspell)]
    [InlineData("Draw two cards.", CardRole.CardDraw)]
    public void DetectsSpellRoles(string text, CardRole expected) =>
        Assert.True(CardRoles.Of(TestCards.Card("X", "B", types: "Instant", text: text)).HasFlag(expected));

    [Fact]
    public void DetectsCombatTrickButNotOnSorcery()
    {
        Assert.True(CardRoles.Of(TestCards.Card("T", "G", types: "Instant", text: "Target creature gets +2/+2 until end of turn.")).HasFlag(CardRole.CombatTrick));
        Assert.False(CardRoles.Of(TestCards.Card("T", "G", types: "Sorcery", text: "Target creature gets +2/+2 until end of turn.")).HasFlag(CardRole.CombatTrick));
    }

    [Fact]
    public void DetectsCreatureAndEvasion()
    {
        var roles = CardRoles.Of(TestCards.Card("Bird", "W", text: "Flying"));
        Assert.True(roles.HasFlag(CardRole.Creature));
        Assert.True(roles.HasFlag(CardRole.Evasion));
    }

    [Fact]
    public void PlainCreatureHasNoSpellRoles() =>
        Assert.Equal(CardRole.Creature, CardRoles.Of(TestCards.Card("Bear", "G", text: "")));
}

public class PickRecommenderTests
{
    private static DraftContext Context(IReadOnlyList<CardInfo> pool, int pack, int pick, IReadOnlyList<SeenPack>? seen = null, bool bot = false) =>
        new(pool, seen ?? [], pack, pick, 14, bot, SignalDataFromSameFormat: bot);

    [Fact]
    public void FirstPickIsByBaseScoreOnly()
    {
        var ratings = new FakeRatings { Base = { ["Bomb"] = 95, ["Filler"] = 40 } };
        var recommender = new PickRecommender(ratings);
        var pack = new[] { TestCards.Card("Filler", "W"), TestCards.Card("Bomb", "B") };

        var result = recommender.Recommend(pack, Context([], 1, 1));

        Assert.Equal("Bomb", result[0].Card.Name);
        Assert.Equal(95, result[0].Score);
        Assert.Equal(40, result[1].Score);
    }

    [Fact]
    public void OffColorCardsArePenalizedOnceCommitted()
    {
        var ratings = new FakeRatings { Base = { ["W1"] = 80, ["U1"] = 80, ["Red"] = 85, ["White"] = 70 } };
        for (var i = 0; i < 8; i++)
        {
            ratings.Base[$"pw{i}"] = 80;
            ratings.Base[$"pu{i}"] = 80;
        }
        var pool = Enumerable.Range(0, 8).SelectMany(i => new[] { TestCards.Card($"pw{i}", "W"), TestCards.Card($"pu{i}", "U") }).ToList();
        var recommender = new PickRecommender(ratings);
        var pack = new[] { TestCards.Card("Red", "R"), TestCards.Card("White", "W") };

        var result = recommender.Recommend(pack, Context(pool, 2, 3));

        Assert.Equal("White", result[0].Card.Name);
        var red = result.Single(r => r.Card.Name == "Red");
        Assert.Contains(red.Factors, f => f.Kind == FactorKind.OffColor && f.Points < -20);
    }

    [Fact]
    public void ColorlessCardsAreNeverOffColor()
    {
        var ratings = new FakeRatings { Base = { ["Artifact"] = 60, ["p"] = 80 } };
        var pool = Enumerable.Range(0, 10).Select(_ => TestCards.Card("p", "W")).ToList();
        var result = new PickRecommender(ratings).Recommend([TestCards.Card("Artifact", "")], Context(pool, 2, 5));
        Assert.Equal(60, result[0].Score);
    }

    [Fact]
    public void CardsWithoutDataGetEstimate()
    {
        var result = new PickRecommender(new FakeRatings()).Recommend(
            [TestCards.Card("Unknown Rare", "B", types: "Instant", text: "Destroy target creature.", rarity: Rarity.Rare)],
            Context([], 1, 1));

        Assert.False(result[0].HasData);
        Assert.Contains(result[0].Factors, f => f.Kind == FactorKind.NoData);
        Assert.Equal(72, result[0].Score); // rare 60 + removal 12
    }

    [Fact]
    public void OpenColorSignalBoostsEarlyPicks()
    {
        var ratings = new FakeRatings { Base = { ["Blue A"] = 60, ["Red A"] = 60, ["Late Blue Bomb"] = 90 } };
        ratings.All["Late Blue Bomb"] = TestCards.Stats("Late Blue Bomb", 1000, 0.62, alsa: 1.5);
        // Silná modrá karta přišla až na 5. picku → modrá je otevřená.
        var seen = new[] { new SeenPack(1, 5, [TestCards.Card("Late Blue Bomb", "U")]) };

        var result = new PickRecommender(ratings).Recommend(
            [TestCards.Card("Red A", "R"), TestCards.Card("Blue A", "U")], Context([], 1, 6, seen));

        Assert.Equal("Blue A", result[0].Card.Name);
        Assert.Contains(result[0].Factors, f => f.Kind == FactorKind.Signal && f.Points > 0);
    }

    [Fact]
    public void BotDraftWeighsSignalsLonger()
    {
        var ratings = new FakeRatings { Base = { ["Blue A"] = 60, ["Late Blue Bomb"] = 90, ["Red A"] = 60 } };
        ratings.All["Late Blue Bomb"] = TestCards.Stats("Late Blue Bomb", 1000, 0.62, alsa: 1.5);
        var seen = new[] { new SeenPack(1, 5, [TestCards.Card("Late Blue Bomb", "U")]) };
        var pack = new[] { TestCards.Card("Red A", "R"), TestCards.Card("Blue A", "U") };

        // P2p8 = pick 22: u lidí už signály nehrají roli, u botů ano.
        var human = new PickRecommender(ratings).Recommend(pack, Context([], 2, 8, seen, bot: false));
        var bot = new PickRecommender(ratings).Recommend(pack, Context([], 2, 8, seen, bot: true));

        Assert.DoesNotContain(human.SelectMany(r => r.Factors), f => f.Kind == FactorKind.Signal);
        Assert.Contains(bot.Single(r => r.Card.Name == "Blue A").Factors, f => f.Kind == FactorKind.Signal);
    }

    [Fact]
    public void RewardsMissingRemovalLateInDraft()
    {
        var ratings = new FakeRatings { Base = { ["Kill"] = 60, ["Bear"] = 60 } };
        for (var i = 0; i < 12; i++)
            ratings.Base[$"c{i}"] = 70;
        var pool = Enumerable.Range(0, 24).Select(i => TestCards.Card($"c{i % 12}", i % 2 == 0 ? "B" : "R", mv: 3)).ToList();

        var result = new PickRecommender(ratings).Recommend(
            [TestCards.Card("Bear", "B", mv: 3, text: ""), TestCards.Card("Kill", "B", types: "Instant", text: "Destroy target creature.")],
            Context(pool, 2, 10));

        Assert.Equal("Kill", result[0].Card.Name);
        Assert.Contains(result[0].Factors, f => f.Kind == FactorKind.NeedRemoval);
    }

    [Fact]
    public void AnalysisFindsTopColors()
    {
        var ratings = new FakeRatings();
        for (var i = 0; i < 6; i++)
        {
            ratings.Base[$"g{i}"] = 80;
            ratings.Base[$"b{i}"] = 75;
        }
        ratings.Base["w"] = 40;
        var pool = Enumerable.Range(0, 6).SelectMany(i => new[] { TestCards.Card($"g{i}", "G", mv: 2), TestCards.Card($"b{i}", "B") })
            .Append(TestCards.Card("w", "W")).ToList();

        var analysis = new PickRecommender(ratings).Analyze(Context(pool, 2, 1));

        Assert.Equal(ManaColors.B | ManaColors.G, analysis.TopColors);
        Assert.True(analysis.Commitment > 0.5);
        Assert.Equal(15, analysis.OverallPick);
        Assert.Equal(42, analysis.TotalPicks);
        Assert.Equal(6, analysis.TwoDrops);
    }

    [Fact]
    public void PicksPerPackHandlesPickTwo()
    {
        var snapshot = new DraftSnapshot(null, "d", DraftPhase.Drafting, null,
            [new PickRecord(1, 1, Enumerable.Range(1, 14).ToList(), [1, 2])], [1, 2], [], [], true);
        Assert.Equal(7, DraftContextFactory.PicksPerPack(snapshot));
    }
}
