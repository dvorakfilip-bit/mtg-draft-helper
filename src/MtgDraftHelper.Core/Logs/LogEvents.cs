namespace MtgDraftHelper.Core.Logs;

/// <summary>Událost odvozená z logu Areny. Čísla balíčků a picků jsou vždy od 1.</summary>
public abstract record LogEvent;

public sealed record DetailedLogsStatus(bool Enabled) : LogEvent;

/// <summary>Hráč vstoupil do eventu (požadavek EventJoin).</summary>
public sealed record EventJoined(string EventName) : LogEvent;

/// <summary>Stav eventu od serveru (odpověď EventJoin, EventGetCourses, EventSetDeck).</summary>
public sealed record CourseInfo(string EventName, string? CurrentModule, IReadOnlyList<int> CardPool)
{
    /// <summary>Event už skončil.</summary>
    public bool IsComplete => string.Equals(CurrentModule, "Complete", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Seznam eventů hráče; <see cref="IsFullList"/> = odpověď EventGetCourses se všemi eventy.</summary>
public sealed record CoursesReceived(IReadOnlyList<CourseInfo> Courses, bool IsFullList) : LogEvent;

/// <summary>Balíček, ze kterého se vybírá.</summary>
public sealed record PackShown(string? DraftId, string? EventName, int Pack, int Pick, IReadOnlyList<int> Cards, int CardsToPick) : LogEvent;

/// <summary>Hráč vybral kartu (v Pick-Two Draftu dvě).</summary>
public sealed record CardsPicked(string? DraftId, string? EventName, int Pack, int Pick, IReadOnlyList<int> Cards) : LogEvent;

/// <summary>Kompletní seznam dosud vybraných karet (Quick Draft posílá s každým stavem).</summary>
public sealed record PoolSnapshot(string? EventName, IReadOnlyList<int> Cards) : LogEvent;

/// <summary>Draft skončil; <see cref="CardPool"/> je celý vydraftovaný pool.</summary>
public sealed record DraftCompleted(string? EventName, string? DraftId, IReadOnlyList<int> CardPool) : LogEvent;

/// <summary>Hráč odeslal balíček do eventu.</summary>
public sealed record DeckSubmitted(string EventName, IReadOnlyList<DeckCard> MainDeck, IReadOnlyList<DeckCard> Sideboard) : LogEvent;

public sealed record DeckCard(int GrpId, int Quantity);
