using System.Diagnostics;
using MtgDraftHelper.Core;
using MtgDraftHelper.Core.Cards;
using MtgDraftHelper.Core.Logs;
using MtgDraftHelper.Core.Ratings;
using MtgDraftHelper.Core.Storage;

// Vývojářský nástroj pro ověření datové vrstvy (M1). Není součástí výsledné aplikace.

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    switch (args[0])
    {
        case "cards":
            return Cards(args.Skip(1).ToArray());
        case "import":
            return await Import(args.Skip(1).ToArray());
        case "top":
            return Top(args.Skip(1).ToArray());
        case "colors":
            return Colors(args.Skip(1).ToArray());
        case "match":
            return Match(args.Skip(1).ToArray());
        case "log":
            return Log(args.Skip(1).ToArray());
        case "watch":
            return Watch(args.Skip(1).ToArray());
        default:
            PrintUsage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Chyba: {ex.Message}");
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("""
        Použití:
          cards [SET]                          načte databázi karet Areny, vypíše počty / karty edice
          import SET FORMAT [--force]          stáhne a zpracuje datasety 17Lands
          top SET FORMAT [--group top] [--colors WU] [-n 20]
                                               karty seřazené podle GIH WR
          colors SET FORMAT [--group top]      win rate barevných kombinací
          match SET FORMAT                     kontrola párování jmen 17Lands ↔ databáze Areny
          log [CESTA] [--events]               zpracuje celý log (výchozí Player.log) a vypíše stav draftu
          watch [CESTA]                        sleduje log živě a vypisuje změny stavu (Ctrl+C ukončí)
        """);
}

static string? Option(string[] args, string name) =>
    Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;

static PlayerGroup Group(string[] args) =>
    string.Equals(Option(args, "--group"), "top", StringComparison.OrdinalIgnoreCase) ? PlayerGroup.Top : PlayerGroup.All;

static int Cards(string[] args)
{
    var sw = Stopwatch.StartNew();
    var files = ArenaPaths.FindCardDatabases();
    Console.WriteLine($"Nalezené databáze: {files.Count}");
    foreach (var f in files)
        Console.WriteLine($"  {f}");

    var db = ArenaCardDatabase.Load(files);
    Console.WriteLine($"Načteno {db.Count:N0} karet za {sw.ElapsedMilliseconds} ms.");

    if (args.Length > 0)
    {
        foreach (var card in db.InSet(args[0]).Where(c => c.IsPrimaryFace).OrderBy(c => c.GrpId).Take(15))
            Console.WriteLine($"  {card.GrpId,7} {card.Name,-32} {card.ManaCost,-14} {card.Rarity,-9} {card.TypeLine}");
    }
    return 0;
}

static async Task<int> Import(string[] args)
{
    var (set, format) = (args[0], args[1]);
    var service = new RatingsService(new StatsStore(AppPaths.StatsDatabase), new PublicDatasetClient(), AppPaths.DatasetCacheDir);
    var sw = Stopwatch.StartNew();
    var results = await service.UpdateAsync(set, format, force: args.Contains("--force"),
        progress: new Progress<string>(m => Console.WriteLine($"  {m}")));
    foreach (var r in results)
        Console.WriteLine($"{r.Kind}: {r.Status}, {r.Rows:N0} řádků, {r.Duration.TotalSeconds:N1} s");
    Console.WriteLine($"Celkem {sw.Elapsed.TotalSeconds:N1} s. Databáze: {AppPaths.StatsDatabase}");
    return 0;
}

static int Top(string[] args)
{
    var (set, format) = (args[0], args[1]);
    var colors = Option(args, "--colors") ?? "";
    var count = int.TryParse(Option(args, "-n"), out var n) ? n : 20;
    var store = new StatsStore(AppPaths.StatsDatabase);
    var stats = store.GetCardStats(set, format, Group(args), colors);

    Console.WriteLine($"{"Karta",-34} {"GIH WR",7} {"GIH #",8} {"OH WR",7} {"GD WR",7} {"IWD",6} {"ALSA",5} {"ATA",5}");
    foreach (var s in stats.Values.Where(s => s.Game.InHandGames >= 200).OrderByDescending(s => s.Game.GihWr).Take(count))
    {
        Console.WriteLine($"{s.Name,-34} {Pct(s.Game.GihWr),7} {s.Game.InHandGames,8:N0} {Pct(s.Game.OhWr),7} {Pct(s.Game.GdWr),7} " +
                          $"{Pp(s.Game.Iwd),6} {s.Draft.Alsa,5:N2} {s.Draft.Ata,5:N2}");
    }
    Console.WriteLine("Data: 17Lands (https://www.17lands.com/public_datasets, CC BY 4.0)");
    return 0;

    static string Pct(double? v) => v is null ? "-" : $"{v * 100:N1}%";
    static string Pp(double? v) => v is null ? "-" : $"{v * 100:+0.0;-0.0}";
}

static int Colors(string[] args)
{
    var store = new StatsStore(AppPaths.StatsDatabase);
    var stats = store.GetColorStats(args[0], args[1], Group(args))
        .GroupBy(c => c.MainColors)
        .Select(g => (Colors: g.Key, Games: g.Sum(c => c.Games), Wins: g.Sum(c => c.Wins)))
        .Where(c => c.Games >= 500)
        .OrderByDescending(c => (double)c.Wins / c.Games);
    foreach (var c in stats)
        Console.WriteLine($"{(c.Colors.Length == 0 ? "?" : c.Colors),-6} {100.0 * c.Wins / c.Games,6:N1}%  {c.Games,8:N0} her");
    return 0;
}

static (ArenaCardDatabase? Db, Func<string, EventInfo> ParseEvent) LoadCardsForLog()
{
    try
    {
        var db = ArenaCardDatabase.LoadDefault();
        var sets = db.All.Select(c => c.SetCode).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (db, name => EventInfo.Parse(name, sets));
    }
    catch (FileNotFoundException)
    {
        return (null, name => EventInfo.Parse(name));
    }
}

static int Log(string[] args)
{
    var path = args.FirstOrDefault(a => !a.StartsWith("--")) ?? ArenaPaths.PlayerLogPath;
    var (db, parseEvent) = LoadCardsForLog();
    var sw = Stopwatch.StartNew();

    using var monitor = new LogMonitor(path, parseEvent);
    var counts = new Dictionary<string, int>();
    monitor.EventParsed += e =>
    {
        counts[e.GetType().Name] = counts.GetValueOrDefault(e.GetType().Name) + 1;
        if (args.Contains("--events"))
            Console.WriteLine($"  {Describe(e)}");
    };
    monitor.Error += ex => Console.Error.WriteLine($"  Chyba: {ex.Message}");
    monitor.Poll();

    Console.WriteLine($"Zpracováno za {sw.ElapsedMilliseconds} ms: {path}");
    foreach (var (type, count) in counts.OrderBy(x => x.Key))
        Console.WriteLine($"  {type}: {count}");
    PrintSnapshot(monitor.Snapshot, db);
    return 0;
}

static int Watch(string[] args)
{
    var path = args.FirstOrDefault() ?? ArenaPaths.PlayerLogPath;
    var (db, parseEvent) = LoadCardsForLog();

    using var monitor = new LogMonitor(path, parseEvent);
    monitor.StateChanged += snapshot =>
    {
        Console.WriteLine($"--- {DateTime.Now:HH:mm:ss}");
        PrintSnapshot(snapshot, db);
    };
    monitor.Error += ex => Console.Error.WriteLine($"Chyba: {ex.Message}");
    Console.WriteLine($"Sleduji {path} (Ctrl+C ukončí)…");
    monitor.Start();
    PrintSnapshot(monitor.Snapshot, db);

    using var exit = new ManualResetEventSlim();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; exit.Set(); };
    exit.Wait();
    return 0;
}

static string Describe(LogEvent e) => e switch
{
    PackShown p => $"PackShown P{p.Pack}p{p.Pick} ({p.Cards.Count} karet)",
    CardsPicked p => $"CardsPicked P{p.Pack}p{p.Pick}: {string.Join(',', p.Cards)}",
    CoursesReceived c => $"CoursesReceived: {string.Join(", ", c.Courses.Select(x => $"{x.EventName}[{x.CurrentModule}, {x.CardPool.Count}]"))}",
    PoolSnapshot s => $"PoolSnapshot: {s.Cards.Count} karet",
    DraftCompleted d => $"DraftCompleted: {d.CardPool.Count} karet",
    DeckSubmitted d => $"DeckSubmitted {d.EventName}: {d.MainDeck.Sum(c => c.Quantity)} + {d.Sideboard.Sum(c => c.Quantity)}",
    _ => e.ToString(),
};

static void PrintSnapshot(DraftSnapshot s, ArenaCardDatabase? db)
{
    string Name(int id) => db?.Get(id)?.Name ?? id.ToString();

    Console.WriteLine($"Detailní logy: {(s.DetailedLogsEnabled is null ? "?" : s.DetailedLogsEnabled.Value ? "zapnuté" : "VYPNUTÉ")}");
    if (s.Event is null)
    {
        Console.WriteLine("Žádný rozehraný limited event.");
        return;
    }
    Console.WriteLine($"Event: {s.Event.EventName} ({s.Event.Format}, edice {s.Event.SetCode ?? "?"}) – fáze {s.Phase}");
    if (s.CurrentPack is { } pack)
    {
        Console.WriteLine($"Aktuální balíček P{pack.Pack}p{pack.Pick} (vybrat {pack.CardsToPick}):");
        foreach (var id in pack.Cards)
            Console.WriteLine($"  {Name(id)}");
    }
    Console.WriteLine($"Picků: {s.Picks.Count}, pool: {s.Pool.Count} karet");
    foreach (var group in s.Pool.GroupBy(Name).OrderBy(g => g.Key).Take(60))
        Console.WriteLine($"  {group.Count()}× {group.Key}");
    if (s.Deck.Count > 0)
        Console.WriteLine($"Odeslaný balíček: {s.Deck.Sum(c => c.Quantity)} karet + sideboard {s.Sideboard.Sum(c => c.Quantity)}");
}

static int Match(string[] args)
{
    var store = new StatsStore(AppPaths.StatsDatabase);
    var stats = store.GetCardStats(args[0], args[1], PlayerGroup.All);
    var db = ArenaCardDatabase.LoadDefault();
    var missing = stats.Keys.Where(name => !db.FindByName(name).Any()).OrderBy(x => x).ToList();
    Console.WriteLine($"Karet v datech 17Lands: {stats.Count}, bez protějšku v databázi Areny: {missing.Count}");
    foreach (var name in missing)
        Console.WriteLine($"  {name}");
    return 0;
}
