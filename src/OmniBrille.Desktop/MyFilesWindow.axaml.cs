using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OmniBrille.Core;
using OmniBrille.Desktop.Presentation;
using OmniBrille.Infrastructure;

namespace OmniBrille.Desktop;

/// <summary>A standalone, native-control projection of the local saved file map.</summary>
public sealed partial class MyFilesWindow : Window, IDisposable
{
    private readonly MyFilesSession _session;
    private readonly string? _startupRoot;
    private readonly IVisualPreferencesStore _preferencesStore;
    private readonly Dictionary<string, Button> _nodeButtons = new(StringComparer.Ordinal);
    private readonly Stack<IReadOnlyList<FileMapLocation>> _back = new();
    private int _uiGeneration;
    private bool _ready;
    private bool _disposed;
    private string? _focusAfterRender;

    public MyFilesWindow() : this(null, null, null, null)
    {
    }

    public MyFilesWindow(string? startupRoot = null, string? startupTheme = null, MyFilesSession? session = null, IVisualPreferencesStore? preferencesStore = null)
    {
        InitializeComponent();
        _session = session ?? new MyFilesSession();
        _startupRoot = startupRoot;
        _preferencesStore = preferencesStore ?? new JsonVisualPreferencesStore();
        var theme = startupTheme ?? _preferencesStore.Load().Theme;
        RequestedThemeVariant = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Dark : ThemeVariant.Light;
        _session.Changed += OnSessionChanged;
        Opened += OnOpened;
        Closed += OnClosed;
        KeyDown += OnWindowKeyDown;
        Render();
    }

    public MyFilesSession Session => _session;

    private async void OnOpened(object? sender, EventArgs e)
    {
        _ready = true;
        ThemePicker.SelectedIndex = RequestedThemeVariant == ThemeVariant.Dark ? 1 : 0;
        await ExecuteAsync(() => _session.InitializeAsync(_startupRoot));
        TextSizePicker.SelectedIndex = _session.State.TextSize >= 26 ? 2 : _session.State.TextSize >= 22 ? 1 : 0;
    }

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Changed -= OnSessionChanged;
        _session.Dispose();
    }

    private void OnSessionChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Render();
        }
        else
        {
            Dispatcher.UIThread.Post(Render);
        }
    }

    private IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush
        ? brush : Brushes.Gray;

    private Color RootColor(FileMapNode node)
    {
        // Saved root order is the visual identity; descendants inherit their root's color.
        var index = 0;
        while (index < _session.State.Roots.Count && _session.State.Roots[index].Path != node.RootPath)
        {
            index++;
        }

        string[] colors = ActualThemeVariant == ThemeVariant.Dark
            ? ["#81BDFF", "#BC94FF", "#FFB16E", "#63DECE", "#FFD36A", "#F5A1CE"]
            : ["#2468A8", "#7740B0", "#A65012", "#16766D", "#876000", "#A53371"];
        return Color.Parse(colors[index % colors.Length]);
    }

    private void AddConnection(StreamGeometry geometry, Color color)
    {
        if (ActualThemeVariant == ThemeVariant.Dark)
        {
            MapCanvas.Children.Add(new Avalonia.Controls.Shapes.Path
            {
                Data = geometry,
                Stroke = new SolidColorBrush(color, 0.10),
                StrokeThickness = 7,
                IsHitTestVisible = false,
            });
        }

        MapCanvas.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2,
            IsHitTestVisible = false,
        });
    }

    private static Avalonia.Controls.Shapes.Path NodeIcon(bool folder, Color color) => new()
    {
        Data = Geometry.Parse(folder
            ? "M 3,7 L 12,7 L 17,12 L 32,12 Q 35,12 35,15 L 35,29 Q 35,32 32,32 L 5,32 Q 2,32 2,29 L 2,10 Q 2,7 3,7 Z"
            : "M 8,3 L 23,3 L 32,12 L 32,33 L 8,33 Z M 23,3 L 23,12 L 32,12 M 13,19 L 26,19 M 13,25 L 24,25"),
        Stroke = new SolidColorBrush(color),
        StrokeThickness = 2.2,
        Width = 32,
        Height = 32,
        Stretch = Stretch.Uniform,
        IsHitTestVisible = false,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 2, 14, 0),
    };

    private void Render()
    {
        if (_disposed)
        {
            return;
        }

        StatusText.Text = _session.Status;
        AutomationProperties.SetName(StatusText, _session.Status);
        BackButton.IsEnabled = _back.Count > 0 && !_session.IsBusy;
        FindButton.IsEnabled = !_session.IsBusy;
        RefreshButton.IsEnabled = !_session.IsBusy;
        var focusedKey = _focusAfterRender ?? _nodeButtons.FirstOrDefault(pair => pair.Value.IsKeyboardFocusWithin).Key;
        RenderMap();
        RenderPins();
        RenderSearch();
        RenderRootSettings();
        RenderDetails();
        if (focusedKey is not null && _nodeButtons.TryGetValue(focusedKey, out var focused))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed && focused.IsAttachedToVisualTree())
                {
                    focused.Focus();
                    focused.BringIntoView();
                    if (_focusAfterRender == focusedKey)
                    {
                        _focusAfterRender = null;
                    }
                }
            }, DispatcherPriority.Loaded);
        }
    }

    private void RenderMap()
    {
        _nodeButtons.Clear();
        MapCanvas.Children.Clear();
        var nodes = _session.VisibleNodes;
        Welcome.IsVisible = nodes.Count == 0;
        MapHint.Text = nodes.Count == 0 ? "Your map starts here." : "Select a folder to expand it. Select a file to open its details.";
        if (nodes.Count == 0)
        {
            return;
        }

        var cardWidth = 300d;
        var cardHeight = 120 + _session.State.TextSize * 2;
        double NodeHeight(FileMapNode node) => cardHeight + (node.Expanded && node.PageCount > 1 ? 52 : 0);
        var gap = 22d;
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
        var children = nodes.Where(n => n.ParentKey is not null).GroupBy(n => n.ParentKey!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        double Place(FileMapNode node, double top)
        {
            var height = NodeHeight(node);
            if (children.TryGetValue(node.Key, out var descendants))
            {
                var cursor = top;
                foreach (var child in descendants)
                {
                    cursor += Place(child, cursor) + gap;
                }

                height = Math.Max(height, cursor - top - gap);
            }

            positions[node.Key] = new Point(32 + node.Depth * (cardWidth + 74), top + (height - NodeHeight(node)) / 2);
            return height;
        }

        var y = 20d;
        foreach (var root in nodes.Where(n => n.ParentKey is null))
        {
            y += Place(root, y) + gap;
        }

        var overview = nodes.All(node => node.ParentKey is null);
        if (overview)
        {
            var rows = (int)Math.Ceiling(nodes.Count / 2d);
            for (var index = 0; index < nodes.Count; index++)
            {
                positions[nodes[index].Key] = new Point(index < rows ? 32 : 790, 24 + (index % rows) * (cardHeight + 30));
            }

            var center = new Point(471, 24 + (rows * (cardHeight + 30) - 30) / 2 - 46);
            var home = new Border
            {
                Width = 180,
                Height = 92,
                CornerRadius = new CornerRadius(18),
                Background = Brush("HomeSurfaceBrush"),
                BorderBrush = Brush("AccentBrush"),
                BorderThickness = new Thickness(2),
                BoxShadow = ActualThemeVariant == ThemeVariant.Dark ? BoxShadows.Parse("0 0 24 1 #4779CDFF") : default,
                Child = new TextBlock { Text = "My Files", FontSize = 27, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            Canvas.SetLeft(home, center.X);
            Canvas.SetTop(home, center.Y);
            foreach (var node in nodes)
            {
                var point = positions[node.Key];
                var onLeft = point.X < center.X;
                var start = new Point(onLeft ? center.X : center.X + 180, center.Y + 46);
                var end = new Point(onLeft ? point.X + cardWidth : point.X, point.Y + cardHeight / 2);
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    context.BeginFigure(start, false);
                    context.CubicBezierTo(new Point((start.X + end.X) / 2, start.Y), new Point((start.X + end.X) / 2, end.Y), end);
                    context.EndFigure(false);
                }

                AddConnection(geometry, RootColor(node));
            }

            MapCanvas.Children.Add(home);
            y = 24 + rows * (cardHeight + 30);
        }

        MapCanvas.Width = Math.Max(700, positions.Values.Max(p => p.X) + cardWidth + 32);
        MapCanvas.Height = Math.Max(480, y);
        foreach (var node in nodes.Where(n => n.ParentKey is not null))
        {
            if (!positions.TryGetValue(node.ParentKey!, out var parent))
            {
                continue;
            }

            var child = positions[node.Key];
            var parentNode = nodes.First(candidate => candidate.Key == node.ParentKey);
            var start = new Point(parent.X + cardWidth, parent.Y + NodeHeight(parentNode) / 2);
            var end = new Point(child.X, child.Y + NodeHeight(node) / 2);
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(start, false);
                context.CubicBezierTo(new Point(start.X + 38, start.Y), new Point(end.X - 38, end.Y), end);
                context.EndFigure(false);
            }

            AddConnection(geometry, RootColor(node));
        }

        foreach (var node in nodes)
        {
            var position = positions[node.Key];
            var card = new Grid { Width = cardWidth, Height = NodeHeight(node), RowDefinitions = new RowDefinitions("*,Auto") };
            var folder = node.Entry.Kind == ExplorerNodeKind.Folder;
            var color = RootColor(node);
            var text = new StackPanel { Spacing = 7 };
            text.Children.Add(new TextBlock
            {
                Text = node.Entry.Name,
                FontSize = overview ? _session.State.TextSize + 6 : _session.State.TextSize,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 3,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            text.Children.Add(new TextBlock
            {
                Text = folder ? (node.Expanded ? "−  Collapse folder" : "+  Expand folder") : FileType(node.Entry.Path),
                FontSize = 14,
                Foreground = Brush("SecondaryTextBrush"),
            });
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(NodeIcon(folder, color));
            Grid.SetColumn(text, 1);
            content.Children.Add(text);
            var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            button.Resources["NodeAccentBrush"] = new SolidColorBrush(color);
            button.Classes.Add("mapNode");
            if (_session.SelectedNode?.Key == node.Key)
            {
                button.Classes.Add("selected");
            }

            AutomationProperties.SetName(button, $"{node.Entry.Name}, {(folder ? "folder" : "file")}, {(folder ? node.Expanded ? "expanded" : "collapsed" : "select to see details")}");
            AutomationProperties.SetHelpText(button, node.Entry.Path);
            ToolTip.SetTip(button, node.Entry.Path);
            button.Click += async (_, _) => await ActivateNodeAsync(node);
            card.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(14),
                BoxShadow = ActualThemeVariant == ThemeVariant.Dark ? new BoxShadows(new BoxShadow
                {
                    Blur = 18,
                    Color = Color.FromArgb(28, color.R, color.G, color.B),
                }) : default,
                Child = button,
            });
            _nodeButtons[node.Key] = button;
            if (folder && node.Expanded && node.PageCount > 1)
            {
                var pager = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
                var previous = new Button { Content = "‹", IsEnabled = node.PageIndex > 0, Padding = new Thickness(8) };
                var next = new Button { Content = "›", IsEnabled = node.PageIndex + 1 < node.PageCount, Padding = new Thickness(8) };
                AutomationProperties.SetName(previous, $"Previous files in {node.Entry.Name}");
                AutomationProperties.SetName(next, $"Next files in {node.Entry.Name}");
                previous.Click += async (_, _) => { _focusAfterRender = node.Key; await ExecuteAsync(() => _session.ChangePageAsync(node.Key, -1)); };
                next.Click += async (_, _) => { _focusAfterRender = node.Key; await ExecuteAsync(() => _session.ChangePageAsync(node.Key, 1)); };
                pager.Children.Add(previous);
                var count = new TextBlock { Text = $"{node.PageIndex + 1} / {node.PageCount}", FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(count, 1);
                pager.Children.Add(count);
                Grid.SetColumn(next, 2);
                pager.Children.Add(next);
                Grid.SetRow(pager, 1);
                card.Children.Add(pager);
            }

            Canvas.SetLeft(card, position.X);
            Canvas.SetTop(card, position.Y);
            MapCanvas.Children.Add(card);
        }
    }

    private async Task ActivateNodeAsync(FileMapNode node)
    {
        var generation = ++_uiGeneration;
        _focusAfterRender = node.Key;
        if (node.Entry.Kind == ExplorerNodeKind.Folder)
        {
            var before = _session.State.ExpandedFolders.ToArray();
            await ExecuteAsync(() => _session.ToggleFolderAsync(node.Key));
            if (generation == _uiGeneration && !before.SequenceEqual(_session.State.ExpandedFolders))
            {
                _back.Push(before);
                BackButton.IsEnabled = true;
            }
        }
        else
        {
            _session.Select(node.Key);
            DetailsPanel.IsVisible = true;
            CloseSettings();
        }
    }

    private void RenderDetails()
    {
        var selected = _session.SelectedNode;
        if (selected is null || selected.Entry.Kind != ExplorerNodeKind.File)
        {
            DetailsPanel.IsVisible = false;
            return;
        }

        FileNameText.Text = selected.Entry.Name;
        FilePathText.Text = selected.Entry.Path;
        FileInfoText.Text = FileType(selected.Entry.Path) + (selected.Entry.SizeBytes is long size ? $" · {size:N0} bytes" : string.Empty);
        OpenFileButton.IsEnabled = !_session.IsBusy;
        PinFileButton.IsEnabled = !_session.IsBusy;
    }

    private void RenderPins()
    {
        PinsPanel.Children.Clear();
        PinsSection.IsVisible = _session.State.Pins.Count > 0;
        foreach (var pin in _session.State.Pins)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 6) };
            var button = new Button { Content = pin.Name, MaxWidth = 260 };
            AutomationProperties.SetName(button, $"Show pinned file {pin.Name}");
            button.Click += async (_, _) => await RevealAsync(pin.RootPath, pin.Path);
            var remove = new Button { Content = "×", Padding = new Thickness(8), Margin = new Thickness(3, 0, 0, 0) };
            AutomationProperties.SetName(remove, $"Unpin {pin.Name} from Current Work");
            remove.Click += (_, _) => { _session.Unpin(pin.Path); HomeButton.Focus(); };
            panel.Children.Add(button);
            panel.Children.Add(remove);
            PinsPanel.Children.Add(panel);
        }
    }

    private void RenderSearch()
    {
        SearchResults.Children.Clear();
        foreach (var hit in _session.SearchResults)
        {
            var panel = new StackPanel { Spacing = 3 };
            panel.Children.Add(new TextBlock { Text = hit.Name, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = hit.Path, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = Brush("SecondaryTextBrush") });
            var button = new Button { Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(button, $"Show {hit.Name} in map, {hit.Path}");
            button.Click += async (_, _) => await RevealAsync(hit.RootPath, hit.Path);
            SearchResults.Children.Add(button);
        }
    }

    private async Task RevealAsync(string rootPath, string path)
    {
        var generation = ++_uiGeneration;
        await ExecuteAsync(() => _session.RevealAsync(rootPath, path));
        if (_disposed || generation != _uiGeneration)
        {
            return;
        }

        SearchPanel.IsVisible = false;
        var match = _session.VisibleNodes.FirstOrDefault(n => n.RootPath == rootPath && n.Entry.Path == path);
        if (match is not null)
        {
            _focusAfterRender = match.Key;
            _session.Select(match.Key);
            DetailsPanel.IsVisible = match.Entry.Kind == ExplorerNodeKind.File;
        }
    }

    private void RenderRootSettings()
    {
        RootSettings.Children.Clear();
        foreach (var root in _session.State.Roots)
        {
            var row = new StackPanel { Spacing = 5 };
            row.Children.Add(new TextBlock { Text = root.Label, FontWeight = FontWeight.SemiBold });
            row.Children.Add(new TextBlock { Text = root.Path, FontSize = 14, TextWrapping = TextWrapping.Wrap });
            var remove = new Button { Content = "Remove from map" };
            AutomationProperties.SetName(remove, $"Remove {root.Label} from map, keep files");
            remove.Click += (_, _) => { _uiGeneration++; _back.Clear(); _session.RemoveRoot(root.Path); SettingsButton.Focus(); };
            row.Children.Add(remove);
            RootSettings.Children.Add(row);
        }
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // A later user action owns the visible state.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            StatusText.Text = "That location is unavailable. Choose another folder or refresh the map.";
            AutomationProperties.SetName(StatusText, StatusText.Text);
        }
    }

    private void OnHome(object? sender, RoutedEventArgs e)
    {
        _uiGeneration++;
        _back.Clear();
        _session.Home();
        SearchPanel.IsVisible = false;
        CloseSettings();
        DetailsPanel.IsVisible = false;
        MapScroll.Offset = default;
        HomeButton.Focus();
    }

    private async void OnBack(object? sender, RoutedEventArgs e)
    {
        _uiGeneration++;
        if (_back.TryPop(out var branches))
        {
            await ExecuteAsync(() => _session.RestoreBranchesAsync(branches));
        }

        BackButton.IsEnabled = _back.Count > 0;
    }

    private void OnSearchToggle(object? sender, RoutedEventArgs e)
    {
        _uiGeneration++;
        SearchPanel.IsVisible = !SearchPanel.IsVisible;
        CloseSettings();
        if (SearchPanel.IsVisible)
        {
            SearchBox.Focus();
        }
    }

    private void CloseSettings()
    {
        SettingsPanel.IsVisible = false;
        MapScroll.IsEnabled = true;
        AddFolderButton.IsEnabled = true;
        PinsSection.IsEnabled = true;
        Welcome.IsEnabled = true;
    }

    private void OnSettings(object? sender, RoutedEventArgs e)
    {
        _uiGeneration++;
        SettingsPanel.IsVisible = !SettingsPanel.IsVisible;
        MapScroll.IsEnabled = !SettingsPanel.IsVisible;
        AddFolderButton.IsEnabled = !SettingsPanel.IsVisible;
        PinsSection.IsEnabled = !SettingsPanel.IsVisible;
        Welcome.IsEnabled = !SettingsPanel.IsVisible;
        DetailsPanel.IsVisible = false;
        SearchPanel.IsVisible = false;
        if (SettingsPanel.IsVisible)
        {
            TextSizePicker.Focus();
        }
        else
        {
            SettingsButton.Focus();
        }
    }

    private async void OnSearch(object? sender, RoutedEventArgs e)
    {
        _uiGeneration++;
        await ExecuteAsync(() => _session.SearchAsync(SearchBox.Text ?? string.Empty));
    }

    private async void OnSearchKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _uiGeneration++;
            await ExecuteAsync(() => _session.SearchAsync(SearchBox.Text ?? string.Empty));
        }
    }

    private async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        _uiGeneration++;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Connect a folder to My Files", AllowMultiple = false });
        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (path is not null)
        {
            await ExecuteAsync(() => _session.AddRootAsync(path, FolderLabelBox.Text));
            FolderLabelBox.Text = string.Empty;
            CloseSettings();
        }
    }

    private async void OnDocuments(object? sender, RoutedEventArgs e) => await ExecuteAsync(() => _session.AddRootAsync(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));

    private async void OnDesktop(object? sender, RoutedEventArgs e) => await ExecuteAsync(() => _session.AddRootAsync(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));

    private async void OnRefresh(object? sender, RoutedEventArgs e) => await ExecuteAsync(_session.RefreshAsync);

    private async void OnOpenFile(object? sender, RoutedEventArgs e) => await ExecuteAsync(_session.OpenSelectedAsync);

    private void OnPin(object? sender, RoutedEventArgs e) => _session.PinSelected();

    private void OnCloseDetails(object? sender, RoutedEventArgs e) => DetailsPanel.IsVisible = false;

    private void OnTextSizeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_ready && TextSizePicker.SelectedItem is ComboBoxItem { Tag: string value } && double.TryParse(value, CultureInfo.InvariantCulture, out var size))
        {
            _session.SetTextSize(size);
        }
    }

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        RequestedThemeVariant = ThemePicker.SelectedIndex == 1 ? ThemeVariant.Dark : ThemeVariant.Light;
        _preferencesStore.Save(_preferencesStore.Load() with { Theme = ThemePicker.SelectedIndex == 1 ? "Dark" : "Light" });
        Render();
    }

    private void OnAdvanced(object? sender, RoutedEventArgs e)
    {
        var root = _session.State.Roots.Count > 0 ? _session.State.Roots[0].Path : null;
        new MainWindow(root, RequestedThemeVariant == ThemeVariant.Dark ? "Dark" : "Light").Show(this);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _uiGeneration++;
            SearchPanel.IsVisible = false;
            CloseSettings();
            DetailsPanel.IsVisible = false;
            HomeButton.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _uiGeneration++;
            CloseSettings();
            SearchPanel.IsVisible = true;
            SearchBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Home && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            OnHome(sender, e);
            e.Handled = true;
        }
    }

    private static string FileType(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "PDF document",
        ".doc" or ".docx" => "Word document",
        ".xls" or ".xlsx" or ".csv" => "Spreadsheet",
        ".ppt" or ".pptx" => "Presentation",
        ".png" or ".jpg" or ".jpeg" or ".webp" => "Image",
        ".txt" or ".md" => "Text document",
        var extension when extension.Length > 1 => extension[1..].ToUpperInvariant() + " file",
        _ => "File",
    };
}
