using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Platform.Storage;
using OmniBrille.Core;
using OmniBrille.Desktop.Rendering;

namespace OmniBrille.Desktop;

public sealed partial class MainWindow
{
    private readonly IPinnedFoldersStore _pinStore;
    private PinnedFolders _pins;
    private bool _pinsCollapsed;
    private long _pinOpenGeneration;
    private string? _dragPinPath;
    private Point _pinPointerStart;
    private bool _pinDragging;
    private readonly Dictionary<string, Button> _pinButtons = new(PathBoundary.Comparer);
    private static readonly Geometry PinGeometry = Geometry.Parse("M 8,2 L 16,2 L 14,6 L 17,11 L 12,11 L 12,18 L 10,20 L 10,11 L 5,11 L 8,6 Z");

    private void InitializePinnedFolders()
    {
        PinnedEntries.AddHandler(PointerMovedEvent, OnPinPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        PinnedEntries.AddHandler(PointerReleasedEvent, OnPinPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        PinnedEntries.PointerCaptureLost += (_, e) =>
        {
            if (ReferenceEquals(e.Source, PinnedEntries)) { _dragPinPath = null; _pinDragging = false; PinnedEntries.Cursor = null; }
        };
        BuildPinnedRows();
    }

    private static Avalonia.Controls.Shapes.Path PinIcon()
    {
        var icon = new Avalonia.Controls.Shapes.Path
        {
            Data = PinGeometry,
            StrokeThickness = 1.3,
            Width = 19,
            Height = 20,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
        };
        icon.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, new DynamicResourceExtension("SecondaryTextBrush"));
        return icon;
    }

    private void BuildPinnedRows()
    {
        PinnedEntries.Children.Clear();
        _pinButtons.Clear();
        foreach (var folder in _pins.Folders)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,44"), Tag = folder.Path };
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("34,*"), ColumnSpacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
            content.Children.Add(new PinnedFolderIcon(folder.Path) { ReducedEffects = _preferences.ReducedEffects });
            var name = new TextBlock
            {
                Text = folder.Name,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(name, 1);
            content.Children.Add(name);
            var open = new Button
            {
                Content = content,
                MinHeight = 50,
                Padding = new Thickness(7, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Tag = folder.Path,
            };
            open.Classes.Add("hud");
            open.Classes.Add("pin-open");
            AutomationProperties.SetName(open, "Open pinned folder " + folder.Name);
            AutomationProperties.SetHelpText(open, "Opens this folder in the spatial graph. Alt+Up or Alt+Down reorders the pin. " + folder.Path);
            ToolTip.SetTip(open, folder.Path);
            open.Click += async (_, _) => { if (!_pinDragging) { await OpenPinnedFolderAsync(folder); } };
            open.KeyDown += (_, e) =>
            {
                if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Up or Key.Down)
                {
                    MovePin(folder.Path, e.Key == Key.Up ? -1 : 1);
                    e.Handled = true;
                }
            };
            open.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (!e.GetCurrentPoint(open).Properties.IsLeftButtonPressed) { return; }
                _dragPinPath = folder.Path;
                _pinPointerStart = e.GetPosition(PinnedEntries);
                _pinDragging = false;
            }, RoutingStrategies.Tunnel);
            var remove = new Button { Content = PinIcon(), MinWidth = 44, MinHeight = 50, Padding = new Thickness(10) };
            remove.Classes.Add("hud");
            AutomationProperties.SetName(remove, "Unpin " + folder.Name);
            ToolTip.SetTip(remove, "Unpin " + folder.Name);
            remove.Click += (_, _) => RemovePin(folder.Path);
            Grid.SetColumn(remove, 1);
            var up = new MenuItem { Header = "Move up" };
            var down = new MenuItem { Header = "Move down" };
            var unpin = new MenuItem { Header = "Unpin folder" };
            up.Click += (_, _) => MovePin(folder.Path, -1);
            down.Click += (_, _) => MovePin(folder.Path, 1);
            unpin.Click += (_, _) => RemovePin(folder.Path);
            open.ContextMenu = new ContextMenu { ItemsSource = new[] { up, down, unpin } };
            row.Children.Add(open);
            row.Children.Add(remove);
            PinnedEntries.Children.Add(row);
            _pinButtons[folder.Path] = open;
        }
        PinsEmptyText.IsVisible = _pins.Folders.Count == 0;
        UpdatePinnedFolderState();
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
        var target = PinnedEntries.Children.Select((row, index) => (row, index))
            .OrderBy(item => Math.Abs(item.row.Bounds.Center.Y - point.Y)).FirstOrDefault();
        e.Pointer.Capture(null);
        var source = _pins.Folders.ToList().FindIndex(item => PathBoundary.Comparer.Equals(item.Path, path));
        MovePin(path, target.index - source);
        e.Handled = true;
    }

    private void MovePin(string path, int offset)
    {
        var folders = _pins.Folders.ToList();
        var index = folders.FindIndex(item => PathBoundary.Comparer.Equals(item.Path, path));
        if (index < 0) { return; }
        var target = Math.Clamp(index + offset, 0, folders.Count - 1);
        if (index != target)
        {
            var folder = folders[index];
            folders.RemoveAt(index);
            folders.Insert(target, folder);
            SavePins(folders, "Pin order saved.");
        }
        _pinButtons.GetValueOrDefault(path)?.Focus();
    }

    private void RemovePin(string path)
    {
        SavePins(_pins.Folders.Where(item => !PathBoundary.Comparer.Equals(item.Path, path)).ToArray(), "Folder unpinned.");
        PinFolderButton.Focus();
    }

    private void SavePins(IReadOnlyList<PinnedFolder> folders, string message)
    {
        _pins = new PinnedFolders(folders).Normalize();
        var saved = _pinStore.TrySave(_pins);
        BuildPinnedRows();
        SetPinStatus(saved ? message : "Changed for this session. Could not save pins to your profile.");
    }

    private void SetPinStatus(string text)
    {
        PinnedStatusText.Text = text;
        PinnedStatusText.IsVisible = !string.IsNullOrEmpty(text);
        AnnounceStatus(text);
    }

    private async void OnPinFolderClick(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) { SetPinStatus("The folder picker is unavailable."); return; }
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pin a folder", AllowMultiple = false });
        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (path is not null && !_isDisposed) { await AddPinnedFolderAsync(path); }
    }

    private async void OnPinCurrentFolderClick(object? sender, RoutedEventArgs e)
    {
        // A connected display path is never a source of filesystem authority.
        if (_session.ProviderMode != ExplorerProviderMode.Standalone || _session.Neighborhood?.Focus.Kind != ExplorerNodeKind.Folder) { return; }
        await AddPinnedFolderAsync(_session.CurrentPath);
    }

    private static bool IsOrdinaryFolder(string path)
    {
        try
        {
            return Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return false;
        }
    }

    private async Task AddPinnedFolderAsync(string path)
    {
        if (!FileMapWorkspace.TryNormalizePath(path, out var normalized) || !await Task.Run(() => IsOrdinaryFolder(normalized)))
        {
            if (!_isDisposed) { SetPinStatus("Choose an available folder, not a link or shortcut."); }
            return;
        }
        if (_isDisposed) { return; }
        if (_pins.Folders.Any(item => PathBoundary.Comparer.Equals(item.Path, normalized)))
        {
            SetPinStatus("That folder is already pinned.");
            return;
        }
        if (_pins.Folders.Count >= PinnedFolders.MaximumFolders)
        {
            SetPinStatus("You can keep 12 pins. Unpin a folder to add another.");
            return;
        }
        SavePins(_pins.Folders.Append(new PinnedFolder(normalized, Path.GetFileName(normalized))).ToArray(), "Folder pinned.");
    }

    private async Task OpenPinnedFolderAsync(PinnedFolder folder)
    {
        var request = ++_pinOpenGeneration;
        var providerGeneration = _session.ProviderGeneration;
        var operationGeneration = _session.OperationGeneration;
        var available = await Task.Run(() => IsOrdinaryFolder(folder.Path));
        if (_isDisposed || request != _pinOpenGeneration || providerGeneration != _session.ProviderGeneration || operationGeneration != _session.OperationGeneration) { return; }
        if (!available) { SetPinStatus("Folder unavailable. Reconnect its drive or unpin it."); return; }
        // An explicit pin activation selects this saved local root. No connected path is used.
        _connection.UseStandalone();
        SettingsPanel.IsVisible = false;
        ConnectionPanel.IsVisible = false;
        AccessibleListPanel.IsVisible = false;
        ContextFilterPanel.IsVisible = false;
        HistoryPanel.IsVisible = false;
        SearchResultsPanel.IsVisible = false;
        _detailsDismissed = true;
        CollapseSearchEditor();
        var opening = OpenRootAsync(folder.Path);
        var openedGeneration = _session.ProviderGeneration;
        var openedOperation = _session.OperationGeneration;
        await opening;
        if (_isDisposed || request != _pinOpenGeneration || openedGeneration != _session.ProviderGeneration || openedOperation != _session.OperationGeneration) { return; }
        SetPinStatus(_session.CurrentFailure == ExplorerFailureKind.None ? string.Empty : "This folder could not be read. Try another pin.");
        GraphScene.Focus();
    }

    private void OnPinsCollapseClick(object? sender, RoutedEventArgs e)
    {
        _pinsCollapsed = !_pinsCollapsed;
        UpdatePinnedFolderState();
        PinsCollapseButton.Focus();
    }

    private void UpdatePinnedFolderState()
    {
        PinnedFoldersPanel.IsVisible = !_interfaceHidden && !HistoryPanel.IsVisible;
        PinnedFoldersBody.IsVisible = !_pinsCollapsed;
        PinsCollapseButton.Content = _pinsCollapsed ? "›" : "⌄";
        AutomationProperties.SetName(PinsCollapseButton, _pinsCollapsed ? "Expand pinned folders" : "Collapse pinned folders");
        PinCurrentFolderButton.IsEnabled = _session.ProviderMode == ExplorerProviderMode.Standalone &&
            _session.Neighborhood?.Focus.Kind == ExplorerNodeKind.Folder && !_session.IsLoading;
        // The middle row scrolls; header, pin actions and status stay inside the viewport.
        PinnedFoldersPanel.MaxHeight = Math.Max(140, ClientSize.Height - 208);
        WelcomePanel.Margin = PinnedFoldersPanel.IsVisible && !_pinsCollapsed ? new Thickness(260, 72, 0, 0) : new Thickness(0);
        WelcomePanel.Width = Math.Min(460, Math.Max(300, ClientSize.Width - 300));
        var reserved = PinnedFoldersPanel.IsVisible && !_pinsCollapsed ? 274 : 20;
        var emptyGuidance = StructureEmptyPanel.IsVisible || ContextEmptyPanel.IsVisible;
        foreach (var panel in new[] { StructureEmptyPanel, ContextEmptyPanel })
        {
            panel.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Margin = new Thickness(reserved, 0, 20, 72);
            panel.Width = Math.Min(380, Math.Max(300, ClientSize.Width - reserved - 40));
        }
        GraphScene.ScenePadding = new Thickness(reserved, PinnedFoldersPanel.IsVisible && _pinsCollapsed ? 230 : 170, 20, emptyGuidance ? 270 : 70);
        foreach (var pair in _pinButtons)
        {
            if (pair.Value.Content is Grid content && content.Children[0] is PinnedFolderIcon icon)
            {
                icon.ReducedEffects = _preferences.ReducedEffects;
            }
            pair.Value.Classes.Set("current-pin", _session.ProviderMode == ExplorerProviderMode.Standalone &&
                PathBoundary.Comparer.Equals(pair.Key, _session.CurrentPath));
        }
    }
}
