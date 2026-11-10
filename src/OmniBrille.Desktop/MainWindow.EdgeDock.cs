using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace OmniBrille.Desktop;

public sealed partial class MainWindow
{
    private EdgeDockWindow? _edgeDock;
    private Screen? _edgeDockScreen;
    private WindowState _lastVisibleWindowState = WindowState.Normal;
    private bool _edgeDockStarted;
    private long _edgeDockGeneration;

    internal EdgeDockWindow? EdgeDock => _edgeDock;

    internal void OnEdgeDeactivated(object? sender, EventArgs e)
    {
        GraphScene.SetMotionActivity(false);
        RememberEdgeDockScreen();
        QueueShowEdgeDock();
    }

    private void OnEdgeActivated(object? sender, EventArgs e)
    {
        if (_disposed) { return; }
        if (WindowState == WindowState.Minimized) { QueueShowEdgeDock(); return; }
        _edgeDockGeneration++;
        RememberEdgeDockScreen();
        _edgeDock?.Hide();
        GraphScene.SetMotionActivity(true);
    }

    private void StartEdgeDock()
    {
        if (_edgeDockStarted || _disposed) { return; }
        _edgeDockStarted = true;
        Screens.Changed += OnEdgeScreensChanged;
        RememberEdgeDockScreen();
        if (WindowState == WindowState.Minimized) { QueueShowEdgeDock(); }
    }

    private void RememberEdgeDockScreen()
    {
        if (!_edgeDockStarted || _disposed || WindowState == WindowState.Minimized) { return; }
        _edgeDockScreen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
    }

    private void ShowEdgeDock()
    {
        if (!_edgeDockStarted || _disposed || !IsVisible) { return; }
        _edgeDock ??= CreateEdgeDock();
        var screen = _edgeDockScreen ?? Screens.Primary;
        if (screen is not null) { _edgeDock.AlignTo(screen); }
        if (!_edgeDock.IsVisible) { _edgeDock.Show(); }
    }

    private void QueueShowEdgeDock()
    {
        if (!_edgeDockStarted || _disposed) { return; }
        var generation = ++_edgeDockGeneration;
        // Defer until native minimize/deactivation messages have completed.
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && generation == _edgeDockGeneration) { ShowEdgeDock(); }
        }, DispatcherPriority.Background);
    }

    private EdgeDockWindow CreateEdgeDock()
    {
        var dock = new EdgeDockWindow();
        dock.OpenRequested += OnEdgeOpenRequested;
        dock.Closed += OnEdgeDockClosed;
        return dock;
    }

    private void OnEdgeDockClosed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _edgeDock)) { _edgeDock = null; }
    }

    private void OnEdgeOpenRequested(object? sender, EventArgs e)
    {
        if (_disposed) { return; }
        _edgeDockGeneration++;
        if (WindowState == WindowState.Minimized) { WindowState = _lastVisibleWindowState; }
        Show();
        Activate();
        _edgeDock?.Hide();
    }

    private void OnEdgeWindowStateChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != WindowStateProperty || !_edgeDockStarted || _disposed) { return; }
        if (WindowState == WindowState.Minimized) { QueueShowEdgeDock(); }
        else
        {
            _lastVisibleWindowState = WindowState;
            RememberEdgeDockScreen();
            if (IsActive) { _edgeDock?.Hide(); }
        }
    }

    private void OnEdgePositionChanged(object? sender, PixelPointEventArgs e) => RememberEdgeDockScreen();

    private void OnEdgeScreensChanged(object? sender, EventArgs e)
    {
        if (_disposed) { return; }
        _edgeDockScreen = WindowState == WindowState.Minimized ? Screens.Primary : Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (_edgeDock?.IsVisible == true && _edgeDockScreen is not null) { _edgeDock.AlignTo(_edgeDockScreen); }
    }

    private void StopEdgeDock()
    {
        _edgeDockGeneration++;
        if (_edgeDockStarted) { Screens.Changed -= OnEdgeScreensChanged; }
        if (_edgeDock is not null)
        {
            _edgeDock.OpenRequested -= OnEdgeOpenRequested;
            _edgeDock.Closed -= OnEdgeDockClosed;
            _edgeDock.Close();
            _edgeDock = null;
        }
        _edgeDockStarted = false;
        _edgeDockScreen = null;
    }
}
