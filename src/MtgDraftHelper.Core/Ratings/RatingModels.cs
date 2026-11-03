namespace MtgDraftHelper.Core.Ratings;

/// <summary>Skupina hráčů, pro kterou se statistiky počítají.</summary>
public enum PlayerGroup
{
    All = 0,
    Top = 1,
}

public enum DatasetKind
{
    Game,
    Draft,
}

/// <summary>Počty z herních dat pro jednu kartu. Win rate se počítají až z počtů.</summary>
public record struct GameCounts(
    long DeckGames, long DeckWins,
    long OpeningHandGames, long OpeningHandWins,
    long DrawnGames, long DrawnWins,
    long InHandGames, long InHandWins,
    long NotDrawnGames, long NotDrawnWins)
{
    /// <summary>GP WR – hry, kdy byla karta v balíčku.</summary>
    public readonly double? GpWr => Rate(DeckWins, DeckGames);

    /// <summary>OH WR – karta v úvodní ruce.</summary>
    public readonly double? OhWr => Rate(OpeningHandWins, OpeningHandGames);

    /// <summary>GD WR – karta líznutá později (ne v úvodní ruce).</summary>
    public readonly double? GdWr => Rate(DrawnWins, DrawnGames);

    /// <summary>GIH WR – karta v úvodní ruce nebo líznutá.</summary>
    public readonly double? GihWr => Rate(InHandWins, InHandGames);

    /// <summary>GNS WR – karta v balíčku, ale nikdy v ruce.</summary>
    public readonly double? GnsWr => Rate(NotDrawnWins, NotDrawnGames);

    /// <summary>IWD – o kolik roste win rate, když se karta dostane do ruky.</summary>
    public readonly double? Iwd => GihWr - GnsWr;

    private static double? Rate(long wins, long games) => games > 0 ? (double)wins / games : null;
}

/// <summary>Počty z draftových dat pro jednu kartu.</summary>
public record struct DraftCounts(long SeenCount, long SeenPickSum, long PickCount, long PickSum)
{
    /// <summary>ALSA – průměrné číslo picku (od 1), kdy byla karta v balíčku viděna naposledy.</summary>
    public readonly double? Alsa => SeenCount > 0 ? (double)SeenPickSum / SeenCount : null;

    /// <summary>ATA – průměrné číslo picku (od 1), kdy byla karta vzata.</summary>
    public readonly double? Ata => PickCount > 0 ? (double)PickSum / PickCount : null;
}

/// <summary>Statistiky karty pro edici, formát, skupinu hráčů a barevnou kombinaci.</summary>
public sealed record CardStats(string Name, GameCounts Game, DraftCounts Draft);

/// <summary>Výsledky balíčků jedné barevné kombinace.</summary>
public sealed record ColorStats(string MainColors, string SplashColors, long Games, long Wins)
{
    public double? WinRate => Games > 0 ? (double)Wins / Games : null;
}

/// <summary>Stav jednoho zpracovaného datasetu.</summary>
public sealed record DatasetInfo(string SetCode, string Format, DatasetKind Kind, DateTimeOffset? LastModified, DateTimeOffset ProcessedUtc, long Rows);
