using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace MtgDraftHelper.Core.Cards;

/// <summary>
/// Čtení lokální databáze karet Areny (SQLite <c>Raw_CardDatabase_*.mtga</c>).
/// Čte se jen pro čtení; soubor patří hře.
/// </summary>
public sealed partial class ArenaCardDatabase
{
    private readonly Dictionary<int, CardInfo> _byGrpId;
    private readonly ILookup<string, CardInfo> _byName;

    private ArenaCardDatabase(Dictionary<int, CardInfo> byGrpId)
    {
        _byGrpId = byGrpId;
        _byName = byGrpId.Values.Where(c => c.IsPrimaryFace).ToLookup(c => c.Name, StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _byGrpId.Count;

    public IEnumerable<CardInfo> All => _byGrpId.Values;

    public CardInfo? Get(int grpId) => _byGrpId.GetValueOrDefault(grpId);

    public IEnumerable<CardInfo> FindByName(string name) => _byName[name];

    public IEnumerable<CardInfo> InSet(string setCode) =>
        _byGrpId.Values.Where(c => string.Equals(c.SetCode, setCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>Načte nejnovější nalezenou databázi Areny.</summary>
    public static ArenaCardDatabase LoadDefault()
    {
        var files = ArenaPaths.FindCardDatabases();
        if (files.Count == 0)
            throw new FileNotFoundException("Databáze karet Areny (Raw_CardDatabase_*.mtga) nebyla nalezena.");
        return Load(files);
    }

    /// <summary>
    /// Načte jednu nebo víc databází. Při shodném GrpId vyhrává první soubor (nejnovější),
    /// starší soubory jen doplní chybějící karty.
    /// </summary>
    public static ArenaCardDatabase Load(IEnumerable<string> paths)
    {
        var cards = new Dictionary<int, CardInfo>();
        foreach (var path in paths)
            foreach (var card in ReadFile(path))
                cards.TryAdd(card.GrpId, card);
        return new ArenaCardDatabase(cards);
    }

    private static IEnumerable<CardInfo> ReadFile(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        var texts = ReadLocalizations(connection);
        var enums = ReadEnums(connection, texts);

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT GrpId, TitleId, ExpansionCode, DigitalReleaseSet, Rarity, Colors, OldSchoolManaText,
                   Types, Subtypes, Supertypes, Power, Toughness, AbilityIds, IsPrimaryCard, LinkedFaceGrpIds
            FROM Cards
            WHERE IsToken = 0
            """;
        using var reader = command.ExecuteReader();

        var result = new List<CardInfo>();
        while (reader.Read())
        {
            var titleId = reader.GetInt32(1);
            if (!texts.TryGetValue(titleId, out var name))
                continue;

            var expansion = GetString(reader, 2);
            var digitalSet = GetString(reader, 3);
            var manaText = GetString(reader, 6);

            result.Add(new CardInfo(
                GrpId: reader.GetInt32(0),
                Name: name,
                SetCode: SetCodeFor(expansion, digitalSet),
                Rarity: Enum.IsDefined(typeof(Rarity), reader.GetInt32(4)) ? (Rarity)reader.GetInt32(4) : Rarity.Unknown,
                Colors: ColorsFromEnum(GetString(reader, 5)),
                ManaCost: ManaCost.FromArena(manaText),
                ManaValue: ManaCost.ManaValue(manaText),
                Types: ResolveEnums(enums, "CardType", GetString(reader, 7)),
                Subtypes: ResolveEnums(enums, "SubType", GetString(reader, 8)),
                Supertypes: ResolveEnums(enums, "SuperType", GetString(reader, 9)),
                Power: NullIfEmpty(GetString(reader, 10)),
                Toughness: NullIfEmpty(GetString(reader, 11)),
                RulesText: AbilityText(texts, GetString(reader, 12)),
                IsPrimaryFace: reader.GetBoolean(13),
                LinkedFaceGrpIds: ParseInts(GetString(reader, 14))));
        }
        return result;
    }

    /// <summary>
    /// Texty z Localizations_enUS. Varianta Formatted=0 je bez HTML značek; pokud chybí, bere se
    /// nejnižší dostupná a značky se odstraní.
    /// </summary>
    private static Dictionary<int, string> ReadLocalizations(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT LocId, Formatted, Loc FROM Localizations_enUS WHERE Loc IS NOT NULL ORDER BY LocId, Formatted DESC";
        using var reader = command.ExecuteReader();

        var texts = new Dictionary<int, string>();
        while (reader.Read())
            texts[reader.GetInt32(0)] = CleanText(reader.GetString(2)); // díky řazení DESC zůstane nejnižší Formatted
        return texts;
    }

    private static Dictionary<(string Type, int Value), string> ReadEnums(SqliteConnection connection, Dictionary<int, string> texts)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Type, Value, LocId FROM Enums WHERE Type IN ('CardType', 'SubType', 'SuperType')";
        using var reader = command.ExecuteReader();

        var enums = new Dictionary<(string, int), string>();
        while (reader.Read())
            if (texts.TryGetValue(reader.GetInt32(2), out var text))
                enums[(reader.GetString(0), reader.GetInt32(1))] = text;
        return enums;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    internal static string CleanText(string text) =>
        TagRegex().Replace(text, "").Replace('�', '•').Trim();

    /// <summary>Alchemy karty mají ExpansionCode např. Y26 a skutečnou edici v DigitalReleaseSet.</summary>
    private static string SetCodeFor(string expansion, string digitalSet) =>
        string.IsNullOrEmpty(digitalSet) ? expansion : digitalSet;

    private static ManaColors ColorsFromEnum(string colorIds)
    {
        var colors = ManaColors.None;
        foreach (var id in ParseInts(colorIds))
            colors |= id switch
            {
                1 => ManaColors.W,
                2 => ManaColors.U,
                3 => ManaColors.B,
                4 => ManaColors.R,
                5 => ManaColors.G,
                _ => ManaColors.None,
            };
        return colors;
    }

    private static List<string> ResolveEnums(Dictionary<(string, int), string> enums, string type, string ids) =>
        ParseInts(ids).Select(id => enums.GetValueOrDefault((type, id)) ?? $"{type}#{id}").ToList();

    /// <summary>AbilityIds má tvar "abilityId:locId,abilityId:locId".</summary>
    private static string AbilityText(Dictionary<int, string> texts, string abilityIds)
    {
        var lines = new List<string>();
        foreach (var pair in abilityIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf(':');
            if (separator > 0 && int.TryParse(pair[(separator + 1)..], out var locId) && texts.TryGetValue(locId, out var text))
                lines.Add(text);
        }
        return string.Join('\n', lines);
    }

    private static List<int> ParseInts(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
            .OfType<int>()
            .ToList();

    private static string GetString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? "" : reader.GetValue(ordinal).ToString() ?? "";

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
