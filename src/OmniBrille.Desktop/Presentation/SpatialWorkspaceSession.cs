using OmniBrille.Core;
using OmniBrille.Infrastructure;

namespace OmniBrille.Desktop.Presentation;

/// <summary>Owns explicit local roots, hierarchy navigation, and transient search previews.</summary>
public sealed class SpatialWorkspaceSession : IDisposable
{
    public const int MaximumSceneNodes = 64;
    private const int MaximumPageSize = 60;
    private readonly ISpatialWorkspaceStore _store;
    private readonly Func<string, IExplorerProvider> _providerFactory;
    private readonly IFileActivationService _activation;
    private CancellationTokenSource? _navigationCancellation;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _previewCancellation;
    private long _navigationVersion;
    private long _searchVersion;
    private long _previewVersion;
    private long _workspaceVersion;
    private long _rootAdditionVersion;
    private SceneResult _baseScene = SceneResult.Empty;
    private SceneResult _fullOverviewScene = SceneResult.Empty;
    private FileMapSearchHit? _previewHit;
    private int _pageOffset;
    private int _overviewChildLimit = 6;
    private int _focusedPageSize = MaximumPageSize;
    private bool _disposed;
    private bool _saveFailed;

    public SpatialWorkspaceSession(
        ISpatialWorkspaceStore? store = null,
        Func<string, IExplorerProvider>? providerFactory = null,
        IFileActivationService? activation = null)
    {
        _store = store ?? new JsonSpatialWorkspaceStore();
        _providerFactory = providerFactory ?? (root => new FileSystemExplorerProvider(root));
        _activation = activation ?? new ShellFileActivationService();
    }

    public event Action? Changed;
    public SpatialWorkspace State { get; private set; } = SpatialWorkspace.Empty;
    public string? CurrentRoot => State.CurrentRoot;
    public string? CurrentPath => State.CurrentPath;
    public bool IsOverview => State.CurrentRoot is null;
    public ExplorerNeighborhood? Scene => _baseScene.Scene;
    public IReadOnlyList<string> ClusterRootIds => _baseScene.ClusterRoots;
    public IReadOnlyList<FileMapSearchHit> SearchResults { get; private set; } = [];
    public IReadOnlyDictionary<string, int> MatchCounts { get; private set; } = new Dictionary<string, int>(PathBoundary.Comparer);
    public string? PreviewNodeId { get; private set; }
    public string? SelectedNodeId { get; private set; }
    public string Status { get; private set; } = "Choose folders to build your workspace.";
    public bool IsBusy { get; private set; }
    public bool IsSearching { get; private set; }
    public string SearchQuery { get; private set; } = string.Empty;

    // The length prefix makes the root/path pair unambiguous even if a legal path contains '|'.
    public static string Key(string root, string path)
    {
        // Graph identity is ordinal. Canonicalize Windows path casing once at this
        // boundary so pins, directory reads, and search share the same exact IDs.
        var identityRoot = OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root;
        var identityPath = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
        return $"{identityRoot.Length}:{identityRoot}|{identityPath}";
    }
    public ExplorerNode? NodeById(string? id) => id is null ? null : Scene?.Nodes.FirstOrDefault(node => PathBoundary.Comparer.Equals(node.Id, id));
    public string? RootForNode(string? id) => id is not null && _baseScene.Locations.TryGetValue(id, out var location) ? location.Root : null;

    public void SetOverviewChildLimit(int limit)
    {
        limit = Math.Clamp(limit, 0, 6);
        if (_disposed || limit == _overviewChildLimit) { return; }
        _overviewChildLimit = limit;
        if (!IsOverview) { return; }
        _baseScene = LimitOverview(_fullOverviewScene);
        if (PreviewNodeId is not null && _previewHit is { } hit) { PreviewNodeId = FindVisiblePreviewNode(hit); }
        RecountMatches();
        Notify();
    }

    public void SetFocusedPageSize(int pageSize)
    {
        pageSize = Math.Clamp(pageSize, 4, MaximumPageSize);
        if (_disposed || pageSize == _focusedPageSize) { return; }
        _focusedPageSize = pageSize;
        if (IsOverview || _baseScene.FocusedSnapshot is not { } snapshot || State.CurrentRoot is not { } root || State.CurrentPath is not { } path) { return; }
        _baseScene = BuildFocusedScene(root, path, snapshot, _pageOffset, NodeById(SelectedNodeId)?.Path);
        _pageOffset = _baseScene.Scene!.AggregatePage!.Offset;
        if (PreviewNodeId is not null && _previewHit is { } hit) { PreviewNodeId = FindVisiblePreviewNode(hit); }
        RecountMatches();
        Notify();
    }

    public async Task InitializeAsync(string? startupRoot = null)
    {
        if (_disposed) { return; }
        State = _store.Load().Normalize();
        if (!string.IsNullOrWhiteSpace(startupRoot))
        {
            var navigationVersion = _navigationVersion;
            var workspaceVersion = _workspaceVersion;
            var addition = AddRootsAsync([startupRoot]);
            var additionVersion = _rootAdditionVersion;
            await addition;
            if (!_disposed && Scene is null && navigationVersion == _navigationVersion &&
                workspaceVersion == _workspaceVersion && additionVersion == _rootAdditionVersion)
            {
                await LoadSceneAsync(State, persist: false);
            }
            return;
        }
        await LoadSceneAsync(State, persist: false);
    }

    public async Task AddRootsAsync(IEnumerable<string> paths)
    {
        if (_disposed) { return; }
        ArgumentNullException.ThrowIfNull(paths);
        var candidates = paths.Take(64).ToArray();
        var workspaceVersion = _workspaceVersion;
        var navigationVersion = _navigationVersion;
        var additionVersion = ++_rootAdditionVersion;
        var accepted = await Task.Run(() => candidates.Where(candidate =>
            FileMapWorkspace.TryNormalizePath(candidate, out var path) && FileMapPathGuard.IsOrdinaryPath(path, path, requireDirectory: true)).ToArray());
        if (_disposed || workspaceVersion != _workspaceVersion || navigationVersion != _navigationVersion || additionVersion != _rootAdditionVersion) { return; }
        var roots = State.Roots.ToList();
        var rejected = false;
        foreach (var candidate in candidates)
        {
            if (!FileMapWorkspace.TryNormalizePath(candidate, out var path) || !accepted.Contains(candidate, PathBoundary.Comparer))
            {
                rejected = true;
                continue;
            }
            if (roots.Any(root => PathBoundary.Comparer.Equals(root.Path, path))) { continue; }
            if (roots.Count == SpatialWorkspace.MaximumRoots) { rejected = true; break; }
            roots.Add(new FileMapRoot(path, NameOf(path)));
        }
        if (roots.Count == State.Roots.Count)
        {
            if (rejected) { SetStatus($"Some folders could not be added. Choose accessible folders; the workspace supports {SpatialWorkspace.MaximumRoots} roots."); }
            return;
        }
        InvalidateWorkspace();
        State = (State with { Roots = roots, CurrentRoot = null, CurrentPath = null }).Normalize();
        Save();
        var load = LoadSceneAsync(State, persist: false);
        workspaceVersion = _workspaceVersion;
        navigationVersion = _navigationVersion;
        var searchVersion = _searchVersion;
        var previewVersion = _previewVersion;
        await load;
        if (_disposed || workspaceVersion != _workspaceVersion || navigationVersion != _navigationVersion ||
            searchVersion != _searchVersion || previewVersion != _previewVersion || additionVersion != _rootAdditionVersion) { return; }
        if (rejected) { SetStatus($"Some folders could not be added. Choose accessible folders; the workspace supports {SpatialWorkspace.MaximumRoots} roots."); }
        if (!string.IsNullOrEmpty(SearchQuery)) { await SearchAsync(SearchQuery); }
    }

    public void RemoveRoot(string path)
    {
        if (_disposed) { return; }
        InvalidateWorkspace();
        State = (State with { Roots = State.Roots.Where(root => !PathBoundary.Comparer.Equals(root.Path, path)).ToArray() }).Normalize();
        Save();
        _ = RefreshAfterRootChangeAsync();
    }

    private async Task RefreshAfterRootChangeAsync()
    {
        await LoadSceneAsync(State, persist: false);
        if (!_disposed && !string.IsNullOrEmpty(SearchQuery)) { await SearchAsync(SearchQuery); }
    }

    public async Task NavigateAsync(string nodeId)
    {
        if (_disposed || !_baseScene.Locations.TryGetValue(nodeId, out var location)) { return; }
        var node = NodeById(nodeId);
        if (node is null) { return; }
        if (_baseScene.Pages.TryGetValue(nodeId, out var page))
        {
            await LoadSceneAsync(State with { CurrentRoot = location.Root, CurrentPath = location.Path }, pageOffset: page);
        }
        else if (node.Kind == ExplorerNodeKind.Folder && node.IsNavigable)
        {
            await NavigatePathAsync(location.Root, location.Path);
        }
        else { Select(nodeId); }
    }

    public async Task GoUpAsync()
    {
        if (_disposed || State.CurrentRoot is not { } root || State.CurrentPath is not { } path) { return; }
        if (PathBoundary.Comparer.Equals(root, path)) { await ShowWorkspaceAsync(); return; }
        var parent = Path.GetDirectoryName(path);
        if (parent is not null && FileMapWorkspace.IsWithin(root, parent)) { await NavigatePathAsync(root, parent); }
        else { await ShowWorkspaceAsync(); }
    }

    public Task ShowWorkspaceAsync() => LoadSceneAsync(State with { CurrentRoot = null, CurrentPath = null });

    public Task OpenRootAsync(string savedRootPath)
    {
        var root = State.Roots.FirstOrDefault(item => PathBoundary.Comparer.Equals(item.Path, savedRootPath));
        return root is null || _disposed ? Task.CompletedTask : NavigatePathAsync(root.Path, root.Path);
    }

    public async Task RefreshAsync()
    {
        if (_disposed || IsBusy) { return; }
        var hover = _previewHit;
        var query = SearchQuery;
        var workspaceVersion = _workspaceVersion;
        var load = LoadSceneAsync(State, persist: false, pageOffset: _pageOffset);
        var navigationVersion = _navigationVersion;
        var searchVersion = _searchVersion;
        var previewVersion = _previewVersion;
        await load;
        if (_disposed || workspaceVersion != _workspaceVersion || navigationVersion != _navigationVersion ||
            searchVersion != _searchVersion || previewVersion != _previewVersion || !string.Equals(query, SearchQuery, StringComparison.Ordinal)) { return; }
        if (!string.IsNullOrEmpty(query))
        {
            var search = SearchAsync(query);
            searchVersion = _searchVersion;
            previewVersion = _previewVersion;
            await search;
        }
        if (!_disposed && workspaceVersion == _workspaceVersion && navigationVersion == _navigationVersion && searchVersion == _searchVersion && previewVersion == _previewVersion && hover is not null)
        {
            await PreviewSearchHitAsync(hover);
        }
    }

    private Task NavigatePathAsync(string root, string path, string? selectedPath = null)
    {
        if (!IsAuthorizedRoot(root) || !FileMapWorkspace.IsWithin(root, path))
        {
            SetStatus("This folder is unavailable or is no longer inside an authorized root.");
            return Task.CompletedTask;
        }
        return LoadSceneAsync(State with { CurrentRoot = root, CurrentPath = path }, selectedPath: selectedPath);
    }

    private async Task LoadSceneAsync(SpatialWorkspace next, bool persist = true, int pageOffset = 0, string? selectedPath = null)
    {
        if (_disposed) { return; }
        Cancel(ref _navigationCancellation);
        _navigationCancellation = new CancellationTokenSource();
        var token = _navigationCancellation.Token;
        var version = ++_navigationVersion;
        var workspaceVersion = _workspaceVersion;
        ClearPreview();
        IsBusy = true;
        Notify();
        try
        {
            if (persist && next.CurrentRoot is { } requestedRoot && next.CurrentPath is { } requestedPath &&
                !await Task.Run(() => FileMapPathGuard.IsOrdinaryPath(requestedRoot, requestedPath, requireDirectory: true), token))
            {
                if (!_disposed && version == _navigationVersion && workspaceVersion == _workspaceVersion) { Status = "This folder is unavailable. Your current location is unchanged."; }
                return;
            }
            var result = await BuildSceneAsync(next.Normalize(), pageOffset, selectedPath, token);
            if (_disposed || token.IsCancellationRequested || version != _navigationVersion || workspaceVersion != _workspaceVersion) { return; }
            // Pin edits can occur while a provider is reading. A navigation result owns only
            // the location, never the roots or shortcuts captured before its await.
            var committed = new SpatialWorkspace(State.Roots, State.Pins, next.CurrentRoot, next.CurrentPath).Normalize();
            if (next.CurrentRoot is not null && committed.CurrentRoot is null) { return; }
            State = committed;
            if (IsOverview) { _fullOverviewScene = result; result = LimitOverview(result); }
            _baseScene = result;
            _pageOffset = result.Scene?.AggregatePage?.Offset ?? 0;
            SelectedNodeId = selectedPath is null ? null : result.Scene?.Nodes.FirstOrDefault(node => PathBoundary.Comparer.Equals(node.Path, selectedPath))?.Id;
            if (persist) { Save(); }
            Status = result.Scene?.Warning ?? (IsOverview ? $"{State.Roots.Count} workspace folders" : NameOf(State.CurrentPath!));
            RecountMatches();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            if (version == _navigationVersion && workspaceVersion == _workspaceVersion) { Status = "The folder could not be read. Your saved roots are unchanged."; }
        }
        finally
        {
            if (!_disposed && version == _navigationVersion) { IsBusy = false; Notify(); }
        }
    }

    public async Task SearchAsync(string query)
    {
        if (_disposed) { return; }
        query = (query ?? string.Empty).Trim();
        if (query.Length > 160) { query = query[..160]; }
        Cancel(ref _searchCancellation);
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;
        var version = ++_searchVersion;
        var workspaceVersion = _workspaceVersion;
        var roots = State.Roots.ToArray();
        SearchQuery = query;
        SearchResults = [];
        ClearPreview();
        RecountMatches();
        IsSearching = query.Length != 0 && roots.Length != 0;
        Notify();
        if (!IsSearching) { return; }
        try
        {
            var perRoot = Math.Max(1, 80 / roots.Length);
            var searches = roots.Select(async root =>
            {
                if (!await Task.Run(() => FileMapPathGuard.IsOrdinaryPath(root.Path, root.Path, requireDirectory: true), token)) { return (Hits: Array.Empty<FileMapSearchHit>(), Incomplete: true); }
                try
                {
                    var provider = ProviderFor(root.Path);
                    if (provider is not IExplorerSearchProvider search) { return (Hits: Array.Empty<FileMapSearchHit>(), Incomplete: true); }
                    var found = await search.SearchAsync(new SearchRequest(root.Path, query, perRoot, Math.Max(1, 500 / roots.Length)), token);
                    token.ThrowIfCancellationRequested();
                    var hits = found.Hits.Take(perRoot).Where(hit =>
                        hit.Kind is ExplorerNodeKind.Folder or ExplorerNodeKind.File &&
                        FileMapWorkspace.TryNormalizePath(hit.Path, out var normalized) && FileMapWorkspace.IsWithin(root.Path, normalized))
                        .Select(hit => new FileMapSearchHit(root.Path, hit with { Id = Key(root.Path, Path.GetFullPath(hit.Path)), Path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(hit.Path)), NavigationTarget = null, ParentNavigationTarget = null })).ToArray();
                    return (Hits: hits, Incomplete: found.WasTruncated || found.Warning is not null);
                }
                catch (Exception exception) when (IsRecoverable(exception)) { return (Hits: Array.Empty<FileMapSearchHit>(), Incomplete: true); }
            });
            var results = await Task.WhenAll(searches);
            if (_disposed || token.IsCancellationRequested || version != _searchVersion || workspaceVersion != _workspaceVersion) { return; }
            var seen = new HashSet<string>(PathBoundary.Comparer);
            SearchResults = results.SelectMany(result => result.Hits).Where(hit => seen.Add(hit.Path)).Take(80).ToArray();
            RecountMatches();
            Status = $"{SearchResults.Count} matches across {roots.Length} workspace folders" + (results.Any(result => result.Incomplete) ? " · bounded search; some folders or results may be omitted" : string.Empty);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (!_disposed && version == _searchVersion) { IsSearching = false; Notify(); }
        }
    }

    public async Task PreviewSearchHitAsync(FileMapSearchHit? hit)
    {
        if (_disposed) { return; }
        ClearPreview();
        if (hit is null) { Notify(); return; }
        if (IsBusy) { Notify(); return; }
        if (!IsSearchHitAuthorized(hit)) { Notify(); return; }
        _previewHit = hit;
        RecountMatches();
        Notify();
        _previewCancellation = new CancellationTokenSource();
        var token = _previewCancellation.Token;
        var version = ++_previewVersion;
        var workspaceVersion = _workspaceVersion;
        var navigationVersion = _navigationVersion;
        try
        {
            var available = await Task.Run(() => FileMapPathGuard.IsOrdinaryPath(hit.RootPath, hit.Path,
                requireDirectory: hit.Kind == ExplorerNodeKind.Folder), token);
            if (_disposed || token.IsCancellationRequested || version != _previewVersion || workspaceVersion != _workspaceVersion || navigationVersion != _navigationVersion) { return; }
            if (!available) { Notify(); return; }
            // Hover never adds a branch or moves the map. Locate the result's
            // closest visible ancestor inside the same explicitly chosen root.
            PreviewNodeId = FindVisiblePreviewNode(hit);
            RecountMatches();
            Notify();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            if (version == _previewVersion) { SetStatus("This search result is no longer available."); }
        }
    }

    public async Task ActivateSearchHitAsync(FileMapSearchHit hit)
    {
        if (_disposed || !IsSearchHitAuthorized(hit)) { return; }
        if (hit.Kind == ExplorerNodeKind.Folder) { await NavigatePathAsync(hit.RootPath, hit.Path); }
        else
        {
            var parent = Path.GetDirectoryName(hit.Path);
            if (parent is not null) { await NavigatePathAsync(hit.RootPath, parent, hit.Path); }
        }
    }

    public void PinCurrent()
    {
        if (State.CurrentPath is { } path) { _ = PinFolderAsync(path); }
    }

    public async Task PinFolderAsync(string candidate)
    {
        if (_disposed) { return; }
        if (!FileMapWorkspace.TryNormalizePath(candidate, out var path))
        {
            SetStatus("This folder could not be pinned. It may be unavailable or a symbolic link.");
            return;
        }
        var version = _workspaceVersion;
        var available = await Task.Run(() => FileMapPathGuard.IsOrdinaryPath(path, path, requireDirectory: true));
        if (_disposed || version != _workspaceVersion) { return; }
        if (!available) { SetStatus("This folder could not be pinned. It may be unavailable or a symbolic link."); return; }
        if (State.Pins.Any(pin => PathBoundary.Comparer.Equals(pin.Path, path))) { SetStatus("This folder is already pinned."); return; }
        if (State.Pins.Count == SpatialWorkspace.MaximumPins) { SetStatus($"You can pin up to {SpatialWorkspace.MaximumPins} folders. Remove one to add another."); return; }
        State = (State with { Pins = State.Pins.Append(new PinnedFolder(path, NameOf(path))).ToArray() }).Normalize();
        Save();
        SetStatus("Folder pinned.");
    }

    public void RemovePin(string path)
    {
        if (_disposed) { return; }
        var currentRoot = State.CurrentRoot;
        State = (State with { Pins = State.Pins.Where(pin => !PathBoundary.Comparer.Equals(pin.Path, path)).ToArray() }).Normalize();
        Save();
        if (currentRoot is not null && State.CurrentRoot is null)
        {
            InvalidateWorkspace();
            _ = LoadSceneAsync(State, persist: false);
        }
        else { SetStatus("Pin removed."); }
    }

    public void MovePin(string path, int direction)
    {
        if (_disposed) { return; }
        var pins = State.Pins.ToList();
        var index = pins.FindIndex(pin => PathBoundary.Comparer.Equals(pin.Path, path));
        if (index < 0 || direction == 0) { return; }
        var destination = (int)Math.Clamp((long)index + direction, 0, pins.Count - 1);
        if (destination == index) { return; }
        var moved = pins[index];
        pins.RemoveAt(index);
        pins.Insert(destination, moved);
        State = State with { Pins = pins };
        Save();
        SetStatus("Pin order saved.");
    }

    public Task OpenPinAsync(string path)
    {
        var pin = State.Pins.FirstOrDefault(item => PathBoundary.Comparer.Equals(item.Path, path));
        if (_disposed || pin is null) { return Task.CompletedTask; }
        var root = State.Roots.Where(item => FileMapWorkspace.IsWithin(item.Path, pin.Path)).OrderByDescending(item => item.Path.Length).FirstOrDefault()?.Path ?? pin.Path;
        return NavigatePathAsync(root, pin.Path);
    }

    public void Select(string? nodeId)
    {
        if (_disposed) { return; }
        SelectedNodeId = NodeById(nodeId) is null ? null : nodeId;
        Notify();
    }

    public async Task OpenSelectedAsync()
    {
        if (_disposed || SelectedNodeId is not { } id || NodeById(id) is not { } node || !_baseScene.Locations.TryGetValue(id, out var location)) { return; }
        if (node.Kind is ExplorerNodeKind.Folder or ExplorerNodeKind.Aggregate) { await NavigateAsync(id); return; }
        var version = _workspaceVersion;
        if (node.Kind != ExplorerNodeKind.File || !IsAuthorizedRoot(location.Root) || !await Task.Run(() => FileMapPathGuard.IsOrdinaryPath(location.Root, location.Path, requireDirectory: false)))
        {
            SetStatus("This file is no longer available inside its root.");
            return;
        }
        if (_disposed || version != _workspaceVersion || !IsAuthorizedRoot(location.Root)) { return; }
        try
        {
            var result = await _activation.OpenAsync(location.Root, location.Path);
            if (!_disposed && version == _workspaceVersion) { SetStatus(result == FileActivationResult.Opened ? "Opened file." : $"Could not open file: {result}."); }
        }
        catch (Exception exception) when (IsRecoverable(exception)) { if (!_disposed && version == _workspaceVersion) { SetStatus("The file could not be opened."); } }
    }

    private async Task<SceneResult> BuildSceneAsync(SpatialWorkspace state, int pageOffset, string? selectedPath, CancellationToken token)
    {
        var nodes = new List<ExplorerNode>();
        var edges = new List<ExplorerEdge>();
        var clusterRoots = new List<string>();
        var locations = new Dictionary<string, Location>(PathBoundary.Comparer);
        var pages = new Dictionary<string, int>(PathBoundary.Comparer);
        var warnings = new List<string>();
        var hidden = 0;
        var total = 0;
        var truncated = false;
        var overview = state.CurrentRoot is null;
        var roots = overview ? state.Roots : [new FileMapRoot(state.CurrentRoot!, NameOf(state.CurrentRoot!))];
        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            var path = overview ? root.Path : state.CurrentPath!;
            var snapshot = await ReadAsync(root.Path, path, token);
            token.ThrowIfCancellationRequested();
            if (!overview) { return BuildFocusedScene(root.Path, path, snapshot, pageOffset, selectedPath); }
            var focus = Node(root.Path, snapshot.Focus with { Name = root.Label });
            nodes.Add(focus);
            locations[focus.Id] = new Location(root.Path, path);
            clusterRoots.Add(focus.Id);
            if (snapshot.Warning is not null) { warnings.Add(snapshot.Warning); }
            var children = snapshot.Children;
            var visible = children.Take(6).ToArray();
            foreach (var entry in visible)
            {
                var node = Node(root.Path, entry);
                nodes.Add(node);
                locations[node.Id] = new Location(root.Path, entry.Path);
                edges.Add(new ExplorerEdge(focus.Id, node.Id));
            }
            total += snapshot.TotalChildCount ?? children.Count;
            hidden += Math.Max(0, (snapshot.TotalChildCount ?? children.Count) - visible.Length);
            truncated |= snapshot.WasTruncated;
        }
        var warning = warnings.Count == 0 ? null : string.Join(" · ", warnings.Distinct(StringComparer.Ordinal).Take(3));
        if (truncated) { warning = (warning is null ? string.Empty : warning + " · ") + "Only the first 5,000 entries are loaded; use search to locate other items."; }
        var scene = nodes.Count == 0 ? null : new ExplorerNeighborhood(nodes[0].Id, nodes, edges, total, hidden, warning, truncated);
        return new SceneResult(scene, clusterRoots, locations, pages);
    }

    private SceneResult BuildFocusedScene(string root, string path, ExplorerDirectorySnapshot snapshot, int pageOffset, string? selectedPath)
    {
        var children = snapshot.Children;
        var offset = Math.Clamp(pageOffset / _focusedPageSize * _focusedPageSize, 0, Math.Max(0, (children.Count - 1) / _focusedPageSize * _focusedPageSize));
        if (selectedPath is not null)
        {
            var selectedIndex = children.ToList().FindIndex(child => PathBoundary.Comparer.Equals(child.Path, selectedPath));
            if (selectedIndex >= 0) { offset = selectedIndex / _focusedPageSize * _focusedPageSize; }
        }
        var focus = Node(root, snapshot.Focus);
        var nodes = new List<ExplorerNode> { focus };
        var edges = new List<ExplorerEdge>();
        var locations = new Dictionary<string, Location>(PathBoundary.Comparer) { [focus.Id] = new(root, path) };
        var pages = new Dictionary<string, int>(PathBoundary.Comparer);
        var visible = children.Skip(offset).Take(_focusedPageSize).ToArray();
        foreach (var entry in visible)
        {
            var node = Node(root, entry);
            nodes.Add(node);
            locations[node.Id] = new Location(root, entry.Path);
            edges.Add(new ExplorerEdge(focus.Id, node.Id));
        }
        if (offset > 0) { AddPageNode(root, path, focus.Id, "Previous items", offset - _focusedPageSize, offset, nodes, edges, locations, pages); }
        if (offset + _focusedPageSize < children.Count) { AddPageNode(root, path, focus.Id, "Next items", offset + _focusedPageSize, children.Count - offset - _focusedPageSize, nodes, edges, locations, pages); }
        var warning = snapshot.Warning;
        if (snapshot.WasTruncated) { warning = (warning is null ? string.Empty : warning + " · ") + "Only the first 5,000 entries are loaded; use search to locate other items."; }
        var total = snapshot.TotalChildCount ?? children.Count;
        var scene = new ExplorerNeighborhood(focus.Id, nodes, edges, total, Math.Max(0, total - visible.Length), warning, snapshot.WasTruncated, new AggregatePage(offset, _focusedPageSize));
        return new SceneResult(scene, [focus.Id], locations, pages, snapshot);
    }

    private async Task<ExplorerDirectorySnapshot> ReadAsync(string root, string path, CancellationToken token)
    {
        var fallback = new ExplorerEntry(Key(root, path), NameOf(path), path, ExplorerNodeKind.Folder, IsNavigable: false);
        if (!await Task.Run(() => FileMapPathGuard.IsOrdinaryPath(root, path, requireDirectory: true), token))
        {
            return new ExplorerDirectorySnapshot(fallback, [], ExplorerFailureKind.AccessDenied, $"{NameOf(path)} is unavailable. Its saved location is retained.");
        }
        try
        {
            var snapshot = await ProviderFor(root).GetDirectoryAsync(path, token);
            token.ThrowIfCancellationRequested();
            if (!await Task.Run(() => FileMapPathGuard.IsOrdinaryPath(root, path, requireDirectory: true), token))
            {
                return new ExplorerDirectorySnapshot(fallback, [], ExplorerFailureKind.AccessDenied, "The folder changed while loading.");
            }
            var children = snapshot.Children.Take(5000).Where(child =>
                child.Kind is ExplorerNodeKind.Folder or ExplorerNodeKind.File &&
                FileMapWorkspace.TryNormalizePath(child.Path, out var target) && FileMapWorkspace.IsWithin(root, target) &&
                PathBoundary.Comparer.Equals(Path.GetDirectoryName(target), path))
                .Select(child => child with { Path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(child.Path)), NavigationTarget = null, ParentNavigationTarget = null })
                .DistinctBy(child => child.Path, PathBoundary.Comparer)
                .OrderBy(child => child.Kind == ExplorerNodeKind.Folder ? 0 : 1).ThenBy(child => child.Name, PathBoundary.Comparer).ToArray();
            return snapshot with
            {
                Focus = fallback with { IsNavigable = snapshot.Failure == ExplorerFailureKind.None },
                Children = children,
                WasTruncated = snapshot.WasTruncated || snapshot.Children.Count > 5000,
            };
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return new ExplorerDirectorySnapshot(fallback, [], ExplorerFailureKind.EnumerationFailed, $"{NameOf(path)} could not be read. Its saved location is retained.");
        }
    }

    private SceneResult LimitOverview(SceneResult source)
    {
        if (source.Scene is not { } scene || _overviewChildLimit == 6) { return source; }
        var nodes = new List<ExplorerNode>();
        var edges = new List<ExplorerEdge>();
        var locations = new Dictionary<string, Location>(source.Locations, PathBoundary.Comparer);
        var pages = new Dictionary<string, int>(source.Pages, PathBoundary.Comparer);
        foreach (var rootId in source.ClusterRoots)
        {
            var root = scene.Nodes.First(node => node.Id == rootId);
            nodes.Add(root);
            if (_overviewChildLimit == 0) { continue; }
            var childIds = scene.Edges.Where(edge => edge.SourceId == rootId).Select(edge => edge.TargetId).ToHashSet(PathBoundary.Comparer);
            var children = scene.Nodes.Where(node => childIds.Contains(node.Id) && node.Kind != ExplorerNodeKind.Aggregate).ToArray();
            foreach (var child in children.Take(_overviewChildLimit)) { nodes.Add(child); edges.Add(new ExplorerEdge(rootId, child.Id)); }
        }
        return source with
        {
            Scene = scene with { Nodes = nodes, Edges = edges, HiddenChildCount = Math.Max(0, scene.TotalChildCount - nodes.Count + source.ClusterRoots.Count) },
            Locations = locations,
            Pages = pages,
        };
    }

    private IExplorerProvider ProviderFor(string root)
    {
        var provider = _providerFactory(root);
        if (!FileMapWorkspace.TryNormalizePath(provider.AccessRoot, out var authority) || !PathBoundary.Comparer.Equals(authority, root))
        {
            throw new InvalidOperationException("A workspace requires a standalone provider with the exact selected root.");
        }
        return provider;
    }

    private static ExplorerNode Node(string root, ExplorerEntry entry) => ExplorerNode.FromEntry(entry with { Id = Key(root, entry.Path), NavigationTarget = null, ParentNavigationTarget = null });

    private static void AddPageNode(string root, string path, string focusId, string label, int offset, int count,
        List<ExplorerNode> nodes, List<ExplorerEdge> edges, Dictionary<string, Location> locations, Dictionary<string, int> pages)
    {
        var id = focusId + "|page:" + offset;
        nodes.Add(new ExplorerNode(id, label, path, ExplorerNodeKind.Aggregate, null, null, true, count, new AggregateAction(AggregateActionKind.OpenPage, offset), Roles: ExplorerNodeRole.Structural));
        edges.Add(new ExplorerEdge(focusId, id));
        locations[id] = new Location(root, path);
        pages[id] = offset;
    }

    private string? FindVisiblePreviewNode(FileMapSearchHit hit) =>
        _baseScene.Scene?.Nodes
                .Where(node => node.Kind is ExplorerNodeKind.Folder or ExplorerNodeKind.File &&
                    _baseScene.Locations.TryGetValue(node.Id, out var location) &&
                    PathBoundary.Comparer.Equals(location.Root, hit.RootPath) &&
                    FileMapWorkspace.IsWithin(node.Path, hit.Path))
                .OrderByDescending(node => node.Path.Length).Select(node => node.Id).FirstOrDefault();

    private bool IsSearchHitAuthorized(FileMapSearchHit hit) => State.Roots.Any(root => PathBoundary.Comparer.Equals(root.Path, hit.RootPath)) &&
        FileMapWorkspace.TryNormalizePath(hit.Path, out var path) && FileMapWorkspace.IsWithin(hit.RootPath, path) &&
        hit.Kind is ExplorerNodeKind.Folder or ExplorerNodeKind.File;

    private bool IsAuthorizedRoot(string root) => State.Roots.Any(item => PathBoundary.Comparer.Equals(item.Path, root)) ||
        State.Pins.Any(pin => PathBoundary.Comparer.Equals(pin.Path, root));

    private void ClearPreview()
    {
        Cancel(ref _previewCancellation);
        _previewVersion++;
        _previewHit = null;
        PreviewNodeId = null;
    }

    private void RecountMatches()
    {
        var counts = new Dictionary<string, int>(PathBoundary.Comparer);
        foreach (var node in Scene?.Nodes ?? [])
        {
            if (!_baseScene.Locations.TryGetValue(node.Id, out var location) || node.Kind == ExplorerNodeKind.Aggregate) { continue; }
            var count = SearchResults.Count(hit => PathBoundary.Comparer.Equals(hit.RootPath, location.Root) && FileMapWorkspace.IsWithin(node.Path, hit.Path));
            if (count > 0) { counts[node.Id] = count; }
        }
        MatchCounts = counts;
    }

    private void InvalidateWorkspace()
    {
        _workspaceVersion++;
        _navigationVersion++;
        _searchVersion++;
        Cancel(ref _navigationCancellation);
        Cancel(ref _searchCancellation);
        ClearPreview();
        IsBusy = false;
        IsSearching = false;
        SearchResults = [];
        MatchCounts = new Dictionary<string, int>(PathBoundary.Comparer);
    }

    private void Save() => _saveFailed = !_store.TrySave(State);
    private void SetStatus(string message) { Status = message; Notify(); }
    private void Notify()
    {
        const string suffix = " · Changes could not be saved; they last only for this visit.";
        if (Status.EndsWith(suffix, StringComparison.Ordinal)) { Status = Status[..^suffix.Length]; }
        if (_saveFailed) { Status += suffix; }
        Changed?.Invoke();
    }
    private static string NameOf(string path) => Path.GetFileName(path) is { Length: > 0 } name ? name : path;
    private static bool IsRecoverable(Exception exception) => exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException or InvalidOperationException;
    private static void Cancel(ref CancellationTokenSource? source) { source?.Cancel(); source?.Dispose(); source = null; }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        InvalidateWorkspace();
        Changed = null;
    }

    private sealed record Location(string Root, string Path);
    private sealed record SceneResult(ExplorerNeighborhood? Scene, IReadOnlyList<string> ClusterRoots,
        Dictionary<string, Location> Locations, Dictionary<string, int> Pages, ExplorerDirectorySnapshot? FocusedSnapshot = null)
    {
        public static SceneResult Empty => new(null, [], new Dictionary<string, Location>(PathBoundary.Comparer), new Dictionary<string, int>(PathBoundary.Comparer));
    }
}
