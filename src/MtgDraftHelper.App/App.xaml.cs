using System.Runtime.InteropServices;
using System.Windows;

namespace MtgDraftHelper.App;

public partial class App : Application
{
    private AppController? _controller;

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Aplikace se spouští přes dotnet.exe (Smart App Control), který otevře okno konzole.
        // Zástupce předává --hide-console a konzoli tu zavřeme.
        if (e.Args.Contains("--hide-console"))
            FreeConsole();

        var logPath = Option(e.Args, "--log");
        var viewModel = new ViewModels.MainViewModel();
        var window = new OverlayWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();

        _controller = new AppController(viewModel, Dispatcher, logPath);
        _controller.Start();

        // Vývojářská volba: vykreslí okno do PNG a aplikaci ukončí (kontrola vzhledu bez snímání obrazovky).
        if (Option(e.Args, "--screenshot") is { } screenshotPath)
        {
            var delay = int.TryParse(Option(e.Args, "--screenshot-delay"), out var s) ? s : 8;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(delay) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                SaveWindowImage(window, screenshotPath);
                Shutdown();
            };
            timer.Start();
        }
    }

    private static void SaveWindowImage(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)content.ActualWidth, (int)content.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        base.OnExit(e);
    }

    private static string? Option(string[] args, string name) =>
        Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
