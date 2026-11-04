using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using MtgDraftHelper.Core.Cards;
using MtgDraftHelper.Core.DeckBuilding;
using MtgDraftHelper.Core.Logs;
using MtgDraftHelper.Core.Recommendation;

namespace MtgDraftHelper.App.ViewModels;

public sealed record ColorBar(string Letter, Brush Brush, double Fraction, bool IsTop);

public sealed record CurveBar(string Label, int Count, double Height);

public sealed record CardRow(
    string Name,
    string ManaCost,
    Brush ColorBrush,
    int Score,
    Brush ScoreBrush,
    string GihWr,
    string Alsa,
    string Reason,
    bool IsTop);

public sealed record PoolGroup(string Title, IReadOnlyList<CardRow> Cards);

public sealed record DeckVariantView(
    string Title,
    string Summary,
    string? Archetype,
    string? Warning,
    IReadOnlyList<PoolGroup> Groups,
    IReadOnlyList<string> Swaps);

/// <summary>Stav overlaye pro XAML. Aktualizuje ho <see cref="AppController"/> na UI vlákně.</summary>
public sealed class MainViewModel : ObservableObject
{
    private string _status = Texts.Get("Status.LoadingCards");
    private bool _hasWarning;
    private string _eventTitle = "";
    private string _pickInfo = "";
    private string _poolInfo = "";
    private bool _isDraftActive;
    private string _dataInfo = "";
    private int _selectedTab;

    public string Status { get => _status; set => Set(ref _status, value); }
    public bool HasWarning { get => _hasWarning; set => Set(ref _hasWarning, value); }
    public string EventTitle { get => _eventTitle; set => Set(ref _eventTitle, value); }
    public string PickInfo { get => _pickInfo; set => Set(ref _pickInfo, value); }
    public string PoolInfo { get => _poolInfo; set => Set(ref _poolInfo, value); }
    public bool IsDraftActive { get => _isDraftActive; set => Set(ref _isDraftActive, value); }
    public string DataInfo { get => _dataInfo; set => Set(ref _dataInfo, value); }
    public int SelectedTab { get => _selectedTab; set => Set(ref _selectedTab, value); }

    public ObservableCollection<ColorBar> ColorBars { get; } = [];
    public ObservableCollection<ColorBar> SignalBars { get; } = [];
    public ObservableCollection<CurveBar> Curve { get; } = [];
    public ObservableCollection<CardRow> PackCards { get; } = [];
    public ObservableCollection<PoolGroup> PoolGroups { get; } = [];
    public ObservableCollection<DeckVariantView> DeckVariants { get; } = [];

    private int _selectedVariant;
    private DeckVariantView? _selectedDeck;

    public int SelectedVariant
    {
        get => _selectedVariant;
        set
        {
            if (Set(ref _selectedVariant, value))
                SelectedDeck = value >= 0 && value < DeckVariants.Count ? DeckVariants[value] : null;
        }
    }

    public DeckVariantView? SelectedDeck { get => _selectedDeck; private set => Set(ref _selectedDeck, value); }

    public bool HasDeck => DeckVariants.Count > 0;

    public void ShowDecks(IReadOnlyList<DeckVariant> variants)
    {
        var previous = SelectedDeck?.Title;
        Replace(DeckVariants, variants.Select(DeckView));
        var index = DeckVariants.ToList().FindIndex(v => v.Title == previous);
        _selectedVariant = -1;
        SelectedVariant = index >= 0 ? index : DeckVariants.Count > 0 ? 0 : -1;
        if (DeckVariants.Count == 0)
            SelectedDeck = null;
        OnPropertyChanged(nameof(HasDeck));
    }

    private static DeckVariantView DeckView(DeckVariant v)
    {
        var colors = string.Join("/", PickRecommender.AllColors.Where(c => v.MainColors.HasFlag(c)).Select(Texts.ColorName));
        var title = ManaCost.ToLetters(v.MainColors) + (v.SplashColor == ManaColors.None ? "" : "+" + ManaCost.ToLetters(v.SplashColor));
        var summary = colors
            + (v.SplashColor == ManaColors.None ? "" : " " + Texts.Get("Deck.Splash", Texts.ColorName(v.SplashColor)))
            + "\n" + Texts.Get("Deck.Summary", v.Score, v.Spells.Count, v.Creatures, v.LandCount);

        CardRow Row(DeckCardChoice c)
        {
            var score = (int)Math.Round(c.Value);
            return new CardRow(c.Card.Name, c.Card.ManaCost, ColorBrush(c.Card.Colors), score, ScoreBrush(score), "", "", "", false);
        }

        var creatures = v.Spells.Where(s => s.Card.IsCreature).Select(Row).ToList();
        var others = v.Spells.Where(s => !s.Card.IsCreature).Select(Row).ToList();
        var lands = v.NonbasicLands.Select(Row)
            .Concat(v.BasicLands.Select(b => new CardRow($"{b.Count}× {b.Name}", "", ColorBrush(b.Color), 0, ScoreBrush(0), "", "", "", false)))
            .ToList();

        return new DeckVariantView(
            title,
            summary,
            v.ArchetypeWinRate is { } wr ? Texts.Get("Deck.Archetype", ManaCost.ToLetters(v.MainColors), wr) : null,
            !v.IsComplete ? Texts.Get("Deck.Incomplete")
                : v.Creatures < DeckBuilder.MinCreatures ? Texts.Get("Deck.FewCreatures", v.Creatures)
                : null,
            [
                new PoolGroup(Texts.Get("Deck.Creatures", creatures.Count), creatures),
                new PoolGroup(Texts.Get("Deck.Spells", others.Count), others),
                new PoolGroup(Texts.Get("Deck.Lands", v.LandCount), lands),
            ],
            v.Swaps.Select(s => Texts.Get("Deck.Swap", s.Out.Card.Name, s.Out.Value, s.In.Card.Name, s.In.Value)).ToList());
    }

    public void ShowIdle(string status, bool warning)
    {
        Status = status;
        HasWarning = warning;
        IsDraftActive = false;
        EventTitle = "";
        PickInfo = "";
        PoolInfo = "";
        ColorBars.Clear();
        SignalBars.Clear();
        Curve.Clear();
        PackCards.Clear();
        PoolGroups.Clear();
        ShowDecks([]);
    }

    public void ShowDraft(
        DraftSnapshot snapshot,
        DraftAnalysis analysis,
        IReadOnlyList<PickRecommendation> pack,
        IReadOnlyList<CardInfo> pool,
        Func<CardInfo, (int Score, string Gih, string Alsa)> poolScore,
        Func<string, (string Gih, string Alsa)> stats)
    {
        IsDraftActive = true;
        var info = snapshot.Event!;
        EventTitle = $"{info.SetCode ?? "?"} · {info.Format} · {Texts.Get("Phase." + snapshot.Phase)}";

        PickInfo = snapshot.CurrentPack is { } current
            ? Texts.Get("Info.Pick", analysis.OverallPick, analysis.TotalPicks, current.Pack, current.Pick)
            : snapshot.Phase == DraftPhase.Drafting ? Texts.Get("Info.WaitingForPack") : "";
        PoolInfo = Texts.Get("Info.PoolCounts", pool.Count, analysis.Creatures, analysis.Removal);

        Replace(ColorBars, Bars(analysis.ColorWeights, analysis.TopColors));
        Replace(SignalBars, Bars(analysis.Signals, ManaColors.None));

        var maxCurve = Math.Max(1, analysis.Curve.Max());
        Replace(Curve, analysis.Curve.Select((count, mv) =>
            new CurveBar(mv == 6 ? "6+" : mv.ToString(), count, 30.0 * count / maxCurve)));

        var topCount = snapshot.CurrentPack?.CardsToPick is > 1 ? 3 + snapshot.CurrentPack.CardsToPick - 1 : 3;
        Replace(PackCards, pack.Select((r, i) =>
        {
            var (gih, alsa) = stats(r.Card.Name);
            return new CardRow(r.Card.Name, r.Card.ManaCost, ColorBrush(r.Card.Colors), r.Score, ScoreBrush(r.Score),
                gih, alsa, Reason(r), i < topCount);
        }));

        Replace(PoolGroups, PoolGroupsOf(pool, poolScore));

        // Záložka se přepíná sama jen při změně fáze; jinak zůstává, kterou si hráč vybral.
        // Po draftu a v Sealedu se ukáže Balíček.
        if (snapshot.Phase != _lastPhase)
        {
            if (snapshot.Phase == DraftPhase.Drafting)
                SelectedTab = 0;
            else if (snapshot.Phase == DraftPhase.DeckBuilding)
                SelectedTab = 2;
            _lastPhase = snapshot.Phase;
        }
    }

    private DraftPhase? _lastPhase;

    private static IEnumerable<ColorBar> Bars(IReadOnlyDictionary<ManaColors, double> values, ManaColors top)
    {
        var max = values.Values.DefaultIfEmpty(0).Max();
        return PickRecommender.AllColors.Select(c => new ColorBar(
            ManaCost.ToLetters(c), ColorBrush(c), max > 0 ? values[c] / max : 0, top.HasFlag(c)));
    }

    private static IEnumerable<PoolGroup> PoolGroupsOf(IReadOnlyList<CardInfo> pool, Func<CardInfo, (int Score, string Gih, string Alsa)> score)
    {
        string GroupOf(CardInfo c) =>
            c.IsLand ? "Lands"
            : c.Colors == ManaColors.None ? "C"
            : PickRecommender.AllColors.Count(x => c.Colors.HasFlag(x)) > 1 ? "M"
            : ManaCost.ToLetters(c.Colors);

        var order = new[] { "W", "U", "B", "R", "G", "M", "C", "Lands" };
        return pool
            .GroupBy(GroupOf)
            .OrderBy(g => Array.IndexOf(order, g.Key))
            .Select(g => new PoolGroup(
                g.Key switch
                {
                    "M" => Texts.Get("Pool.Multicolor"),
                    "C" => Texts.Get("Pool.Colorless"),
                    "Lands" => Texts.Get("Pool.Lands"),
                    var letter => Texts.ColorName(ManaCost.ParseColors(letter)),
                } + $" ({g.Count()})",
                g.OrderBy(c => c.ManaValue).ThenBy(c => c.Name).Select(c =>
                {
                    var (s, gih, alsa) = score(c);
                    return new CardRow(c.Name, c.ManaCost, ColorBrush(c.Colors), s, ScoreBrush(s), gih, alsa, "", false);
                }).ToList()));
    }

    public static string Reason(PickRecommendation r)
    {
        var parts = new List<string>();
        foreach (var f in r.Factors)
        {
            if (parts.Count >= 2)
                break;
            if (f.Kind != FactorKind.NoData && Math.Abs(f.Points) < 1.5)
                continue;
            parts.Add(f.Kind switch
            {
                FactorKind.NoData => Texts.Get("Reason.NoData"),
                FactorKind.OffColor => Texts.Get("Reason.OffColor"),
                FactorKind.Archetype => Texts.Get(f.Points > 0 ? "Reason.ArchetypeGood" : "Reason.ArchetypeBad", f.Colors),
                FactorKind.NeedCreatures => Texts.Get("Reason.NeedCreatures"),
                FactorKind.NeedRemoval => Texts.Get("Reason.NeedRemoval"),
                FactorKind.NeedTwoDrops => Texts.Get("Reason.NeedTwoDrops"),
                FactorKind.TooManyExpensive => Texts.Get("Reason.TooManyExpensive"),
                FactorKind.Signal => Texts.Get(f.Points > 0 ? "Reason.SignalOpen" : "Reason.SignalClosed",
                    string.Join("/", PickRecommender.AllColors.Where(c => ManaCost.ParseColors(f.Colors).HasFlag(c)).Select(Texts.ColorName))),
                _ => "",
            });
        }
        if (parts.Count == 0 && r.Roles.HasFlag(CardRole.Removal))
            parts.Add(Texts.Get("Reason.Removal"));
        return string.Join(" · ", parts);
    }

    public static Brush ColorBrush(ManaColors colors)
    {
        var list = PickRecommender.AllColors.Where(c => colors.HasFlag(c)).ToList();
        if (list.Count == 0)
            return Resource("ManaC");
        if (list.Count == 1)
            return Resource("Mana" + ManaCost.ToLetters(list[0]));

        // Vícebarevná karta: přechod mezi barvami.
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        for (var i = 0; i < list.Count; i++)
            brush.GradientStops.Add(new GradientStop(((SolidColorBrush)Resource("Mana" + ManaCost.ToLetters(list[i]))).Color, (double)i / (list.Count - 1)));
        brush.Freeze();
        return brush;
    }

    public static Brush ScoreBrush(int score) => Resource(score switch
    {
        >= 80 => "ScoreHighBrush",
        >= 60 => "ScoreGoodBrush",
        >= 40 => "ScoreMidBrush",
        _ => "ScoreLowBrush",
    });

    private static Brush Resource(string key) => (Brush)Application.Current.FindResource(key);

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}
