using MtgDraftHelper.Core.Csv;
using MtgDraftHelper.Core.Ratings;
using MtgDraftHelper.Core.Storage;

namespace MtgDraftHelper.Core.Tests;

public sealed class StatsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "MtgDraftHelperTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void SavesAndReadsStats()
    {
        var store = new StatsStore(Path.Combine(_dir, "stats.db"));

        var game = new GameDataAggregator().Aggregate(new CsvReader(new StringReader("""
            main_colors,splash_colors,won,deck_A,opening_hand_A,drawn_A,user_game_win_rate_bucket
            WU,,True,1,1,0,0.5
            WU,,False,1,0,0,0.5

            """)));
        var draft = new DraftDataAggregator().Aggregate(new CsvReader(new StringReader("""
            draft_id,pack_number,pick_number,pick,pack_card_A
            d1,0,3,A,1

            """)));

        var modified = new DateTimeOffset(2026, 10, 19, 16, 20, 26, TimeSpan.Zero);
        store.SaveGameData("FRA", "PremierDraft", game, modified);
        store.SaveDraftData("FRA", "PremierDraft", draft, modified);

        var stats = store.GetCardStats("FRA", "PremierDraft", PlayerGroup.All);
        var a = stats["A"];
        Assert.Equal(2, a.Game.DeckGames);
        Assert.Equal(1.0, a.Game.GihWr);
        Assert.Equal(4.0, a.Draft.Ata);

        Assert.Equal(2, store.GetCardStats("FRA", "PremierDraft", PlayerGroup.All, "WU")["A"].Game.DeckGames);
        Assert.Equal(modified, store.GetDatasetInfo("FRA", "PremierDraft", DatasetKind.Game)!.LastModified);
        Assert.Null(store.GetDatasetInfo("FRA", "QuickDraft", DatasetKind.Game));

        var colors = store.GetColorStats("FRA", "PremierDraft", PlayerGroup.All);
        Assert.Equal(new ColorStats("WU", "", 2, 1), colors.Single());
    }

    [Fact]
    public void SavingAgainReplacesPreviousData()
    {
        var store = new StatsStore(Path.Combine(_dir, "stats.db"));
        var csv = "main_colors,splash_colors,won,deck_A\nWU,,True,1\n";

        store.SaveGameData("FRA", "PremierDraft", new GameDataAggregator().Aggregate(new CsvReader(new StringReader(csv))), null);
        store.SaveGameData("FRA", "PremierDraft", new GameDataAggregator().Aggregate(new CsvReader(new StringReader(csv))), null);

        Assert.Equal(1, store.GetCardStats("FRA", "PremierDraft", PlayerGroup.All)["A"].Game.DeckGames);
    }
}
