using System.Globalization;
using Microsoft.Data.Sqlite;
using MtgDraftHelper.Core.Ratings;

namespace MtgDraftHelper.Core.Storage;

/// <summary>
/// Lokální SQLite se zpracovanými statistikami 17Lands. Ukládají se surové počty,
/// win rate se dopočítávají při čtení.
/// </summary>
public sealed class StatsStore
{
    private readonly string _connectionString;

    public StatsStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        EnsureSchema();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS datasets (
                set_code TEXT NOT NULL, format TEXT NOT NULL, kind TEXT NOT NULL,
                last_modified TEXT, processed_utc TEXT NOT NULL, rows INTEGER NOT NULL,
                PRIMARY KEY (set_code, format, kind));
            CREATE TABLE IF NOT EXISTS card_game_stats (
                set_code TEXT NOT NULL, format TEXT NOT NULL, player_group INTEGER NOT NULL, colors TEXT NOT NULL, name TEXT NOT NULL,
                deck_games INTEGER NOT NULL, deck_wins INTEGER NOT NULL,
                oh_games INTEGER NOT NULL, oh_wins INTEGER NOT NULL,
                gd_games INTEGER NOT NULL, gd_wins INTEGER NOT NULL,
                gih_games INTEGER NOT NULL, gih_wins INTEGER NOT NULL,
                gns_games INTEGER NOT NULL, gns_wins INTEGER NOT NULL,
                PRIMARY KEY (set_code, format, player_group, colors, name));
            CREATE TABLE IF NOT EXISTS card_draft_stats (
                set_code TEXT NOT NULL, format TEXT NOT NULL, player_group INTEGER NOT NULL, name TEXT NOT NULL,
                seen_count INTEGER NOT NULL, seen_pick_sum INTEGER NOT NULL,
                pick_count INTEGER NOT NULL, pick_sum INTEGER NOT NULL,
                PRIMARY KEY (set_code, format, player_group, name));
            CREATE TABLE IF NOT EXISTS color_stats (
                set_code TEXT NOT NULL, format TEXT NOT NULL, player_group INTEGER NOT NULL,
                main_colors TEXT NOT NULL, splash_colors TEXT NOT NULL, games INTEGER NOT NULL, wins INTEGER NOT NULL,
                PRIMARY KEY (set_code, format, player_group, main_colors, splash_colors));
            """;
        command.ExecuteNonQuery();
    }

    public void SaveGameData(string setCode, string format, GameDataAggregator.Result result, DateTimeOffset? lastModified)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        Delete(connection, "card_game_stats", setCode, format);
        Delete(connection, "color_stats", setCode, format);

        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO card_game_stats VALUES ($set, $format, $group, $colors, $name,
                    $dg, $dw, $og, $ow, $gg, $gw, $ig, $iw, $ng, $nw)
                """;
            var p = AddParameters(insert, "$set", "$format", "$group", "$colors", "$name",
                "$dg", "$dw", "$og", "$ow", "$gg", "$gw", "$ig", "$iw", "$ng", "$nw");
            p["$set"].Value = setCode;
            p["$format"].Value = format;

            foreach (var ((group, colors), counts) in result.Cards)
            {
                p["$group"].Value = (int)group;
                p["$colors"].Value = colors;
                for (var i = 0; i < counts.Length; i++)
                {
                    var c = counts[i];
                    if (c.DeckGames == 0)
                        continue;
                    p["$name"].Value = result.CardNames[i];
                    p["$dg"].Value = c.DeckGames;
                    p["$dw"].Value = c.DeckWins;
                    p["$og"].Value = c.OpeningHandGames;
                    p["$ow"].Value = c.OpeningHandWins;
                    p["$gg"].Value = c.DrawnGames;
                    p["$gw"].Value = c.DrawnWins;
                    p["$ig"].Value = c.InHandGames;
                    p["$iw"].Value = c.InHandWins;
                    p["$ng"].Value = c.NotDrawnGames;
                    p["$nw"].Value = c.NotDrawnWins;
                    insert.ExecuteNonQuery();
                }
            }
        }

        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO color_stats VALUES ($set, $format, $group, $main, $splash, $games, $wins)";
            var p = AddParameters(insert, "$set", "$format", "$group", "$main", "$splash", "$games", "$wins");
            p["$set"].Value = setCode;
            p["$format"].Value = format;
            foreach (var ((group, main, splash), (games, wins)) in result.Colors)
            {
                p["$group"].Value = (int)group;
                p["$main"].Value = main;
                p["$splash"].Value = splash;
                p["$games"].Value = games;
                p["$wins"].Value = wins;
                insert.ExecuteNonQuery();
            }
        }

        SaveDatasetInfo(connection, setCode, format, DatasetKind.Game, lastModified, result.Rows);
        transaction.Commit();
    }

    public void SaveDraftData(string setCode, string format, DraftDataAggregator.Result result, DateTimeOffset? lastModified)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        Delete(connection, "card_draft_stats", setCode, format);

        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO card_draft_stats VALUES ($set, $format, $group, $name, $sc, $ss, $pc, $ps)";
            var p = AddParameters(insert, "$set", "$format", "$group", "$name", "$sc", "$ss", "$pc", "$ps");
            p["$set"].Value = setCode;
            p["$format"].Value = format;
            foreach (var (group, counts) in result.Cards)
            {
                p["$group"].Value = (int)group;
                for (var i = 0; i < counts.Length; i++)
                {
                    var c = counts[i];
                    if (c.SeenCount == 0 && c.PickCount == 0)
                        continue;
                    p["$name"].Value = result.CardNames[i];
                    p["$sc"].Value = c.SeenCount;
                    p["$ss"].Value = c.SeenPickSum;
                    p["$pc"].Value = c.PickCount;
                    p["$ps"].Value = c.PickSum;
                    insert.ExecuteNonQuery();
                }
            }
        }

        SaveDatasetInfo(connection, setCode, format, DatasetKind.Draft, lastModified, result.Rows);
        transaction.Commit();
    }

    /// <summary>
    /// Statistiky karet podle jména. <paramref name="colors"/> prázdné = všechny balíčky,
    /// jinak hlavní barvy balíčku (např. "WU"). ALSA/ATA nejsou rozdělené podle barev.
    /// </summary>
    public IReadOnlyDictionary<string, CardStats> GetCardStats(string setCode, string format, PlayerGroup group, string colors = "")
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT n.name,
                   g.deck_games, g.deck_wins, g.oh_games, g.oh_wins, g.gd_games, g.gd_wins,
                   g.gih_games, g.gih_wins, g.gns_games, g.gns_wins,
                   d.seen_count, d.seen_pick_sum, d.pick_count, d.pick_sum
            FROM (SELECT name FROM card_game_stats WHERE set_code = $set AND format = $format AND player_group = $group AND colors = $colors
                  UNION
                  SELECT name FROM card_draft_stats WHERE set_code = $set AND format = $format AND player_group = $group) n
            LEFT JOIN card_game_stats g
                   ON g.set_code = $set AND g.format = $format AND g.player_group = $group AND g.colors = $colors AND g.name = n.name
            LEFT JOIN card_draft_stats d
                   ON d.set_code = $set AND d.format = $format AND d.player_group = $group AND d.name = n.name
            """;
        command.Parameters.AddWithValue("$set", setCode);
        command.Parameters.AddWithValue("$format", format);
        command.Parameters.AddWithValue("$group", (int)group);
        command.Parameters.AddWithValue("$colors", colors);

        var result = new Dictionary<string, CardStats>(StringComparer.OrdinalIgnoreCase);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            long L(int i) => reader.IsDBNull(i) ? 0 : reader.GetInt64(i);
            var name = reader.GetString(0);
            result[name] = new CardStats(
                name,
                new GameCounts(L(1), L(2), L(3), L(4), L(5), L(6), L(7), L(8), L(9), L(10)),
                new DraftCounts(L(11), L(12), L(13), L(14)));
        }
        return result;
    }

    public IReadOnlyList<ColorStats> GetColorStats(string setCode, string format, PlayerGroup group)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT main_colors, splash_colors, games, wins FROM color_stats
            WHERE set_code = $set AND format = $format AND player_group = $group
            ORDER BY games DESC
            """;
        command.Parameters.AddWithValue("$set", setCode);
        command.Parameters.AddWithValue("$format", format);
        command.Parameters.AddWithValue("$group", (int)group);

        var result = new List<ColorStats>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(new ColorStats(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3)));
        return result;
    }

    public DatasetInfo? GetDatasetInfo(string setCode, string format, DatasetKind kind)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_modified, processed_utc, rows FROM datasets WHERE set_code = $set AND format = $format AND kind = $kind";
        command.Parameters.AddWithValue("$set", setCode);
        command.Parameters.AddWithValue("$format", format);
        command.Parameters.AddWithValue("$kind", kind.ToString());

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return new DatasetInfo(
            setCode, format, kind,
            reader.IsDBNull(0) ? null : DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
            reader.GetInt64(2));
    }

    private static void SaveDatasetInfo(SqliteConnection connection, string setCode, string format, DatasetKind kind, DateTimeOffset? lastModified, long rows)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO datasets VALUES ($set, $format, $kind, $modified, $processed, $rows)
            """;
        command.Parameters.AddWithValue("$set", setCode);
        command.Parameters.AddWithValue("$format", format);
        command.Parameters.AddWithValue("$kind", kind.ToString());
        command.Parameters.AddWithValue("$modified", (object?)lastModified?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$processed", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rows", rows);
        command.ExecuteNonQuery();
    }

    private static void Delete(SqliteConnection connection, string table, string setCode, string format)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE set_code = $set AND format = $format";
        command.Parameters.AddWithValue("$set", setCode);
        command.Parameters.AddWithValue("$format", format);
        command.ExecuteNonQuery();
    }

    private static Dictionary<string, SqliteParameter> AddParameters(SqliteCommand command, params string[] names) =>
        names.ToDictionary(n => n, n =>
        {
            // Typ parametru se odvodí z hodnoty (text / integer).
            var parameter = command.CreateParameter();
            parameter.ParameterName = n;
            command.Parameters.Add(parameter);
            return parameter;
        });
}
