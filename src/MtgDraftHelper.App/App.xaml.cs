using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using MtgDraftHelper.Core.Settings;

namespace MtgDraftHelper.App;

public partial class App : Application
{
    private readonly SettingsStore _settingsStore = new(SettingsStore.DefaultPath);
    private AppSettings _settings = new();
    private AppController? _controller;
    private OverlayWindow? _window;
    private ViewModels.MainViewModel? _viewModel;
    private HotkeyManager? _hotkeys;
    private DispatcherTimer? _saveTimer;

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Aplikace se spouští přes dotnet.exe (Smart App Control), který otevře okno konzole.
        // Zástupce předává --hide-console a konzoli tu zavřeme.
        if (e.Args.Contains("--hide-console"))
            FreeConsole();

        _settings = _settingsStore.Load();
        _viewModel = new ViewModels.MainViewModel();
        _window = new OverlayWindow { DataContext = _viewModel };
        _window.RestorePlacement(_settings.Window);
        MainWindow = _window;
        _window.Show();
        NativeWindow.SetNoActivate(_window);

        _hotkeys = new HotkeyManager(_window);
        ApplyToUi(_settings);

        _window.SettingsRequested += OpenSettings;
        _window.LocationChanged += (_, _) => ScheduleSave();
        _window.SizeChanged += (_, _) => ScheduleSave();
        _window.Closing += (_, _) => SavePlacement();

        _controller = new AppController(_viewModel, Dispatcher, _settings, Option(e.Args, "--log"));
        _controller.Start();

        // Vývojářská volba: vykreslí okno do PNG a aplikaci ukončí (kontrola vzhledu bez snímání obrazovky).
        if (Option(e.Args, "--screenshot") is { } screenshotPath)
        {
            var delay = int.TryParse(Option(e.Args, "--screenshot-delay"), out var s) ? s : 8;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delay) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (e.Args.Contains("--screenshot-settings"))
                {
                    var settingsWindow = new SettingsWindow(_settings, _controller.CurrentDataInfo(), _controller.RefreshDataAsync, _ => { });
                    settingsWindow.Show();
                    settingsWindow.UpdateLayout();
                    SaveWindowImage(settingsWindow, screenshotPath);
                    settingsWindow.Close();
                }
                else
                {
                    SaveWindowImage(_window, screenshotPath);
                }
                Shutdown();
            };
            timer.Start();
        }
    }

    /// <summary>Průhlednost, sloupce a klávesové zkratky podle nastavení.</summary>
    private void ApplyToUi(AppSettings settings)
    {
        _viewModel!.WindowOpacity = settings.Opacity;
        _viewModel.ShowGihWr = settings.ShowGihWr;
        _viewModel.ShowAlsa = settings.ShowAlsa;
        _viewModel.ShowReasons = settings.ShowReasons;
        _viewModel.ClickThroughInfo = Texts.Get("Info.ClickThrough", settings.ClickThroughHotkey);

        _hotkeys!.UnregisterAll();
        var failed = new List<string>();
        if (!_hotkeys.Register(settings.ToggleHotkey, ToggleVisibility))
            failed.Add(settings.ToggleHotkey);
        if (!_hotkeys.Register(settings.ClickThroughHotkey, ToggleClickThrough))
            failed.Add(settings.ClickThroughHotkey);
        if (failed.Count > 0)
            MessageBox.Show(_window!, Texts.Get("Status.HotkeyFailed", string.Join(", ", failed)), Texts.Get("App.Title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ToggleVisibility()
    {
        if (_window!.IsVisible)
        {
            _window.Hide();
        }
        else
        {
            _window.Show();
            if (_window.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;
        }
    }

    private void ToggleClickThrough()
    {
        _viewModel!.ClickThrough = !_viewModel.ClickThrough;
        NativeWindow.SetClickThrough(_window!, _viewModel.ClickThrough);
        if (!_window!.IsVisible)
            _window.Show();
    }

    private void OpenSettings()
    {
        var dialog = new SettingsWindow(_settings, _controller!.CurrentDataInfo(), _controller.RefreshDataAsync,
            opacity => _viewModel!.WindowOpacity = opacity);
        if (dialog.ShowDialog() != true || dialog.Result is not { } result)
            return;

        _settings = result with { Window = _window!.CurrentPlacement };
        _settingsStore.Save(_settings);
        ApplyToUi(_settings);
        _controller.ApplySettings(_settings);
    }

    /// <summary>Pozice a velikost okna se uloží chvíli po posledním přesunu.</summary>
    private void ScheduleSave()
    {
        _saveTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            _saveTimer!.Stop();
            SavePlacement();
        }, Dispatcher);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SavePlacement()
    {
        if (_window is null || _window.WindowState != WindowState.Normal)
            return;
        _settings = _settings with { Window = _window.CurrentPlacement };
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception)
        {
            // Uložení pozice není kritické.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _controller?.Dispose();
        base.OnExit(e);
    }

    private static void SaveWindowImage(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var size = new Size(content.ActualWidth, content.ActualHeight);
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Obsah okna bez pozadí okna by byl na průhledném podkladu.
            dc.DrawRectangle(window.Background, null, new Rect(size));
            dc.DrawRectangle(new System.Windows.Media.VisualBrush(content), null, new Rect(size));
        }
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)size.Width, (int)size.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }

    private static string? Option(string[] args, string name) =>
        Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
