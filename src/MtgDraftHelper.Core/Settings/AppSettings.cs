using MtgDraftHelper.Core.Ratings;

namespace MtgDraftHelper.Core.Settings;

/// <summary>Uživatelské nastavení aplikace (ukládá se do settings.json).</summary>
public sealed record AppSettings
{
    public const string DefaultToggleHotkey = "Ctrl+Shift+D";
    public const string DefaultClickThroughHotkey = "Ctrl+Shift+C";

    /// <summary>Skupina hráčů pro statistiky 17Lands.</summary>
    public PlayerGroup PlayerGroup { get; init; } = PlayerGroup.All;

    public double OffColorPenalty { get; init; } = 30;
    public double MaxArchetypeBonus { get; init; } = 6;
    public double MaxSignalBonus { get; init; } = 6;

    public bool ShowGihWr { get; init; } = true;
    public bool ShowAlsa { get; init; } = true;
    public bool ShowReasons { get; init; } = true;

    /// <summary>Průhlednost overlaye 0,3–1.</summary>
    public double Opacity { get; init; } = 1.0;

    public string ToggleHotkey { get; init; } = DefaultToggleHotkey;
    public string ClickThroughHotkey { get; init; } = DefaultClickThroughHotkey;

    /// <summary>Vlastní cesta k Player.log; null = výchozí umístění Areny.</summary>
    public string? LogPath { get; init; }

    public WindowPlacement? Window { get; init; }

    public AppSettings Normalized() => this with
    {
        OffColorPenalty = Math.Clamp(OffColorPenalty, 0, 60),
        MaxArchetypeBonus = Math.Clamp(MaxArchetypeBonus, 0, 15),
        MaxSignalBonus = Math.Clamp(MaxSignalBonus, 0, 15),
        Opacity = Math.Clamp(Opacity, 0.3, 1.0),
        ToggleHotkey = Hotkey.TryParse(ToggleHotkey, out _) ? ToggleHotkey : DefaultToggleHotkey,
        ClickThroughHotkey = Hotkey.TryParse(ClickThroughHotkey, out _) ? ClickThroughHotkey : DefaultClickThroughHotkey,
        LogPath = string.IsNullOrWhiteSpace(LogPath) ? null : LogPath.Trim(),
    };
}

public sealed record WindowPlacement(double Left, double Top, double Width, double Height);
