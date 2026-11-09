using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using OmniBrille.Core;
using OmniBrille.Desktop.Presentation;
using OmniBrille.Desktop.Rendering;
using OmniBrille.Infrastructure;

namespace OmniBrille.Desktop;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly SpatialWorkspaceSession _session;
    private readonly IVisualPreferencesStore _preferencesStore;
    private readonly bool _monitorChanges;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(PathBoundary.Comparer);
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(850) };
    private readonly DispatcherTimer _recheckTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(240) };
    private VisualPreferences _preferences;
    private IReadOnlyList<FileMapSearchHit>? _renderedSearch;
    private string? _startupRoot;
    private string _pinSignature = string.Empty;
    private string _rootSignature = string.Empty;
    private string _lastSubmittedSearchText = string.Empty;
    private bool _ready;
    private bool _syncing;
    private bool _disposed;
    private bool _searchVisible;
    private bool _interfaceHidden;
    private bool _sidebarCollapsed;
    private bool _refreshPending;
    private bool _updating;
    private bool _watcherBuildRunning;
    private bool _watcherReconcilePending;
    private string _watcherSignature = string.Empty;
    private int _watcherRevision;
    private int _refreshQueued;

    public MainWindow() : this(new SpatialWorkspaceSession()) { }

    public MainWindow(string? startupRoot, string? startupTheme) : this(new SpatialWorkspaceSession())
    {
        _startupRoot = startupRoot;
        if (startupTheme is not null)
        {
            _preferences = (_preferences with { Theme = startupTheme }).Normalize();
            ApplyPreferences();
        }
    }

    public MainWindow(SpatialWorkspaceSession session, IVisualPreferencesStore? preferencesStore = null, bool monitorChanges = true)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _preferencesStore = preferencesStore ?? new JsonVisualPreferencesStore();
        _monitorChanges = monitorChanges;
        _preferences = _preferencesStore.Load().Normalize();
        InitializeComponent();
        _ready = true;
        ApplyPreferences();
        _session.Changed += OnSessionChanged;
        GraphScene.NodeSelected += (_, id) => _session.Select(id);
        GraphScene.NodeActivated += async (_, id) =>
        {
            if (_session.NodeById(id)?.Kind == ExplorerNodeKind.File)
            {
                _session.Select(id);
                await _session.OpenSelectedAsync();
            }
            else { await _session.NavigateAsync(id); }
        };
        GraphScene.TextScaleChanged += (_, _) => UpdateView();
        GraphScene.BackRequested += async (_, _) => await _session.GoUpAsync();
        GraphScene.DismissRequested += (_, _) => { SettingsPanel.IsVisible = false; _session.Select(null); };
        KeyDown += OnWindowKeyDown;
        SearchResultsList.AddHandler(KeyDownEvent, OnSearchResultsKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        SizeChanged += (_, _) => UpdateView();
        Activated += (_, _) => { RememberEdgeDockScreen(); _edgeDock?.Hide(); GraphScene.SetMotionActivity(true); };
        Deactivated += OnEdgeDeactivated;
        PropertyChanged += OnEdgeWindowStateChanged;
        PositionChanged += OnEdgePositionChanged;
        _searchTimer.Tick += async (_, _) => await RunCurrentSearchAsync();
        _refreshTimer.Tick += async (_, _) => await RefreshPendingAsync();
        _recheckTimer.Tick += (_, _) =>
        {
            ReconcileWatchers(force: true);
            // A periodic bounded rescan recovers missed/overflow events and reconnected drives.
            QueueRefresh();
        };
        InitializePinDragging();
        Opened += (_, _) => { StartEdgeDock(); Initialization = InitializeAsync(); };
        Closed += (_, _) => Dispose();
        UpdateView();
    }

    public SpatialWorkspaceSession Session => _session;

    public Task Initialization { get; private set; } = Task.CompletedTask;

    private async Task InitializeAsync()
    {
        await _session.InitializeAsync(_startupRoot);
        if (_disposed) { return; }
        UpdateView();
        if (_monitorChanges) { _recheckTimer.Start(); }
    }

    private void OnSessionChanged()
    {
        if (_disposed) { return; }
        if (Dispatcher.UIThread.CheckAccess()) { UpdateView(); }
        else { Dispatcher.UIThread.Post(UpdateView); }
    }

    private void UpdateView()
    {
        if (!_ready || _disposed || _updating) { return; }
        _updating = true;
        try
        {
            var state = _session.State;
            var compact = ClientSize.Width > 0 && ClientSize.Width < 1050;
            var shortCanvas = ClientSize.Height > 0 && ClientSize.Height < 650;
            var textScale = GraphScene.TextScale;
            var overviewLimit = shortCanvas ? 0 : compact ? 1 : state.Roots.Count > 4 ? 2 : 6;
            _session.SetOverviewChildLimit((int)(overviewLimit / textScale));
            _session.SetFocusedPageSize(Math.Max(4, (int)((shortCanvas ? 4 : compact ? 8 : 24) / (textScale * textScale))));
            var sidebarWidth = compact ? 252 : 304;
            SidePanel.Width = _sidebarCollapsed ? 80 : sidebarWidth;
            SidePanel.IsVisible = !_interfaceHidden || _searchVisible;
            SidebarTabs.IsVisible = !_interfaceHidden;
            SearchInputPanel.IsVisible = !_interfaceHidden;
            PinnedTabButton.IsVisible = !_sidebarCollapsed;
            SearchTabButton.IsVisible = !_sidebarCollapsed;
            CollapsePinsButton.Content = _sidebarCollapsed ? "›" : "‹";
            AutomationProperties.SetName(CollapsePinsButton, _sidebarCollapsed ? "Expand sidebar" : "Collapse sidebar");
            PinnedPanel.IsVisible = !_sidebarCollapsed && !_searchVisible;
            SearchPanel.IsVisible = !_sidebarCollapsed && _searchVisible;
            PinnedTabButton.Classes.Set("active", !_searchVisible);
            SearchTabButton.Classes.Set("active", _searchVisible);
            TitleBar.IsVisible = !_interfaceHidden;
            Toolbar.IsVisible = !_interfaceHidden;
            ZoomHud.IsVisible = !_interfaceHidden;
            StatusPanel.IsVisible = !_interfaceHidden;
            BreadcrumbPanel.IsVisible = !_interfaceHidden && state.Roots.Count > 0;
            MinimalSearchPanel.IsVisible = _interfaceHidden;
            var left = SidePanel.IsVisible ? (_sidebarCollapsed ? 120 : sidebarWidth + 52) : 28;
            GraphScene.ScenePadding = new Thickness(left, _interfaceHidden ? 88 : 196, 36, 96);
            GraphScene.SearchActive = _searchVisible && _session.SearchQuery.Length > 0;
            GraphScene.SetWorkspaceScene(_session.Scene, _session.ClusterRootIds, _session.SelectedNodeId,
                _session.MatchCounts, _session.PreviewNodeId);
            BreadcrumbPanel.Margin = new Thickness(left, 128, 24, 0);
            BreadcrumbPanel.MaxWidth = Math.Max(200, ClientSize.Width - left - 60);
            BreadcrumbText.Text = _session.IsOverview ? "Workspace" : Path.GetFileName(_session.State.CurrentPath) is { Length: > 0 } name ? name : _session.State.CurrentPath;
            BreadcrumbDetailText.Text = _session.IsOverview
                ? $"{state.Roots.Count} folder{(state.Roots.Count == 1 ? string.Empty : "s")}"
                : DisplayLocation(state.CurrentRoot!, state.CurrentPath!);
            BackButton.IsEnabled = !_session.IsOverview && !_session.IsBusy;
            UpButton.IsEnabled = BackButton.IsEnabled;
            PinCurrentFolderButton.IsEnabled = !_session.IsOverview && !_session.IsBusy;
            WelcomePanel.IsVisible = !_interfaceHidden && state.Roots.Count == 0 && _session.IsOverview;
            WelcomePanel.Width = Math.Min(430, Math.Max(300, ClientSize.Width - left - 52));
            WelcomePanel.Margin = new Thickness(left, 80, 24, 0);
            StatusText.Text = _session.Status;
            ToolTip.SetTip(StatusText, _session.Status);
            SavedStatusText.Text = _session.Status.Contains("save", StringComparison.OrdinalIgnoreCase) ? _session.Status : "Workspace saved automatically";
            UpdatePinRows();
            UpdateRootRows();
            UpdateSearchRows();
            ShowResultButton.IsEnabled = SearchResultsList.SelectedItem is ListBoxItem { Tag: FileMapSearchHit };
            SearchSummaryText.Text = _session.IsSearching ? "Searching your workspace…"
                : _session.SearchQuery.Length == 0 ? "All workspace folders"
                : $"{_session.SearchResults.Count} matches · All workspace folders";
            var selected = _session.SelectedNodeId is { } selectedId ? _session.NodeById(selectedId) : null;
            FileDetailsPanel.IsVisible = !_interfaceHidden && !_searchVisible && selected?.Kind == ExplorerNodeKind.File;
            SelectedFileText.Text = selected?.Name;
            SelectedFilePathText.Text = selected?.Path;
            ReconcileWatchers();
        }
        finally { _updating = false; }
    }

    private void UpdateRootRows()
    {
        var signature = string.Join('\n', _session.State.Roots.Select(root => root.Path + "\t" + root.Label));
        if (signature == _rootSignature && RootsEntries.Children.Count == _session.State.Roots.Count) { return; }
        _rootSignature = signature;
        RootsEntries.Children.Clear();
        foreach (var root in _session.State.Roots)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,44") };
            var open = FolderButton(root.Path, root.Label);
            AutomationProperties.SetName(open, "Open workspace folder " + root.Label);
            open.Click += async (_, _) => await _session.OpenRootAsync(root.Path);
            var remove = new Button { Content = "×", Classes = { "hud", "icon" } };
            AutomationProperties.SetName(remove, "Remove " + root.Label + " from workspace");
            ToolTip.SetTip(remove, "Remove from workspace. Files stay in place.");
            remove.Click += (_, _) => _session.RemoveRoot(root.Path);
            Grid.SetColumn(remove, 1);
            row.Children.Add(open);
            row.Children.Add(remove);
            RootsEntries.Children.Add(row);
        }
    }

    private void UpdateSearchRows()
    {
        if (ReferenceEquals(_renderedSearch, _session.SearchResults)) { return; }
        _renderedSearch = _session.SearchResults;
        _syncing = true;
        try
        {
            var rows = new List<ListBoxItem>();
            foreach (var hit in _session.SearchResults)
            {
                var content = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*"), Margin = new Thickness(10, 12), ColumnSpacing = 9 };
                content.Children.Add(new TextBlock { Text = hit.Kind == ExplorerNodeKind.Folder ? "▱" : "▤", FontSize = 23, VerticalAlignment = VerticalAlignment.Center });
                var labels = new StackPanel { Spacing = 5 };
                labels.Children.Add(new TextBlock { Text = hit.Name, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis });
                labels.Children.Add(new TextBlock { Text = DisplayLocation(hit.RootPath, Path.GetDirectoryName(hit.Path) ?? hit.RootPath), FontSize = 11, Opacity = 0.8, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis });
                Grid.SetColumn(labels, 1);
                content.Children.Add(labels);
                var row = new ListBoxItem { Content = content, Tag = hit, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                AutomationProperties.SetName(row, hit.Name + ", " + DisplayLocation(hit.RootPath, hit.Path));
                ToolTip.SetTip(row, hit.Path);
                row.PointerEntered += async (_, _) => await _session.PreviewSearchHitAsync(hit);
                row.DoubleTapped += async (_, e) => { e.Handled = true; GraphScene.Focus(); await _session.ActivateSearchHitAsync(hit); };
                row.PointerExited += async (_, _) => await _session.PreviewSearchHitAsync(null);
                rows.Add(row);
            }
            SearchResultsList.ItemsSource = rows;
        }
        finally { _syncing = false; }
    }

    private string DisplayLocation(string root, string path)
    {
        var label = _session.State.Roots.FirstOrDefault(item => PathBoundary.Comparer.Equals(item.Path, root))?.Label ?? Path.GetFileName(root);
        var relative = Path.GetRelativePath(root, path);
        return relative == "." ? label : label + " / " + relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private void ShowSearch()
    {
        _sidebarCollapsed = false;
        _searchVisible = true;
        SettingsPanel.IsVisible = false;
        UpdateView();
        if (_interfaceHidden) { MinimalSearchBox.Focus(); } else { SearchBox.Focus(); }
    }

    private async void OnChooseFolderClick(object? sender, RoutedEventArgs e)
    {
        var storage = StorageProvider;
        if (storage is null) { return; }
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose folders for your workspace", AllowMultiple = true });
        var paths = folders.Select(folder => folder.TryGetLocalPath()).OfType<string>().ToArray();
        if (paths.Length > 0 && !_disposed) { await _session.AddRootsAsync(paths); }
    }

    private async void OnWorkspaceClick(object? sender, RoutedEventArgs e) { GraphScene.ResetView(); await _session.ShowWorkspaceAsync(); }
    private async void OnUpClick(object? sender, RoutedEventArgs e) => await _session.GoUpAsync();
    private async void OnRefreshClick(object? sender, RoutedEventArgs e) => await _session.RefreshAsync();
    private void OnSearchToggleClick(object? sender, RoutedEventArgs e) => ShowSearch();
    private async void OnPinnedTabClick(object? sender, RoutedEventArgs e)
    {
        _searchTimer.Stop();
        _searchVisible = false;
        _syncing = true;
        SearchBox.Text = string.Empty;
        MinimalSearchBox.Text = string.Empty;
        _syncing = false;
        _lastSubmittedSearchText = string.Empty;
        await _session.SearchAsync(string.Empty);
        UpdateView();
    }
    private async void OnSearchClick(object? sender, RoutedEventArgs e) => await RunCurrentSearchAsync(force: true);

    private async Task RunCurrentSearchAsync(bool force = false)
    {
        _searchTimer.Stop();
        var text = (SearchBox.Text ?? string.Empty).Trim();
        if (!force && string.Equals(text, _lastSubmittedSearchText, StringComparison.Ordinal)) { return; }
        // TextChanged can arrive after an explicit submission. Remember the submitted text
        // before awaiting acquisition so its delayed echo cannot replace focused result rows.
        _lastSubmittedSearchText = text;
        await _session.SearchAsync(text);
    }
    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_ready || _syncing) { return; }
        _syncing = true;
        if (ReferenceEquals(sender, MinimalSearchBox))
        {
            if (SearchBox.Text != MinimalSearchBox.Text) { SearchBox.Text = MinimalSearchBox.Text; }
        }
        else if (MinimalSearchBox.Text != SearchBox.Text) { MinimalSearchBox.Text = SearchBox.Text; }
        _syncing = false;
        _searchTimer.Stop();
        if (!string.Equals((SearchBox.Text ?? string.Empty).Trim(), _lastSubmittedSearchText, StringComparison.Ordinal)) { _searchTimer.Start(); }
    }
    private async void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await RunCurrentSearchAsync(force: true); }
        else if (e.Key == Key.Escape) { OnPinnedTabClick(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Down)
        {
            if (SearchResultsList.ItemCount > 0)
            {
                SearchResultsList.SelectedIndex = 0;
                SearchResultsList.ScrollIntoView(SearchResultsList.SelectedItem!);
                (SearchResultsList.SelectedItem as ListBoxItem)?.Focus();
            }
            e.Handled = true;
        }
    }
    private async void OnSearchSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready) { return; }
        ShowResultButton.IsEnabled = SearchResultsList.SelectedItem is ListBoxItem { Tag: FileMapSearchHit };
        if (!_syncing && SearchResultsList.SelectedItem is ListBoxItem { Tag: FileMapSearchHit hit }) { await _session.PreviewSearchHitAsync(hit); }
    }
    private async void OnShowResultClick(object? sender, RoutedEventArgs e)
    {
        if (SearchResultsList.SelectedItem is ListBoxItem { Tag: FileMapSearchHit hit }) { await _session.ShowSearchHitInMapAsync(hit); }
    }
    private async void OnSearchResultsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await ActivateSelectedSearchAsync(); }
        else if (e.Key == Key.Escape) { OnPinnedTabClick(this, new RoutedEventArgs()); e.Handled = true; }
    }
    private async Task ActivateSelectedSearchAsync()
    {
        if (SearchResultsList.SelectedItem is ListBoxItem { Tag: FileMapSearchHit hit }) { GraphScene.Focus(); await _session.ActivateSearchHitAsync(hit); }
    }
    private async void OnMinimalSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) { return; }
        e.Handled = true;
        ShowSearch();
        await RunCurrentSearchAsync(force: true);
    }
    private void OnInterfaceToggleClick(object? sender, RoutedEventArgs e)
    {
        _interfaceHidden = !_interfaceHidden;
        SettingsPanel.IsVisible = false;
        UpdateView();
        if (_interfaceHidden) { MinimalSearchBox.Focus(); } else { InterfaceToggleButton.Focus(); }
    }
    private void OnCollapseSidebarClick(object? sender, RoutedEventArgs e) { _sidebarCollapsed = !_sidebarCollapsed; UpdateView(); }
    private void OnSettingsClick(object? sender, RoutedEventArgs e) => SettingsPanel.IsVisible = !SettingsPanel.IsVisible;
    private async void OnOpenFileClick(object? sender, RoutedEventArgs e) => await _session.OpenSelectedAsync();
    private void OnZoomInClick(object? sender, RoutedEventArgs e) => GraphScene.ZoomIn();
    private void OnZoomOutClick(object? sender, RoutedEventArgs e) => GraphScene.ZoomOut();
    private void OnFitClick(object? sender, RoutedEventArgs e) => GraphScene.ResetView();
    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
    private void OnTitlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.Source is Button) { return; }
        if (e.ClickCount == 2) { OnMaximizeClick(this, new RoutedEventArgs()); }
        else { BeginMoveDrag(e); }
    }

    private void ApplyPreferences()
    {
        if (!_ready) { return; }
        _syncing = true;
        RequestedThemeVariant = _preferences.Theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        ThemePicker.SelectedIndex = _preferences.Theme == "Light" ? 1 : 0;
        ReducedMotionToggle.IsChecked = _preferences.ReducedMotion;
        ReducedEffectsToggle.IsChecked = _preferences.ReducedEffects;
        GraphScene.ReducedMotion = _preferences.ReducedMotion;
        GraphScene.ReducedEffects = _preferences.ReducedEffects;
        foreach (var row in PinnedEntries.Children.OfType<Grid>())
        {
            if (row.Children[0] is Button { Content: Grid content } && content.Children[0] is PinnedFolderIcon icon) { icon.ReducedEffects = _preferences.ReducedEffects; }
        }
        _syncing = false;
        GraphScene.InvalidateVisual();
    }
    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _syncing) { return; }
        _preferences = _preferences with { Theme = ThemePicker.SelectedIndex == 1 ? "Light" : "Dark" };
        _preferencesStore.Save(_preferences);
        ApplyPreferences();
    }
    private void OnPreferencesChanged(object? sender, RoutedEventArgs e)
    {
        if (!_ready || _syncing) { return; }
        _preferences = _preferences with { ReducedMotion = ReducedMotionToggle.IsChecked == true, ReducedEffects = ReducedEffectsToggle.IsChecked == true };
        _preferencesStore.Save(_preferences);
        ApplyPreferences();
    }
    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.H) { OnInterfaceToggleClick(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F) { ShowSearch(); e.Handled = true; }
        else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.P) { _session.PinCurrent(); e.Handled = true; }
        else if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Home) { await _session.ShowWorkspaceAsync(); e.Handled = true; }
        else if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Left) { await _session.GoUpAsync(); e.Handled = true; }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.R) { await _session.RefreshAsync(); e.Handled = true; }
    }

    private async void ReconcileWatchers(bool force = false)
    {
        if (!_monitorChanges || _disposed) { return; }
        var roots = _session.State.Roots.Select(root => root.Path).ToArray();
        var signature = string.Join('\n', roots);
        if (!force && signature == _watcherSignature) { return; }
        _watcherSignature = signature;
        var revision = ++_watcherRevision;
        foreach (var path in _watchers.Keys.Where(path => !roots.Contains(path, PathBoundary.Comparer)).ToArray())
        {
            _watchers[path].Dispose();
            _watchers.Remove(path);
        }
        if (_watcherBuildRunning) { _watcherReconcilePending = true; return; }
        _watcherBuildRunning = true;
        var replacements = await Task.Run(() => CreateWatchers(roots));
        _watcherBuildRunning = false;
        if (_disposed || revision != _watcherRevision)
        {
            foreach (var watcher in replacements.Values) { watcher.Dispose(); }
        }
        else
        {
            foreach (var watcher in _watchers.Values) { watcher.Dispose(); }
            _watchers.Clear();
            foreach (var pair in replacements) { _watchers.Add(pair.Key, pair.Value); }
            // Accepted watchers are tracked before native subscription begins. A root
            // removal or close can dispose them even if another drive is unresponsive.
            _ = Task.Run(() => StartWatchers(replacements.Values));
        }
        if (_watcherReconcilePending && !_disposed)
        {
            _watcherReconcilePending = false;
            ReconcileWatchers(force: true);
        }
    }

    private Dictionary<string, FileSystemWatcher> CreateWatchers(string[] roots)
    {
        var watchers = new Dictionary<string, FileSystemWatcher>(PathBoundary.Comparer);
        foreach (var path in roots)
        {
            // Metadata probes and native subscriptions stay off the UI thread. The same
            // authority check as navigation rejects links, including redirected ancestors.
            if (_disposed || !FileMapPathGuard.IsOrdinaryPath(path, path, requireDirectory: true)) { continue; }
            FileSystemWatcher? watcher = null;
            try
            {
                watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                };
                watcher.Created += OnFilesystemChanged;
                watcher.Deleted += OnFilesystemChanged;
                watcher.Changed += OnFilesystemChanged;
                watcher.Renamed += OnFilesystemChanged;
                watcher.Error += (_, _) => QueueRefresh();
                watchers.Add(path, watcher);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                ArgumentException or PlatformNotSupportedException or System.Security.SecurityException)
            {
                watcher?.Dispose();
                // The bounded periodic refresh still works if a native watch is unavailable.
            }
        }
        return watchers;
    }
    private static void StartWatchers(IEnumerable<FileSystemWatcher> watchers)
    {
        foreach (var watcher in watchers)
        {
            try
            {
                if (FileMapPathGuard.IsOrdinaryPath(watcher.Path, watcher.Path, requireDirectory: true)) { watcher.EnableRaisingEvents = true; }
                else { watcher.Dispose(); }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                ArgumentException or PlatformNotSupportedException or System.Security.SecurityException or ObjectDisposedException)
            {
                // Removed roots can dispose a watcher before this worker starts it.
                watcher.Dispose();
            }
        }
    }

    private void OnFilesystemChanged(object sender, FileSystemEventArgs e) => QueueRefresh();
    private void QueueRefresh()
    {
        // Coalesce before dispatch so a filesystem burst cannot flood the UI queue.
        if (_disposed || Interlocked.Exchange(ref _refreshQueued, 1) != 0) { return; }
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) { return; }
            _refreshPending = true;
            _refreshTimer.Stop();
            _refreshTimer.Start();
        });
    }
    private async Task RefreshPendingAsync()
    {
        if (_disposed) { _refreshTimer.Stop(); return; }
        if (!_refreshPending) { _refreshTimer.Stop(); return; }
        if (_session.IsBusy || _session.IsSearching) { return; }
        _refreshTimer.Stop();
        _refreshPending = false;
        Interlocked.Exchange(ref _refreshQueued, 0);
        await _session.RefreshAsync();
    }
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        StopEdgeDock();
        _session.Changed -= OnSessionChanged;
        _searchTimer.Stop();
        _refreshTimer.Stop();
        _recheckTimer.Stop();
        foreach (var watcher in _watchers.Values) { watcher.Dispose(); }
        _watchers.Clear();
        _session.Dispose();
        GC.SuppressFinalize(this);
    }
}
