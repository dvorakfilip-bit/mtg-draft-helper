namespace MtgDraftHelper.Core.Logs;

/// <summary>
/// Sleduje Player.log a udržuje <see cref="DraftState"/>. Při startu zpracuje celý existující
/// log (obnova rozehraného draftu), pak každých <see cref="PollInterval"/> čte nové řádky.
/// </summary>
public sealed class LogMonitor : IDisposable
{
    private readonly LogTailer _tailer;
    private readonly DraftState _state;
    private readonly object _lock = new();
    private LogMessageReader _reader = new();
    private Timer? _timer;
    private int _polling;

    public LogMonitor(string playerLogPath, Func<string, EventInfo>? parseEvent = null)
    {
        _tailer = new LogTailer(playerLogPath);
        _state = new DraftState(parseEvent);
        _tailer.Rewritten += () =>
        {
            // Nový běh Areny: stav se obnoví z odpovědi EventGetCourses v novém logu.
            _reader = new LogMessageReader();
            _state.Reset();
        };
    }

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Stav se změnil. Volá se z vlákna časovače.</summary>
    public event Action<DraftSnapshot>? StateChanged;

    /// <summary>Událost z logu (pro ladění). Volá se z vlákna časovače.</summary>
    public event Action<LogEvent>? EventParsed;

    /// <summary>Chyba při čtení nebo zpracování; sledování pokračuje.</summary>
    public event Action<Exception>? Error;

    public DraftSnapshot Snapshot
    {
        get
        {
            lock (_lock)
                return _state.ToSnapshot();
        }
    }

    public void Start()
    {
        Poll();
        _timer = new Timer(_ => Poll(), null, PollInterval, PollInterval);
    }

    /// <summary>Jednorázově zpracuje nové řádky. Vrací true, pokud se stav změnil.</summary>
    public bool Poll()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1)
            return false; // předchozí čtení ještě běží

        try
        {
            DraftSnapshot? changed = null;
            lock (_lock)
            {
                var anyChange = false;
                foreach (var line in _tailer.ReadNewLines())
                {
                    LogMessage? message;
                    try
                    {
                        message = _reader.ReadLine(line);
                    }
                    catch (Exception ex)
                    {
                        Error?.Invoke(ex);
                        continue;
                    }
                    if (message is null)
                        continue;

                    foreach (var logEvent in SafeParse(message))
                    {
                        EventParsed?.Invoke(logEvent);
                        anyChange |= _state.Apply(logEvent);
                    }
                }
                if (anyChange)
                    changed = _state.ToSnapshot();
            }

            if (changed is not null)
                StateChanged?.Invoke(changed);
            return changed is not null;
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex);
            return false;
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    private IEnumerable<LogEvent> SafeParse(LogMessage message)
    {
        try
        {
            return LogEventParser.Parse(message).ToList();
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex);
            return [];
        }
    }

    public void Dispose() => _timer?.Dispose();
}
