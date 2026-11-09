using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;

namespace OmniBrille.Desktop;

/// <summary>A small, non-taskbar launcher for the running workspace.</summary>
public sealed partial class EdgeDockWindow : Window
{
    public EdgeDockWindow() => InitializeComponent();

    public event EventHandler? OpenRequested;

    public void AlignTo(Screen screen)
    {
        var area = screen.WorkingArea;
        var height = (int)Math.Round(Height * screen.Scaling);
        Position = new PixelPoint(area.X, area.Y + Math.Max(0, (area.Height - height) / 2));
    }

    private void OnOpenClick(object? sender, RoutedEventArgs e) => OpenRequested?.Invoke(this, EventArgs.Empty);
}
