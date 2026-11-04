using System.Globalization;
using System.Windows.Threading;
using MtgDraftHelper.App.ViewModels;
using MtgDraftHelper.Core;
using MtgDraftHelper.Core.Cards;
using MtgDraftHelper.Core.DeckBuilding;
using MtgDraftHelper.Core.Logs;
using MtgDraftHelper.Core.Ratings;
using MtgDraftHelper.Core.Recommendation;
using MtgDraftHelper.Core.Settings;
using MtgDraftHelper.Core.Storage;

namespace MtgDraftHelper.App;

/// <summary>
/// Propojuje sledování logu, data 17Lands a doporučování s view modelem. Těžká práce běží
/// na pozadí, view model se mění jen na UI vlákně.
/// </summary>
public sealed class AppController : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly Dispatcher _dispatcher;
    private readonly string? _logPathOverride;
    private readonly RatingsService _ratingsService;
    private readonly SemaphoreSlim _ratingsLock = new(1, 1);
    private readonly object _monitorLock = new();

    private volatile AppSettings _settings;
    private ArenaCardDatabase? _cards;
    private Func<string, EventInfo>? _parseEvent;
    private LogMonitor? _monitor;
    private string? _monitoredPath;
    private DraftSnapshot? _lastSnapshot;

    // Hodnocení pro aktuální edici a formát; klíč "SET|Format eventu".
    private string? _ratingsKey;
    private CardRatings? _ratings;
    private string? _ratingsFormat;
    private string? _dataStatus;

    public AppController(MainViewModel viewModel, Dispatcher dispatcher, AppSettings settings, string? logPathOverride)
    {
        _viewModel = viewModel;
        _dispatcher = dispatcher;
        _settings = settings;
        _logPathOverride = logPathOverride;
        _ratingsService = new RatingsService(new StatsStore(AppPaths.StatsDatabase), new PublicDatasetClient(), AppPaths.DatasetCacheDir);
    }

    private string LogPath => _logPathOverride ?? _settings.LogPath ?? ArenaPaths.PlayerLogPath;

    public void Start() => Task.Run(Initialize);

    private void Initialize()
    {
        try
        {
            _cards = ArenaCardDatabase.LoadDefault();
        }
        catch (Exception)
        {
            UI(() => _viewModel.ShowIdle(Texts.Get("Status.CardsMissing"), warning: true));
            return;
        }

        var sets = _cards.All.Select(c => c.SetCode).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _parseEvent = name => EventInfo.Parse(name, sets);
        StartMonitor();
    }

    /// <summary>Spustí (nebo po změně cesty znovu spustí) sledování logu.</summary>
    private void StartMonitor()
    {
        LogMonitor monitor;
        lock (_monitorLock)
        {
            if (_parseEvent is null || (_monitor is not null && _monitoredPath == LogPath))
                return;
            _monitor?.Dispose();
            _monitoredPath = LogPath;
            _monitor = monitor = new LogMonitor(_monitoredPath, _parseEvent);
            _monitor.StateChanged += snapshot =>
            {
                if (ReferenceEquals(_monitor, monitor))
                    OnStateChanged(snapshot);
            };
        }
        monitor.Start();
        OnStateChanged(monitor.Snapshot);
    }

    /// <summary>Použije nové nastavení: cestu k logu, skupinu hráčů a váhy skóre.</summary>
    public void ApplySettings(AppSettings settings)
    {
        var groupChanged = settings.PlayerGroup != _settings.PlayerGroup;
        _settings = settings;
        Task.Run(() =>
        {
            StartMonitor();
            if (groupChanged)
                _ratingsKey = null; // hodnocení se načtou znovu pro novou skupinu
            if (_lastSnapshot is { } snapshot)
                OnStateChanged(snapshot);
        });
    }

    /// <summary>Popis dat použitých pro aktuální event (pro okno nastavení).</summary>
    public string CurrentDataInfo()
    {
        if (_ratings is not { } ratings)
            return "–";
        var info = _ratingsService.Store.GetDatasetInfo(ratings.SetCode, ratings.Format, DatasetKind.Game);
        var group = Texts.Get(_settings.PlayerGroup == PlayerGroup.Top ? "Settings.GroupTop" : "Settings.GroupAll");
        return $"{ratings.SetCode} {ratings.Format}, {group}"
               + (info?.LastModified is { } modified ? $", verze {modified.LocalDateTime:d}" : "");
    }

    /// <summary>Ruční kontrola nové verze datasetu pro aktuální event.</summary>
    public async Task<string> RefreshDataAsync()
    {
        var info = _lastSnapshot?.Event;
        if (info?.SetCode is null || info.SeventeenLandsFormat is null)
            return Texts.Get("Settings.RefreshNoEvent");

        var format = _ratingsFormat ?? info.SeventeenLandsFormat;
        IReadOnlyList<DatasetUpdateResult> results;
        await _ratingsLock.WaitAsync().ConfigureAwait(false);
        try
        {
            results = await _ratingsService.UpdateAsync(info.SetCode, format).ConfigureAwait(false);
        }
        finally
        {
            _ratingsLock.Release();
        }

        if (results.Any(r => r.Status == DatasetUpdateStatus.Updated))
        {
            _ratingsKey = null; // načíst nově zpracovaná data
            if (_lastSnapshot is { } snapshot)
                OnStateChanged(snapshot);
        }
        return Texts.Get("Settings.RefreshDone", string.Join(", ", results.Select(r => $"{r.Kind}: {r.Status}")));
    }

    private void OnStateChanged(DraftSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        var info = snapshot.Event;
        var active = info is { IsLimited: true } && snapshot.Phase is not (DraftPhase.Idle or DraftPhase.Complete);

        if (!active)
        {
            var warning = snapshot.DetailedLogsEnabled == false;
            UI(() => _viewModel.ShowIdle(Texts.Get(warning ? "Status.DetailedLogsOff" : "Status.WaitingForDraft"), warning));
            return;
        }

        var key = $"{info!.SetCode}|{info.SeventeenLandsFormat}|{_settings.PlayerGroup}";
        if (key != _ratingsKey)
        {
            _ratingsKey = key;
            _ratings = null;
            _ratingsFormat = null;
            _ = LoadRatingsAsync(info, key);
        }

        Render(snapshot);
    }

    /// <summary>Zajistí data 17Lands pro edici a formát eventu (případně záložní formát).</summary>
    private async Task LoadRatingsAsync(EventInfo info, string key)
    {
        if (info.SetCode is null || info.SeventeenLandsFormat is null)
        {
            _dataStatus = Texts.Get("Status.NoData", info.EventName);
            Rerender();
            return;
        }

        await _ratingsLock.WaitAsync().ConfigureAwait(false);
        try
        {
            string? usedFormat = null;
            foreach (var format in RatingsDefaults.FormatFallbacks(info.SeventeenLandsFormat))
            {
                var hasLocal = _ratingsService.Store.GetDatasetInfo(info.SetCode, format, DatasetKind.Game) is not null;
                if (!hasLocal)
                {
                    _dataStatus = Texts.Get("Status.DownloadingData", info.SetCode, format);
                    Rerender();
                }

                try
                {
                    // Kontrola nové verze je jen dotaz na hlavičky; stahuje se, jen když se dataset změnil.
                    var results = await _ratingsService.UpdateAsync(info.SetCode, format).ConfigureAwait(false);
                    if (results.Any(r => r.Kind == DatasetKind.Game && r.Status != DatasetUpdateStatus.NotAvailable))
                    {
                        usedFormat = format;
                        break;
                    }
                }
                catch (Exception) when (hasLocal)
                {
                    // Offline: použijeme data z cache.
                    usedFormat = format;
                    break;
                }
                catch (Exception ex)
                {
                    _dataStatus = Texts.Get("Status.DataError", ex.Message);
                    Rerender();
                }
            }

            if (key != _ratingsKey)
                return; // mezitím začal jiný event

            if (usedFormat is null)
            {
                _dataStatus ??= Texts.Get("Status.NoData", $"{info.SetCode} {info.SeventeenLandsFormat}");
                _ratings = null;
            }
            else
            {
                _ratings = new CardRatings(_ratingsService.Store, info.SetCode, usedFormat, _settings.PlayerGroup);
                _ratingsFormat = usedFormat;
                _dataStatus = usedFormat == info.SeventeenLandsFormat
                    ? null
                    : Texts.Get("Status.FallbackFormat", usedFormat, info.SeventeenLandsFormat);
            }
            Rerender();
        }
        finally
        {
            _ratingsLock.Release();
        }
    }

    private void Rerender()
    {
        if (_lastSnapshot is { } snapshot && snapshot.Event is not null)
            Render(snapshot);
    }

    private void Render(DraftSnapshot snapshot)
    {
        if (_cards is null)
            return;

        var ratings = _ratings;
        ICardRatings source = (ICardRatings?)ratings ?? NoRatings.Instance;
        var settings = _settings;
        var recommender = new PickRecommender(source)
        {
            OffColorPenalty = settings.OffColorPenalty,
            MaxArchetypeBonus = settings.MaxArchetypeBonus,
            MaxSignalBonus = settings.MaxSignalBonus,
        };
        var sameFormat = ratings is not null && _ratingsFormat == snapshot.Event?.SeventeenLandsFormat;
        var context = DraftContextFactory.Create(snapshot, _cards, sameFormat);
        var analysis = recommender.Analyze(context);

        var packCards = snapshot.CurrentPack?.Cards.Select(_cards.Get).OfType<CardInfo>().ToList() ?? [];
        var recommendations = recommender.Recommend(packCards, context, analysis);

        // Návrh balíčku (záložka Balíček) – průběžně během draftu i v Sealedu.
        var colorStats = ratings is null ? [] : ColorStatsFor(ratings);
        IReadOnlyList<DeckVariant> decks = context.Pool.Count >= MinPoolForDeck
            ? new DeckBuilder(source, colorStats).Build(context.Pool)
            : [];

        (string Gih, string Alsa) Stats(string name)
        {
            var s = source.Stats(name);
            return (
                s?.Game.GihWr is { } wr ? wr.ToString("P1", CultureInfo.CurrentCulture) : "–",
                s?.Draft.Alsa is { } alsa ? alsa.ToString("N1", CultureInfo.CurrentCulture) : "–");
        }

        (int, string, string) PoolScore(CardInfo card)
        {
            var (gih, alsa) = Stats(card.Name);
            return ((int)Math.Round(recommender.BaseScoreOf(card)), gih, alsa);
        }

        var dataInfo = Texts.Get("Footer.Data")
            + (ratings is not null ? $" · {ratings.SetCode} {ratings.Format}" : "")
            + (ratings is not null && settings.PlayerGroup == PlayerGroup.Top ? " · top" : "")
            + (_dataStatus is null ? "" : $" · {_dataStatus}");
        var warning = snapshot.DetailedLogsEnabled == false;

        UI(() =>
        {
            _viewModel.ShowDraft(snapshot, analysis, recommendations, context.Pool, PoolScore, Stats);
            _viewModel.ShowDecks(decks);
            _viewModel.DataInfo = dataInfo;
            _viewModel.HasWarning = warning;
            _viewModel.Status = warning ? Texts.Get("Status.DetailedLogsOff") : "";
        });
    }

    private const int MinPoolForDeck = 15;

    private (CardRatings Ratings, IReadOnlyList<ColorStats> Stats)? _colorStatsCache;

    private IReadOnlyList<ColorStats> ColorStatsFor(CardRatings ratings)
    {
        if (_colorStatsCache is { } cache && ReferenceEquals(cache.Ratings, ratings))
            return cache.Stats;
        var stats = _ratingsService.Store.GetColorStats(ratings.SetCode, ratings.Format, PlayerGroup.All);
        _colorStatsCache = (ratings, stats);
        return stats;
    }

    private void UI(Action action) => _dispatcher.BeginInvoke(action);

    public void Dispose() => _monitor?.Dispose();

    private sealed class NoRatings : ICardRatings
    {
        public static readonly NoRatings Instance = new();
        public double? BaseScore(string cardName) => null;
        public CardStats? Stats(string cardName) => null;
        public CardStats? StatsForColors(string cardName, string colors) => null;
    }
}
