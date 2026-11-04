using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace MtgDraftHelper.App;

public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
        ShowActivated = false;

        // Výchozí pozice: pravý okraj pracovní plochy, aby panel nezakrýval balíček v Areně.
        var area = SystemParameters.WorkArea;
        Height = Math.Min(Height, area.Height - 40);
        Left = area.Right - Width - 12;
        Top = area.Top + 20;
    }

    /// <summary>Uživatel klikl na ozubené kolo.</summary>
    public event Action? SettingsRequested;

    /// <summary>Obnoví uloženou pozici, pokud je okno aspoň částečně na některé obrazovce.</summary>
    public void RestorePlacement(Core.Settings.WindowPlacement? placement)
    {
        if (placement is null)
            return;
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var rect = new Rect(placement.Left, placement.Top, Math.Max(MinWidth, placement.Width), Math.Max(MinHeight, placement.Height));
        var visible = Rect.Intersect(screen, rect);
        if (visible.IsEmpty || visible.Width < 80 || visible.Height < 40)
            return;

        Left = rect.Left;
        Top = rect.Top;
        Width = rect.Width;
        Height = rect.Height;
    }

    public Core.Settings.WindowPlacement CurrentPlacement => new(Left, Top, Width, Height);

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
