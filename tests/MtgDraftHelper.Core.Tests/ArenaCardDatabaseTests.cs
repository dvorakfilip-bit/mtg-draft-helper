using Microsoft.Data.Sqlite;
using MtgDraftHelper.Core.Cards;

namespace MtgDraftHelper.Core.Tests;

public sealed class ArenaCardDatabaseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "MtgDraftHelperTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>Malá databáze se stejným schématem (jen používané sloupce) jako Raw_CardDatabase.</summary>
    private string CreateDatabase(string fileName, params (int GrpId, int TitleId, string Name)[] cards)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, fileName);
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE Cards(GrpId INT PRIMARY KEY, TitleId INT, ExpansionCode TEXT, DigitalReleaseSet TEXT, Rarity INT,
                Colors TEXT, OldSchoolManaText TEXT, Types TEXT, Subtypes TEXT, Supertypes TEXT, Power TEXT, Toughness TEXT,
                AbilityIds TEXT, IsPrimaryCard BOOLEAN, LinkedFaceGrpIds TEXT, IsToken BOOLEAN);
            CREATE TABLE Localizations_enUS(LocId INT, Formatted INT, Loc TEXT, PRIMARY KEY (LocId, Formatted));
            CREATE TABLE Enums(Type TEXT, Value INT, LocId INT);
            INSERT INTO Localizations_enUS VALUES (7, 1, 'Artifact'), (8, 1, 'Creature'), (500, 1, 'Construct'),
                (28, 1, 'Trample'), (900, 0, 'When this creature enters, draw a card.'),
                (900, 1, 'When this creature enters, <i>draw</i> a card.');
            INSERT INTO Enums VALUES ('CardType', 1, 7), ('CardType', 2, 8), ('SubType', 17, 500);
            """;
        command.ExecuteNonQuery();

        foreach (var (grpId, titleId, name) in cards)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO Localizations_enUS VALUES ($title, 1, $formatted), ($title, 0, $name);
                INSERT INTO Cards VALUES ($grp, $title, 'FRA', '', 3, '4,5', 'o3oRoG', '1,2', '17', '', '7', '5',
                    '14:28,208229:900', 1, '', 0);
                """;
            insert.Parameters.AddWithValue("$grp", grpId);
            insert.Parameters.AddWithValue("$title", titleId);
            insert.Parameters.AddWithValue("$name", name);
            insert.Parameters.AddWithValue("$formatted", $"<nobr>{name}</nobr>");
            insert.ExecuteNonQuery();
        }
        return path;
    }

    [Fact]
    public void ReadsCardFields()
    {
        var path = CreateDatabase("Raw_CardDatabase_a.mtga", (106362, 1000, "Craftwork Crusher"));

        var db = ArenaCardDatabase.Load([path]);
        var card = db.Get(106362)!;

        Assert.Equal("Craftwork Crusher", card.Name);
        Assert.Equal("FRA", card.SetCode);
        Assert.Equal(Rarity.Uncommon, card.Rarity);
        Assert.Equal(ManaColors.R | ManaColors.G, card.Colors);
        Assert.Equal("{3}{R}{G}", card.ManaCost);
        Assert.Equal(5, card.ManaValue);
        Assert.Equal("Artifact Creature - Construct", card.TypeLine);
        Assert.True(card.IsCreature);
        Assert.Equal("7", card.Power);
        Assert.Equal("Trample\nWhen this creature enters, draw a card.", card.RulesText);
        Assert.Single(db.FindByName("craftwork crusher"));
    }

    [Fact]
    public void NewerDatabaseWinsAndOlderFillsGaps()
    {
        var newer = CreateDatabase("Raw_CardDatabase_new.mtga", (1, 1000, "New Name"));
        var older = CreateDatabase("Raw_CardDatabase_old.mtga", (1, 1000, "Old Name"), (2, 1001, "Only Old"));

        var db = ArenaCardDatabase.Load([newer, older]);

        Assert.Equal("New Name", db.Get(1)!.Name);
        Assert.Equal("Only Old", db.Get(2)!.Name);
    }

    [Theory]
    [InlineData("<nobr>Blossom-Blessed</nobr> Angel", "Blossom-Blessed Angel")]
    [InlineData("choose two �", "choose two •")]
    public void CleansMarkup(string raw, string expected) =>
        Assert.Equal(expected, ArenaCardDatabase.CleanText(raw));
}
