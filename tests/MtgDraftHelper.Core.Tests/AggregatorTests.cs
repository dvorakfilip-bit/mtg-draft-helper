using MtgDraftHelper.Core.Csv;
using MtgDraftHelper.Core.Ratings;

namespace MtgDraftHelper.Core.Tests;

public class GameDataAggregatorTests
{
    // Dvě karty, čtyři hry. "Alpha, the First" ověřuje jméno s čárkou v hlavičce.
    private const string GameCsv = """
        expansion,event_type,main_colors,splash_colors,won,"deck_Alpha, the First","opening_hand_Alpha, the First","drawn_Alpha, the First",deck_Beta,opening_hand_Beta,drawn_Beta,user_game_win_rate_bucket
        FRA,PremierDraft,WU,,True,1,1,0,0,0,0,0.62
        FRA,PremierDraft,WU,,False,2,0,1,1,0,0,0.5
        FRA,PremierDraft,BR,G,True,1,0,0,1,0,2,0.7000000000000001
        FRA,PremierDraft,BR,,False,0,0,0,1,1,0,0.4

        """;

    private static GameDataAggregator.Result Aggregate() =>
        new GameDataAggregator().Aggregate(new CsvReader(new StringReader(GameCsv)));

    [Fact]
    public void CountsAllDecks()
    {
        var result = Aggregate();
        Assert.Equal(["Alpha, the First", "Beta"], result.CardNames);
        Assert.Equal(4, result.Rows);

        var alpha = result.Cards[(PlayerGroup.All, "")][0];
        Assert.Equal(3, alpha.DeckGames);
        Assert.Equal(2, alpha.DeckWins);
        Assert.Equal(1, alpha.OpeningHandGames);
        Assert.Equal(1, alpha.DrawnGames);
        Assert.Equal(2, alpha.InHandGames);
        Assert.Equal(1, alpha.InHandWins);
        Assert.Equal(1, alpha.NotDrawnGames);
        Assert.Equal(1, alpha.NotDrawnWins);
        Assert.Equal(0.5, alpha.GihWr);
        Assert.Equal(-0.5, alpha.Iwd);
    }

    [Fact]
    public void SplitsByMainColors()
    {
        var result = Aggregate();
        var betaInBr = result.Cards[(PlayerGroup.All, "BR")][1];
        Assert.Equal(2, betaInBr.DeckGames);
        Assert.Equal(2, betaInBr.InHandGames);
        Assert.Equal(1, betaInBr.InHandWins);
        Assert.Equal(0, result.Cards[(PlayerGroup.All, "WU")][1].InHandGames);
    }

    [Fact]
    public void TopGroupUsesWinRateBucket()
    {
        var result = Aggregate();
        var alphaTop = result.Cards[(PlayerGroup.Top, "")][0];
        Assert.Equal(2, alphaTop.DeckGames); // hry s bucketem 0.62 a 0.70
        Assert.Equal(2, alphaTop.DeckWins);
    }

    [Fact]
    public void CountsColorPairs()
    {
        var result = Aggregate();
        Assert.Equal((2L, 1L), result.Colors[(PlayerGroup.All, "WU", "")]);
        Assert.Equal((1L, 1L), result.Colors[(PlayerGroup.All, "BR", "G")]);
        Assert.Equal((1L, 0L), result.Colors[(PlayerGroup.All, "BR", "")]);
    }

    [Fact]
    public void MissingColumnFails()
    {
        var csv = new CsvReader(new StringReader("won,deck_A\nTrue,1\n"));
        Assert.Throws<InvalidDataException>(() => new GameDataAggregator().Aggregate(csv));
    }
}

public class DraftDataAggregatorTests
{
    // Dva drafty po jednom balíčku. Picky jsou v datech od 0, ALSA/ATA od 1.
    private const string DraftCsv = """
        draft_id,pack_number,pick_number,pick,pick_2,pack_card_A,pack_card_B,pack_card_C,user_game_win_rate_bucket
        d1,0,0,A,,1,1,1,0.66
        d1,0,1,C,,0,1,1,0.66
        d1,0,2,B,,0,1,0,0.66
        d2,0,0,B,,1,1,0,0.3
        d2,0,1,A,,1,0,0,0.3

        """;

    [Fact]
    public void ComputesAlsaAndAta()
    {
        var result = new DraftDataAggregator().Aggregate(new CsvReader(new StringReader(DraftCsv)));
        var all = result.Cards[PlayerGroup.All];

        // A: vzata na picku 1 (d1) a 2 (d2); naposledy viděna na picku 1 (d1) a 2 (d2).
        Assert.Equal(1.5, all[0].Ata);
        Assert.Equal(1.5, all[0].Alsa);
        // B: vzata na picku 3 (d1) a 1 (d2); naposledy viděna na picku 3 (d1) a 1 (d2).
        Assert.Equal(2.0, all[1].Ata);
        Assert.Equal(2.0, all[1].Alsa);
        // C: jen d1, vzata na picku 2.
        Assert.Equal(1, all[2].PickCount);
        Assert.Equal(2.0, all[2].Alsa);
    }

    [Fact]
    public void TopGroupContainsOnlyTopDrafts()
    {
        var result = new DraftDataAggregator().Aggregate(new CsvReader(new StringReader(DraftCsv)));
        var top = result.Cards[PlayerGroup.Top];
        Assert.Equal(1, top[0].PickCount);
        Assert.Equal(1.0, top[0].Ata);
    }

    [Fact]
    public void CountsSecondPickInPickTwoDraft()
    {
        const string csv = """
            draft_id,pack_number,pick_number,pick,pick_2,pack_card_A,pack_card_B
            d1,0,0,A,B,1,1

            """;
        var result = new DraftDataAggregator().Aggregate(new CsvReader(new StringReader(csv)));
        Assert.Equal(1, result.Cards[PlayerGroup.All][1].PickCount);
    }
}
