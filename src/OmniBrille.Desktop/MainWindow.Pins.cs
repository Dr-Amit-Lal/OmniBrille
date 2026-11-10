using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OmniBrille.Core;
using OmniBrille.Desktop.Rendering;

namespace OmniBrille.Desktop;

public sealed partial class MainWindow
{
    private string? _dragPinPath;
    private Point _pinPointerStart;
    private bool _pinDragging;
    private static readonly Geometry PinGeometry = Geometry.Parse("M 8,2 L 16,2 L 14,6 L 17,11 L 12,11 L 12,18 L 10,20 L 10,11 L 5,11 L 8,6 Z");

    private void InitializePinDragging()
    {
        PinnedEntries.AddHandler(PointerMovedEvent, OnPinPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        PinnedEntries.AddHandler(PointerReleasedEvent, OnPinPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        PinnedEntries.PointerCaptureLost += (_, e) =>
        {
            if (ReferenceEquals(e.Source, PinnedEntries)) { _dragPinPath = null; _pinDragging = false; PinnedEntries.Cursor = null; }
        };
    }
    private static Avalonia.Controls.Shapes.Path PinIcon()
    {
        var icon = new Avalonia.Controls.Shapes.Path { Data = PinGeometry, StrokeThickness = 1.3, Width = 19, Height = 20, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        icon.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, new DynamicResourceExtension("SecondaryTextBrush"));
        return icon;
    }
    private Button FolderButton(string path, string name)
    {
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("34,*"), ColumnSpacing = 10 };
        content.Children.Add(new PinnedFolderIcon(path) { ReducedEffects = _preferences.ReducedEffects });
        var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(label, 1);
        content.Children.Add(label);
        var button = new Button { Content = content, Tag = path, MinHeight = 50, Padding = new Thickness(6, 3), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Classes = { "hud" } };
        ToolTip.SetTip(button, path);
        return button;
    }
    private void UpdatePinRows()
    {
        var signature = string.Join('\n', _session.State.Pins.Select(pin => pin.Path + "\t" + pin.Name));
        if (signature != _pinSignature || PinnedEntries.Children.Count != _session.State.Pins.Count)
        {
            _pinSignature = signature;
            PinnedEntries.Children.Clear();
            foreach (var pin in _session.State.Pins)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,44"), Tag = pin.Path };
                var open = FolderButton(pin.Path, pin.Name);
                AutomationProperties.SetName(open, "Open pinned folder " + pin.Name);
                AutomationProperties.SetHelpText(open, "Alt+Up and Alt+Down reorder this shortcut.");
                open.Click += async (_, _) => { if (!_pinDragging) { await _session.OpenPinAsync(pin.Path); } };
                open.KeyDown += (_, e) =>
                {
                    if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Up or Key.Down)
                    {
                        _session.MovePin(pin.Path, e.Key == Key.Up ? -1 : 1);
                        FocusPin(pin.Path);
                        e.Handled = true;
                    }
                };
                open.AddHandler(PointerPressedEvent, (_, e) =>
                {
                    if (!e.GetCurrentPoint(open).Properties.IsLeftButtonPressed) { return; }
                    _dragPinPath = pin.Path;
                    _pinPointerStart = e.GetPosition(PinnedEntries);
                    _pinDragging = false;
                }, RoutingStrategies.Tunnel);
                var unpin = new Button { Content = PinIcon(), MinWidth = 44, MinHeight = 50, Padding = new Thickness(10), Classes = { "hud" } };
                AutomationProperties.SetName(unpin, "Unpin " + pin.Name);
                ToolTip.SetTip(unpin, "Unpin " + pin.Name);
                unpin.Click += (_, _) => { _session.RemovePin(pin.Path); PinFolderButton.Focus(); };
                var menuUp = new MenuItem { Header = "Move up" };
                var menuDown = new MenuItem { Header = "Move down" };
                var menuRemove = new MenuItem { Header = "Unpin folder" };
                menuUp.Click += (_, _) => _session.MovePin(pin.Path, -1);
                menuDown.Click += (_, _) => _session.MovePin(pin.Path, 1);
                menuRemove.Click += (_, _) => _session.RemovePin(pin.Path);
                open.ContextMenu = new ContextMenu { ItemsSource = new[] { menuUp, menuDown, menuRemove } };
                Grid.SetColumn(unpin, 1);
                row.Children.Add(open);
                row.Children.Add(unpin);
                PinnedEntries.Children.Add(row);
            }
        }
        PinsEmptyText.IsVisible = _session.State.Pins.Count == 0;
        foreach (var row in PinnedEntries.Children.OfType<Grid>())
        {
            if (row.Children[0] is Button open)
            {
                open.Classes.Set("active", PathBoundary.Comparer.Equals(open.Tag as string, _session.State.CurrentPath));
            }
        }
    }
    private void FocusPin(string path)
    {
        PinnedEntries.Children.OfType<Grid>().FirstOrDefault(row => PathBoundary.Comparer.Equals(row.Tag as string, path))?.Children.OfType<Button>().FirstOrDefault()?.Focus();
    }
    private void OnPinPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragPinPath is null || !e.GetCurrentPoint(PinnedEntries).Properties.IsLeftButtonPressed) { return; }
        var delta = e.GetPosition(PinnedEntries) - _pinPointerStart;
        if (!_pinDragging && Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y) < 8) { return; }
        _pinDragging = true;
        e.Pointer.Capture(PinnedEntries);
        PinnedEntries.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        e.Handled = true;
    }
    private void OnPinPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var path = _dragPinPath;
        var dragging = _pinDragging;
        _dragPinPath = null;
        _pinDragging = false;
        PinnedEntries.Cursor = null;
        if (!dragging || path is null) { return; }
        var point = e.GetPosition(PinnedEntries);
        var target = PinnedEntries.Children.Select((row, index) => (row, index)).OrderBy(item => Math.Abs(item.row.Bounds.Center.Y - point.Y)).FirstOrDefault();
        e.Pointer.Capture(null);
        var source = _session.State.Pins.ToList().FindIndex(pin => PathBoundary.Comparer.Equals(pin.Path, path));
        _session.MovePin(path, target.index - source);
        FocusPin(path);
        e.Handled = true;
    }
    private void OnPinCurrentClick(object? sender, RoutedEventArgs e) => _session.PinCurrent();
    private async void OnPinFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a folder to pin", AllowMultiple = false });
        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (path is not null && !_disposed) { await _session.PinFolderAsync(path); }
    }
}
