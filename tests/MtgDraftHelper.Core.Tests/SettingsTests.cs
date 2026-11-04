using MtgDraftHelper.Core.Ratings;
using MtgDraftHelper.Core.Settings;

namespace MtgDraftHelper.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "MtgDraftHelperTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var settings = new SettingsStore(SettingsPath).Load();
        Assert.Equal(new AppSettings(), settings);
    }

    [Fact]
    public void RoundTrips()
    {
        var store = new SettingsStore(SettingsPath);
        var settings = new AppSettings
        {
            PlayerGroup = PlayerGroup.Top,
            OffColorPenalty = 20,
            ShowAlsa = false,
            Opacity = 0.8,
            ToggleHotkey = "Alt+F2",
            LogPath = @"D:\logs\Player.log",
            Window = new WindowPlacement(100, 50, 400, 700),
        };

        store.Save(settings);

        Assert.Equal(settings, store.Load());
        Assert.Contains("\"Top\"", File.ReadAllText(SettingsPath)); // enum jako text, čitelné ručně
    }

    [Fact]
    public void CorruptedFileGivesDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ not json");
        Assert.Equal(new AppSettings(), new SettingsStore(SettingsPath).Load());
    }

    [Fact]
    public void NormalizesOutOfRangeValues()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, """{ "Opacity": 0.05, "OffColorPenalty": 500, "ToggleHotkey": "D", "LogPath": "  " }""");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Equal(0.3, settings.Opacity);
        Assert.Equal(60, settings.OffColorPenalty);
        Assert.Equal(AppSettings.DefaultToggleHotkey, settings.ToggleHotkey);
        Assert.Null(settings.LogPath);
    }
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Shift+D", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "D")]
    [InlineData("alt + f2", HotkeyModifiers.Alt, "f2")]
    [InlineData("F9", HotkeyModifiers.None, "F9")]
    [InlineData("Ctrl+1", HotkeyModifiers.Ctrl, "1")]
    [InlineData("Win+Alt+O", HotkeyModifiers.Win | HotkeyModifiers.Alt, "O")]
    public void ParsesValidHotkeys(string text, HotkeyModifiers modifiers, string key)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(modifiers, hotkey.Modifiers);
        Assert.Equal(key, hotkey.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("D")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+A+B")]
    [InlineData(null)]
    public void RejectsInvalidHotkeys(string? text) =>
        Assert.False(Hotkey.TryParse(text, out _));

    [Fact]
    public void FormatsInCanonicalOrder() =>
        Assert.Equal("Ctrl+Alt+Shift+K", new Hotkey(HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Ctrl, "K").ToString());
}
