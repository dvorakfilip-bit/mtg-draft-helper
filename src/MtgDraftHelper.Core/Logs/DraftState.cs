namespace MtgDraftHelper.Core.Logs;

public enum DraftPhase
{
    /// <summary>Žádný rozehraný limited event.</summary>
    Idle,

    /// <summary>Hráč je v eventu, draft ještě nezačal (nebo nevíme, v jaké je fázi).</summary>
    Joined,

    Drafting,

    /// <summary>Draft skončil nebo je otevřený Sealed pool – skládá se balíček.</summary>
    DeckBuilding,

    /// <summary>Balíček je odeslaný, hrají se zápasy.</summary>
    Playing,

    Complete,
}

public sealed record PickRecord(int Pack, int Pick, IReadOnlyList<int> Offered, IReadOnlyList<int> Picked);

/// <summary>Neměnný obraz stavu pro UI a doporučování.</summary>
public sealed record DraftSnapshot(
    EventInfo? Event,
    string? DraftId,
    DraftPhase Phase,
    PackShown? CurrentPack,
    IReadOnlyList<PickRecord> Picks,
    IReadOnlyList<int> Pool,
    IReadOnlyList<DeckCard> Deck,
    IReadOnlyList<DeckCard> Sideboard,
    bool? DetailedLogsEnabled);

/// <summary>
/// Stav aktuálního limited eventu, skládaný z událostí logu. Není vláknově bezpečný –
/// používá ho <see cref="LogMonitor"/> pod zámkem.
/// </summary>
public sealed class DraftState
{
    private readonly Func<string, EventInfo> _parseEvent;
    private readonly List<PickRecord> _picks = [];
    private List<int> _pool = [];
    private IReadOnlyList<DeckCard> _deck = [];
    private IReadOnlyList<DeckCard> _sideboard = [];

    public DraftState(Func<string, EventInfo>? parseEvent = null) =>
        _parseEvent = parseEvent ?? (name => EventInfo.Parse(name));

    public EventInfo? Event { get; private set; }
    public string? DraftId { get; private set; }
    public DraftPhase Phase { get; private set; } = DraftPhase.Idle;
    public PackShown? CurrentPack { get; private set; }
    public bool? DetailedLogsEnabled { get; private set; }
    public IReadOnlyList<PickRecord> Picks => _picks;
    public IReadOnlyList<int> Pool => _pool;

    public DraftSnapshot ToSnapshot() =>
        new(Event, DraftId, Phase, CurrentPack, _picks.ToList(), _pool.ToList(), _deck, _sideboard, DetailedLogsEnabled);

    public void Reset()
    {
        Event = null;
        DraftId = null;
        Phase = DraftPhase.Idle;
        CurrentPack = null;
        _picks.Clear();
        _pool = [];
        _deck = [];
        _sideboard = [];
    }

    /// <summary>Aplikuje událost. Vrací true, pokud se stav změnil.</summary>
    public bool Apply(LogEvent logEvent)
    {
        switch (logEvent)
        {
            case DetailedLogsStatus status:
                if (DetailedLogsEnabled == status.Enabled)
                    return false;
                DetailedLogsEnabled = status.Enabled;
                return true;

            case EventJoined joined:
                return OnEventJoined(joined.EventName);

            case CoursesReceived courses:
                return OnCourses(courses);

            case PackShown pack:
                return OnPackShown(pack);

            case CardsPicked picked:
                return OnCardsPicked(picked);

            case PoolSnapshot snapshot:
                if (!IsCurrentEvent(snapshot.EventName) || _pool.SequenceEqual(snapshot.Cards))
                    return false;
                _pool = snapshot.Cards.ToList();
                return true;

            case DraftCompleted completed:
                if (!IsCurrentEvent(completed.EventName))
                    return false;
                if (Event is null && completed.EventName is not null)
                    Event = _parseEvent(completed.EventName);
                if (completed.CardPool.Count > 0)
                    _pool = completed.CardPool.ToList();
                CurrentPack = null;
                Phase = DraftPhase.DeckBuilding;
                return true;

            case DeckSubmitted deck:
                if (Event is null || deck.EventName != Event.EventName)
                    return false;
                _deck = deck.MainDeck;
                _sideboard = deck.Sideboard;
                Phase = DraftPhase.Playing;
                return true;
        }
        return false;
    }

    private bool IsCurrentEvent(string? eventName) =>
        eventName is null || Event is null || eventName == Event.EventName;

    private bool OnEventJoined(string eventName)
    {
        var info = _parseEvent(eventName);
        if (!info.IsLimited)
            return false; // constructed nebo jiný event – rozehraný limited zůstává

        if (Event?.EventName == eventName && Phase != DraftPhase.Complete)
            return false;

        Reset();
        Event = info;
        Phase = DraftPhase.Joined;
        return true;
    }

    private bool OnCourses(CoursesReceived received)
    {
        var limited = received.Courses
            .Select(c => (Course: c, Info: _parseEvent(c.EventName)))
            .Where(x => x.Info.IsLimited)
            .ToList();

        if (Event is not null)
        {
            var current = limited.FirstOrDefault(x => x.Course.EventName == Event.EventName).Course;
            if (current is not null)
                return UpdateFromCourse(current);
            if (!received.IsFullList)
                return false;
            // Aktuální event v úplném seznamu chybí – skončil. Pokračujeme hledáním jiného aktivního.
        }

        if (!received.IsFullList)
        {
            // Odpověď na EventJoin pro event, o kterém zatím nevíme (např. log začal uprostřed).
            var single = limited.FirstOrDefault(x => !x.Course.IsComplete);
            if (single.Course is null)
                return false;
            Reset();
            Event = single.Info;
            Phase = DraftPhase.Joined;
            UpdateFromCourse(single.Course);
            return true;
        }

        // Obnova po startu: aktivní limited event, přednost má rozehraný draft.
        var active = limited
            .Where(x => !x.Course.IsComplete)
            .OrderBy(x => PhaseForModule(x.Course.CurrentModule) switch
            {
                DraftPhase.Drafting => 0,
                DraftPhase.DeckBuilding => 1,
                DraftPhase.Playing => 2,
                _ => 3,
            })
            .FirstOrDefault();

        if (active.Course is null)
        {
            if (Event is null)
                return false;
            Phase = DraftPhase.Complete;
            CurrentPack = null;
            return true;
        }

        if (Event?.EventName != active.Course.EventName)
        {
            Reset();
            Event = active.Info;
            Phase = DraftPhase.Joined;
        }
        UpdateFromCourse(active.Course);
        return true;
    }

    private bool UpdateFromCourse(CourseInfo course)
    {
        var changed = false;
        var phase = PhaseForModule(course.CurrentModule);

        // Pool od serveru je spolehlivější než součet picků z logu.
        if (course.CardPool.Count > 0 && course.CardPool.Count >= _pool.Count && !_pool.SequenceEqual(course.CardPool))
        {
            _pool = course.CardPool.ToList();
            changed = true;
        }

        if (phase is { } p && p != Phase)
        {
            // Rozehraný draft nepřepisujeme na Joined/Drafting zpět, jen posouváme dopředu.
            if (p > Phase || p == DraftPhase.Complete || Phase == DraftPhase.Joined)
            {
                Phase = p;
                if (p != DraftPhase.Drafting)
                    CurrentPack = null;
                changed = true;
            }
        }
        return changed;
    }

    internal static DraftPhase? PhaseForModule(string? module) => module switch
    {
        "Draft" or "BotDraft" => DraftPhase.Drafting,
        "DeckSelect" => DraftPhase.DeckBuilding,
        "CreateMatch" => DraftPhase.Playing,
        "Complete" or "ClaimPrize" => DraftPhase.Complete,
        _ => null,
    };

    private bool OnPackShown(PackShown pack)
    {
        if (pack.EventName is not null && Event?.EventName != pack.EventName)
        {
            var info = _parseEvent(pack.EventName);
            Reset();
            Event = info;
        }

        if (pack.DraftId is not null && DraftId is not null && pack.DraftId != DraftId)
        {
            // Nový draft stejného eventu (např. další run) – picky a pool začínají znovu.
            _picks.Clear();
            _pool = [];
            _deck = [];
            _sideboard = [];
        }

        DraftId = pack.DraftId ?? DraftId;
        CurrentPack = pack;
        Phase = DraftPhase.Drafting;
        return true;
    }

    private bool OnCardsPicked(CardsPicked picked)
    {
        if (picked.DraftId is not null && DraftId is not null && picked.DraftId != DraftId)
            return false; // pick z jiného draftu
        if (!IsCurrentEvent(picked.EventName))
            return false;

        var offered = CurrentPack is { } pack && pack.Pack == picked.Pack && pack.Pick == picked.Pick
            ? pack.Cards
            : [];

        var existing = _picks.FindIndex(p => p.Pack == picked.Pack && p.Pick == picked.Pick);
        if (existing >= 0)
        {
            if (_picks[existing].Picked.SequenceEqual(picked.Cards))
                return false; // duplicitní záznam
            foreach (var card in _picks[existing].Picked)
                _pool.Remove(card);
            _picks[existing] = new PickRecord(picked.Pack, picked.Pick, offered.Count > 0 ? offered : _picks[existing].Offered, picked.Cards);
        }
        else
        {
            _picks.Add(new PickRecord(picked.Pack, picked.Pick, offered, picked.Cards));
        }

        _pool.AddRange(picked.Cards);
        DraftId = picked.DraftId ?? DraftId;
        CurrentPack = null;
        Phase = DraftPhase.Drafting;
        return true;
    }
}
