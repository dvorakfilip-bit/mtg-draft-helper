using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using MtgDraftHelper.Core.Cards;
using MtgDraftHelper.Core.Ratings;
using MtgDraftHelper.Core.Settings;

namespace MtgDraftHelper.App;

/// <summary>Okno nastavení. Upravuje kopii nastavení; výsledek je v <see cref="Result"/> po Uložit.</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _original;
    private readonly Func<Task<string>> _refreshData;
    private readonly Action<double> _previewOpacity;

    public SettingsWindow(AppSettings settings, string dataInfo, Func<Task<string>> refreshData, Action<double> previewOpacity)
    {
        InitializeComponent();
        _original = settings;
        _refreshData = refreshData;
        _previewOpacity = previewOpacity;

        GroupBox.SelectedIndex = settings.PlayerGroup == PlayerGroup.Top ? 1 : 0;
        DataInfoText.Text = Texts.Get("Settings.DataCurrent", dataInfo);
        OffColorSlider.Value = settings.OffColorPenalty;
        ArchetypeSlider.Value = settings.MaxArchetypeBonus;
        SignalSlider.Value = settings.MaxSignalBonus;
        ShowGihBox.IsChecked = settings.ShowGihWr;
        ShowAlsaBox.IsChecked = settings.ShowAlsa;
        ShowReasonsBox.IsChecked = settings.ShowReasons;
        OpacitySlider.Value = Math.Round(settings.Opacity * 100);
        ToggleHotkeyBox.Text = settings.ToggleHotkey;
        ClickThroughHotkeyBox.Text = settings.ClickThroughHotkey;
        LogPathBox.Text = settings.LogPath ?? "";

        Closed += (_, _) =>
        {
            if (Result is null)
                _previewOpacity(_original.Opacity); // Zrušit vrací průhlednost zpět
        };
    }

    public AppSettings? Result { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Hotkey.TryParse(ToggleHotkeyBox.Text, out _) || !Hotkey.TryParse(ClickThroughHotkeyBox.Text, out _)
            || ToggleHotkeyBox.Text == ClickThroughHotkeyBox.Text)
        {
            HotkeyError.Text = Texts.Get("Settings.InvalidHotkey");
            return;
        }

        Result = (_original with
        {
            PlayerGroup = GroupBox.SelectedIndex == 1 ? PlayerGroup.Top : PlayerGroup.All,
            OffColorPenalty = OffColorSlider.Value,
            MaxArchetypeBonus = ArchetypeSlider.Value,
            MaxSignalBonus = SignalSlider.Value,
            ShowGihWr = ShowGihBox.IsChecked == true,
            ShowAlsa = ShowAlsaBox.IsChecked == true,
            ShowReasons = ShowReasonsBox.IsChecked == true,
            Opacity = OpacitySlider.Value / 100,
            ToggleHotkey = ToggleHotkeyBox.Text,
            ClickThroughHotkey = ClickThroughHotkeyBox.Text,
            LogPath = LogPathBox.Text,
        }).Normalized();
        DialogResult = true;
    }

    private void ResetWeights_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings();
        OffColorSlider.Value = defaults.OffColorPenalty;
        ArchetypeSlider.Value = defaults.MaxArchetypeBonus;
        SignalSlider.Value = defaults.MaxSignalBonus;
    }

    private void Opacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsLoaded)
            _previewOpacity(e.NewValue / 100);
    }

    /// <summary>Zachytí stisknutou kombinaci kláves do textového pole.</summary>
    private void Hotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.Tab)
            return;

        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Win;

        var name = key is >= Key.D0 and <= Key.D9 ? ((int)(key - Key.D0)).ToString() : key.ToString();
        var text = new Hotkey(modifiers, name).ToString();
        ((TextBox)sender).Text = text;
        HotkeyError.Text = Hotkey.TryParse(text, out _) ? "" : Texts.Get("Settings.InvalidHotkey");
    }

    private void DefaultLog_Click(object sender, RoutedEventArgs e) => LogPathBox.Text = "";

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Player.log|Player*.log|Log (*.log)|*.log",
            FileName = string.IsNullOrEmpty(LogPathBox.Text) ? ArenaPaths.PlayerLogPath : LogPathBox.Text,
        };
        if (dialog.ShowDialog(this) == true)
            LogPathBox.Text = dialog.FileName;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        RefreshStatus.Text = Texts.Get("Settings.Refreshing");
        try
        {
            RefreshStatus.Text = await _refreshData();
        }
        catch (Exception ex)
        {
            RefreshStatus.Text = Texts.Get("Status.DataError", ex.Message);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }
}
