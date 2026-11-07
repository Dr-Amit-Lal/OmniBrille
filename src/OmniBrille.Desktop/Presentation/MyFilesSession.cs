using OmniBrille.Core;
using OmniBrille.Infrastructure;

namespace OmniBrille.Desktop.Presentation;

/// <summary>Owns only the saved standalone file map. Connected explorer state never enters this session.</summary>
public sealed class MyFilesSession : IDisposable
{
    public const int PageSize = 12;
    public const int MaximumVisibleNodes = 48;
    public const int MaximumDepth = 16;
    private readonly IFileMapWorkspaceStore _store;
    private readonly IFileActivationService _activation;
    private readonly Func<string, IExplorerProvider> _providerFactory;
    private readonly Dictionary<string, ExplorerDirectorySnapshot> _snapshots = new(PathBoundary.Comparer);
    private readonly Dictionary<string, int> _pages = new(PathBoundary.Comparer);
    private readonly Dictionary<string, string> _revealOrder = new(PathBoundary.Comparer);
    private CancellationTokenSource? _work;
    private int _generation;
    private string? _selectedKey;
    private bool _disposed;
    private bool _saveFailed;

    public MyFilesSession(
        IFileMapWorkspaceStore? store = null,
        IFileActivationService? activation = null,
        Func<string, IExplorerProvider>? providerFactory = null)
    {
        _store = store ?? new JsonFileMapWorkspaceStore();
        _activation = activation ?? new ShellFileActivationService();
        _providerFactory = providerFactory ?? (path => new FileSystemExplorerProvider(path));
    }

    public event Action? Changed;

    public FileMapWorkspace State { get; private set; } = FileMapWorkspace.Empty;

    public IReadOnlyList<FileMapNode> VisibleNodes { get; private set; } = [];

    public FileMapNode? SelectedNode => VisibleNodes.FirstOrDefault(node => PathBoundary.Comparer.Equals(node.Key, _selectedKey));

    public IReadOnlyList<FileMapSearchHit> SearchResults { get; private set; } = [];

    public string Status { get; private set; } = "Add Documents or Desktop to begin. Your files stay in place.";

    public bool IsBusy { get; private set; }

    public async Task InitializeAsync(string? startupRoot = null)
    {
        _snapshots.Clear();
        _pages.Clear();
        _revealOrder.Clear();
        State = _store.Load().Normalize();
        foreach (var page in State.Pages ?? [])
        {
            _pages[Key(page.RootPath, page.Path)] = page.PageIndex;
        }

        Rebuild();
        var restoreGeneration = _generation + 1;
        await RunAsync("Opening your saved map…", async (generation, token) =>
        {
            foreach (var location in State.ExpandedFolders.OrderBy(item => item.Path.Length).ToArray())
            {
                var snapshot = await ReadAsync(location.RootPath, location.Path, token);
                if (!Current(generation))
                {
                    return;
                }

                if (snapshot is not null)
                {
                    _snapshots[Key(location.RootPath, location.Path)] = snapshot;
                }
            }

            Status = State.Roots.Count == 0
                ? "Add Documents or Desktop to begin. Your files stay in place."
                : _snapshots.Count < State.ExpandedFolders.Count
                    ? "Your map is open. Some saved folders are unavailable or have changed location."
                    : "Your map is ready. Select a file, then choose Open.";
        });
        if (!string.IsNullOrWhiteSpace(startupRoot) && Current(restoreGeneration))
        {
            await AddRootAsync(startupRoot);
        }
    }

    public Task AddRootAsync(string path, string? label = null) => RunAsync("Adding folder…", async (generation, token) =>
    {
        if (!FileMapWorkspace.TryNormalizePath(path, out var normalized) ||
            !await Task.Run(() => FileMapPathGuard.IsOrdinaryPath(normalized, normalized, true), token))
        {
            if (Current(generation))
            {
                Status = "This folder is unavailable or is a linked folder. Choose an ordinary folder.";
            }

            return;
        }

        if (!Current(generation))
        {
            return;
        }

        var existing = State.Roots.Any(root => PathBoundary.Comparer.Equals(root.Path, normalized));
        if (!existing && State.Roots.Count == FileMapWorkspace.MaximumRoots)
        {
            Status = $"Keep up to {FileMapWorkspace.MaximumRoots} home folders. Remove one from the map before adding another.";
            return;
        }

        if (!existing)
        {
            State = (State with { Roots = [.. State.Roots, new FileMapRoot(normalized, label ?? Path.GetFileName(normalized))] }).Normalize();
        }

        SearchResults = [];
        var snapshot = await ReadAsync(normalized, normalized, token);
        if (!Current(generation))
        {
            return;
        }

        if (snapshot is not null)
        {
            _snapshots[Key(normalized, normalized)] = snapshot;
            SetExpanded(normalized, normalized, true);
            Status = SnapshotStatus(snapshot, "Folder added to your map.");
        }
        else
        {
            Status = "The folder could not be read. Refresh when it is available.";
        }

        Save();
    });

    public void RemoveRoot(string rootPath)
    {
        CancelWork();
        State = (State with
        {
            Roots = State.Roots.Where(root => !PathBoundary.Comparer.Equals(root.Path, rootPath)).ToArray(),
            ExpandedFolders = State.ExpandedFolders.Where(item => !PathBoundary.Comparer.Equals(item.RootPath, rootPath)).ToArray(),
            Pins = State.Pins.Where(item => !PathBoundary.Comparer.Equals(item.RootPath, rootPath)).ToArray(),
        }).Normalize();
        var rootPrefix = Key(rootPath, string.Empty);
        foreach (var key in _snapshots.Keys.Where(key => key.StartsWith(rootPrefix, PathBoundary.Comparison)).ToArray())
        {
            _snapshots.Remove(key);
            _pages.Remove(key);
            _revealOrder.Remove(key);
        }
        _selectedKey = null;
        SearchResults = [];
        Status = "Folder removed from the map. Files remain in their original location.";
        Save();
        Publish();
    }

    public Task ToggleFolderAsync(string key)
    {
        var node = Find(key);
        if (node is null || node.Entry.Kind != ExplorerNodeKind.Folder)
        {
            return Task.CompletedTask;
        }

        if (node.Expanded)
        {
            CancelWork();
            Collapse(node.RootPath, node.Entry.Path);
            Status = $"Collapsed {node.Entry.Name}.";
            Save();
            Publish();
            return Task.CompletedTask;
        }

        if (!node.Entry.IsNavigable || node.Entry.IsReparsePoint || node.Depth >= MaximumDepth)
        {
            Status = "This folder cannot be expanded in the map.";
            Publish();
            return Task.CompletedTask;
        }

        return RunAsync($"Opening {node.Entry.Name}…", async (generation, token) =>
        {
            if (State.ExpandedFolders.Count >= FileMapWorkspace.MaximumExpandedFolders)
            {
                Status = "Collapse a branch before opening another.";
                return;
            }

            var snapshot = await ReadAsync(node.RootPath, node.Entry.Path, token);
            if (!Current(generation))
            {
                return;
            }

            if (snapshot is null)
            {
                Status = "This folder is unavailable or has become a linked folder. Refresh the map.";
                return;
            }

            _snapshots[key] = snapshot;
            SetExpanded(node.RootPath, node.Entry.Path, true);
            Status = SnapshotStatus(snapshot, snapshot.Children.Count == 0 ? "This folder is empty." : $"Expanded {node.Entry.Name}.");
            Save();
        });
    }

    public void Select(string key)
    {
        CancelWork();
        _selectedKey = Find(key)?.Key;
        Status = SelectedNode is { } selected
            ? $"Selected {selected.Entry.Name}." + (selected.Entry.Kind == ExplorerNodeKind.File ? " Choose Open to open it." : string.Empty)
            : "Select an item in the map.";
        Publish();
    }

    public void PinSelected()
    {
        var node = SelectedNode;
        if (node is null || node.Entry.Kind != ExplorerNodeKind.File)
        {
            Status = "Select a file to add to Current Work.";
        }
        else if (State.Pins.Any(pin => PathBoundary.Comparer.Equals(pin.Path, node.Entry.Path)))
        {
            Status = "This file is already in Current Work.";
        }
        else if (State.Pins.Count >= FileMapWorkspace.MaximumPins)
        {
            Status = $"Current Work holds up to {FileMapWorkspace.MaximumPins} files. Unpin one to add another.";
        }
        else
        {
            State = State with { Pins = [.. State.Pins, new FileMapPin(node.RootPath, node.Entry.Path, node.Entry.Name)] };
            Status = "Added to Current Work.";
            Save();
        }

        Publish();
    }

    public void Unpin(string path)
    {
        State = State with { Pins = State.Pins.Where(pin => !PathBoundary.Comparer.Equals(pin.Path, path)).ToArray() };
        Status = "Removed from Current Work. The file remains in place.";
        Save();
        Publish();
    }

    public Task OpenSelectedAsync()
    {
        var node = SelectedNode;
        if (node is null || node.Entry.Kind != ExplorerNodeKind.File)
        {
            Status = "Select a file, then choose Open.";
            Publish();
            return Task.CompletedTask;
        }

        return RunAsync("Opening file…", async (generation, token) =>
        {
            var ordinary = await Task.Run(() => FileMapPathGuard.IsOrdinaryPath(node.RootPath, node.Entry.Path, false), token);
            if (!Current(generation))
            {
                return;
            }

            if (!ordinary)
            {
                Status = "This file is unavailable or its location has changed. Refresh the map.";
                return;
            }

            var result = await _activation.OpenAsync(node.RootPath, node.Entry.Path, token);
            if (Current(generation))
            {
                Status = result switch
                {
                    FileActivationResult.Opened => $"Opened {node.Entry.Name} in its usual app.",
                    FileActivationResult.HighRiskTypeBlocked => "Programs, scripts, installers, and shortcuts must be opened in Windows File Explorer.",
                    _ => "The file could not be opened. Refresh the map and try again.",
                };
            }
        });
    }

    public Task RefreshAsync() => RunAsync("Refreshing your map…", async (generation, token) =>
    {
        var fresh = new Dictionary<string, ExplorerDirectorySnapshot>(PathBoundary.Comparer);
        foreach (var location in State.ExpandedFolders.ToArray())
        {
            var snapshot = await ReadAsync(location.RootPath, location.Path, token);
            if (!Current(generation))
            {
                return;
            }

            if (snapshot is not null)
            {
                fresh[Key(location.RootPath, location.Path)] = snapshot;
            }
        }

        _snapshots.Clear();
        foreach (var item in fresh)
        {
            _snapshots[item.Key] = item.Value;
        }

        SearchResults = [];
        Status = fresh.Count < State.ExpandedFolders.Count
            ? "Map refreshed. Some folders are unavailable or have changed location."
            : "Map refreshed.";
    });

    public void Home()
    {
        CancelWork();
        State = State with { ExpandedFolders = [] };
        _snapshots.Clear();
        _pages.Clear();
        _revealOrder.Clear();
        _selectedKey = null;
        SearchResults = [];
        Status = "Home. Expand a folder to see its files.";
        Save();
        Publish();
    }

    public Task RestoreBranchesAsync(IReadOnlyList<FileMapLocation> branches)
    {
        ArgumentNullException.ThrowIfNull(branches);
        return RunAsync("Returning to your previous branches…", async (generation, token) =>
        {
            var desired = (State with { ExpandedFolders = branches }).Normalize();
            var fresh = new Dictionary<string, ExplorerDirectorySnapshot>(PathBoundary.Comparer);
            foreach (var location in desired.ExpandedFolders)
            {
                var snapshot = await ReadAsync(location.RootPath, location.Path, token);
                if (!Current(generation))
                {
                    return;
                }

                if (snapshot is not null)
                {
                    fresh[Key(location.RootPath, location.Path)] = snapshot;
                }
            }

            State = State with { ExpandedFolders = desired.ExpandedFolders };
            _snapshots.Clear();
            _revealOrder.Clear();
            foreach (var item in fresh)
            {
                _snapshots[item.Key] = item.Value;
            }

            foreach (var location in desired.ExpandedFolders)
            {
                var parent = Path.GetDirectoryName(location.Path);
                if (parent is not null && fresh.TryGetValue(Key(location.RootPath, parent), out var parentSnapshot))
                {
                    var position = parentSnapshot.Children.ToList().FindIndex(entry => PathBoundary.Comparer.Equals(entry.Path, location.Path));
                    if (position >= 0)
                    {
                        _pages[Key(location.RootPath, parent)] = position / PageSize;
                    }
                }
            }

            _selectedKey = null;
            Status = "Previous branches restored.";
            Save();
        });
    }

    public Task ChangePageAsync(string key, int delta)
    {
        var node = Find(key);
        if (node is null || !node.Expanded || delta == 0)
        {
            return Task.CompletedTask;
        }

        CancelWork();
        _pages[key] = Math.Clamp(node.PageIndex + Math.Sign(delta), 0, Math.Max(0, node.PageCount - 1));
        _revealOrder.Remove(key);
        _selectedKey = null;
        Status = $"Showing page {_pages[key] + 1} of {node.PageCount} in {node.Entry.Name}.";
        Save();
        Publish();
        return Task.CompletedTask;
    }

    public Task SearchAsync(string query) => RunAsync("Searching your chosen folders…", async (generation, token) =>
    {
        SearchResults = [];
        var search = query.Trim();
        if (search.Length == 0)
        {
            Status = "Enter part of a filename to search your chosen folders.";
            return;
        }

        if (search.Length > 200 || search.Any(char.IsControl))
        {
            Status = "Use a filename search of up to 200 characters.";
            return;
        }

        var hits = new List<FileMapSearchHit>();
        var incomplete = false;
        var roots = State.Roots.ToArray();
        var directoryBudget = Math.Max(1, 500 / Math.Max(1, roots.Length));
        foreach (var root in roots)
        {
            var ordinary = await Task.Run(() => FileMapPathGuard.IsOrdinaryPath(root.Path, root.Path, true), token);
            if (!Current(generation))
            {
                return;
            }

            if (!ordinary)
            {
                incomplete = true;
                continue;
            }

            if (!Current(generation))
            {
                return;
            }

            if (_providerFactory(root.Path) is not IExplorerSearchProvider provider)
            {
                incomplete = true;
                continue;
            }

            var result = await provider.SearchAsync(new SearchRequest(root.Path, search, 80 - hits.Count, directoryBudget), token);
            if (!Current(generation))
            {
                return;
            }

            hits.AddRange(result.Hits.Take(80 - hits.Count).Where(hit =>
                FileMapWorkspace.TryNormalizePath(hit.Path, out var path) && FileMapWorkspace.IsWithin(root.Path, path))
                .Select(hit => new FileMapSearchHit(root.Path, hit)));
            incomplete |= result.WasTruncated || result.Warning is not null;
            if (hits.Count >= 80)
            {
                incomplete = true;
                break;
            }
        }

        SearchResults = hits;
        Status = $"{hits.Count} matching item(s) in your chosen folders." +
                 (incomplete ? " Search reached a limit or an unavailable folder; more matches may exist." : string.Empty);
    });

    public Task RevealAsync(string rootPath, string path) => RunAsync("Showing the file's location…", async (generation, token) =>
    {
        if (!FileMapWorkspace.TryNormalizePath(rootPath, out var root) ||
            !FileMapWorkspace.TryNormalizePath(path, out var target) ||
            !State.Roots.Any(item => PathBoundary.Comparer.Equals(item.Path, root)) ||
            !await Task.Run(() => FileMapPathGuard.IsOrdinaryPath(root, target, false), token))
        {
            if (Current(generation))
            {
                Status = "This location is unavailable or is outside your chosen folders.";
            }

            return;
        }

        var isDirectory = await Task.Run(() => Directory.Exists(target), token);
        if (!Current(generation))
        {
            return;
        }

        var current = isDirectory ? target : Path.GetDirectoryName(target);
        var chain = new List<string>();
        while (current is not null && FileMapWorkspace.IsWithin(root, current))
        {
            chain.Add(current);
            if (PathBoundary.Comparer.Equals(current, root))
            {
                break;
            }

            if (chain.Count >= MaximumDepth)
            {
                Status = "This location is too deep for one map. Add its parent folder as a home folder.";
                return;
            }

            current = Path.GetDirectoryName(current);
        }

        chain.Reverse();
        var fresh = new Dictionary<string, ExplorerDirectorySnapshot>(PathBoundary.Comparer);
        foreach (var folder in chain)
        {
            var snapshot = await ReadAsync(root, folder, token);
            if (!Current(generation))
            {
                return;
            }

            if (snapshot is null || snapshot.Failure != ExplorerFailureKind.None)
            {
                Status = "The location could not be shown. Refresh and try again.";
                return;
            }

            fresh[Key(root, folder)] = snapshot;
        }

        var nextPages = new Dictionary<string, int>(PathBoundary.Comparer);
        var nextOrder = new Dictionary<string, string>(PathBoundary.Comparer);
        for (var index = 0; index < chain.Count; index++)
        {
            var folder = chain[index];
            var child = index + 1 < chain.Count ? chain[index + 1] : target;
            if (PathBoundary.Comparer.Equals(folder, child))
            {
                continue;
            }

            var children = fresh[Key(root, folder)].Children;
            var position = children.ToList().FindIndex(entry => PathBoundary.Comparer.Equals(entry.Path, child));
            if (position < 0)
            {
                Status = "This item is beyond the folder's reading limit or has moved. Add its parent as a home folder.";
                return;
            }

            nextPages[Key(root, folder)] = position / PageSize;
            nextOrder[Key(root, folder)] = child;
        }

        if (!Current(generation))
        {
            return;
        }

        State = State with { ExpandedFolders = chain.Select(folder => new FileMapLocation(root, folder)).ToArray() };
        _snapshots.Clear();
        _pages.Clear();
        _revealOrder.Clear();
        foreach (var item in fresh)
        {
            _snapshots[item.Key] = item.Value;
        }

        foreach (var item in nextPages)
        {
            _pages[item.Key] = item.Value;
        }

        foreach (var item in nextOrder)
        {
            _revealOrder[item.Key] = item.Value;
        }

        _selectedKey = Key(root, target);
        Status = "Location shown in the map. Select Open to open the file.";
        Save();
    });

    public void SetTextSize(double size)
    {
        State = (State with { TextSize = size }).Normalize();
        Save();
        Publish();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CancelWork();
        _disposed = true;
        _work?.Dispose();
        _work = null;
        Changed = null;
    }

    private async Task RunAsync(string message, Func<int, CancellationToken, Task> operation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelWork();
        var source = new CancellationTokenSource();
        _work = source;
        var generation = _generation;
        IsBusy = true;
        Status = message;
        Publish();
        try
        {
            await operation(generation, source.Token);
        }
        catch (OperationCanceledException)
        {
            if (Current(generation))
            {
                Status = "Operation cancelled.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            if (Current(generation))
            {
                Status = "The folder could not be read. Refresh or choose another folder.";
            }
        }
        finally
        {
            if (Current(generation))
            {
                IsBusy = false;
                _work = null;
                Publish();
            }

            source.Dispose();
        }
    }

    private async Task<ExplorerDirectorySnapshot?> ReadAsync(string root, string path, CancellationToken token)
    {
        if (!State.Roots.Any(item => PathBoundary.Comparer.Equals(item.Path, root)) ||
            !await Task.Run(() => FileMapPathGuard.IsOrdinaryPath(root, path, true), token))
        {
            return null;
        }

        var snapshot = await _providerFactory(root).GetDirectoryAsync(path, token);
        if (!await Task.Run(() => FileMapPathGuard.IsOrdinaryPath(root, path, true), token))
        {
            return null;
        }

        var children = snapshot.Children.Where(entry =>
                FileMapWorkspace.TryNormalizePath(entry.Path, out var child) && FileMapWorkspace.IsWithin(root, child) &&
                PathBoundary.Comparer.Equals(Path.GetDirectoryName(child), path))
            .OrderBy(entry => entry.Kind == ExplorerNodeKind.Folder ? 0 : 1)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Path, StringComparer.Ordinal)
            .Take(FileSystemExplorerProvider.DefaultEnumerationLimit)
            .ToArray();
        return snapshot with { Children = children };
    }

    private void Rebuild()
    {
        var nodes = new List<FileMapNode>(MaximumVisibleNodes);
        var expanded = State.ExpandedFolders.Select(item => Key(item.RootPath, item.Path)).ToHashSet(PathBoundary.Comparer);
        foreach (var key in _pages.Keys.Where(key => !expanded.Contains(key)).ToArray())
        {
            _pages.Remove(key);
        }

        void Add(ExplorerEntry entry, string root, string? parent, int depth, int reserved)
        {
            if (nodes.Count >= MaximumVisibleNodes - reserved || depth > MaximumDepth)
            {
                return;
            }

            var key = Key(root, entry.Path);
            _snapshots.TryGetValue(key, out var snapshot);
            var isExpanded = expanded.Contains(key);
            var count = snapshot?.Children.Count ?? 0;
            var pageCount = Math.Max(1, (count + PageSize - 1) / PageSize);
            var page = Math.Clamp(_pages.GetValueOrDefault(key), 0, pageCount - 1);
            if (isExpanded && snapshot is not null)
            {
                _pages[key] = page;
            }
            var nodeIndex = nodes.Count;
            nodes.Add(new FileMapNode(key, parent, root, entry, depth, isExpanded, page, pageCount,
                snapshot?.TotalChildCount ?? count, snapshot?.WasTruncated ?? false));
            if (!isExpanded || snapshot is null)
            {
                return;
            }

            var pageChildren = snapshot.Children.Skip(page * PageSize).Take(PageSize).ToArray();
            if (_revealOrder.TryGetValue(key, out var first))
            {
                pageChildren = pageChildren.OrderBy(child => PathBoundary.Comparer.Equals(child.Path, first) ? 0 : 1).ToArray();
            }

            var added = 0;
            foreach (var child in pageChildren)
            {
                if (nodes.Count >= MaximumVisibleNodes - reserved)
                {
                    break;
                }

                Add(child, root, key, depth + 1, reserved);
                added++;
            }

            if (added < pageChildren.Length)
            {
                nodes[nodeIndex] = nodes[nodeIndex] with { Truncated = true };
            }
        }

        for (var index = 0; index < State.Roots.Count; index++)
        {
            var root = State.Roots[index];
            var entry = new ExplorerEntry(root.Path, root.Label, root.Path, ExplorerNodeKind.Folder);
            Add(entry, root.Path, null, 0, State.Roots.Count - index - 1);
        }

        VisibleNodes = nodes;
    }

    private FileMapNode? Find(string key) => VisibleNodes.FirstOrDefault(node => PathBoundary.Comparer.Equals(node.Key, key));

    private static string Key(string root, string path) => root.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + root + path;

    private bool Current(int generation) => !_disposed && generation == _generation;

    private void CancelWork()
    {
        _generation++;
        _work?.Cancel();
        _work = null;
        IsBusy = false;
    }

    private void SetExpanded(string root, string path, bool value)
    {
        var items = State.ExpandedFolders.Where(item =>
            !PathBoundary.Comparer.Equals(item.RootPath, root) || !PathBoundary.Comparer.Equals(item.Path, path)).ToList();
        if (value)
        {
            items.Add(new FileMapLocation(root, path));
        }

        State = (State with { ExpandedFolders = items }).Normalize();
    }

    private void Collapse(string root, string path)
    {
        State = State with
        {
            ExpandedFolders = State.ExpandedFolders.Where(item =>
                !PathBoundary.Comparer.Equals(item.RootPath, root) || !FileMapWorkspace.IsWithin(path, item.Path)).ToArray(),
        };
        var rootPrefix = Key(root, string.Empty);
        foreach (var key in _snapshots.Keys.Where(key =>
                     key.StartsWith(rootPrefix, PathBoundary.Comparison) &&
                     FileMapWorkspace.IsWithin(path, _snapshots[key].Focus.Path)).ToArray())
        {
            _snapshots.Remove(key);
            _pages.Remove(key);
            _revealOrder.Remove(key);
        }
    }

    private void Save()
    {
        State = State with
        {
            Pages = State.ExpandedFolders.Select(item =>
                new FileMapPage(item.RootPath, item.Path, _pages.GetValueOrDefault(Key(item.RootPath, item.Path)))).ToArray(),
        };
        _saveFailed = !_store.TrySave(State);
    }

    private void Publish()
    {
        Rebuild();
        if (_saveFailed && !Status.Contains("could not be saved", StringComparison.Ordinal))
        {
            Status += " Your map could not be saved; changes will last for this visit.";
        }

        if (VisibleNodes.Count >= MaximumVisibleNodes && !Status.Contains("48", StringComparison.Ordinal))
        {
            Status += " The map shows up to 48 items. Collapse a branch to make room.";
        }

        Changed?.Invoke();
    }

    private static string SnapshotStatus(ExplorerDirectorySnapshot snapshot, string ready) =>
        snapshot.Failure != ExplorerFailureKind.None ? "This folder could not be read completely. Refresh to try again." :
        snapshot.WasTruncated ? "This folder reached the 5,000-item reading limit. Some items are not shown; add a narrower folder." :
        snapshot.Warning is not null ? "Some items could not be read. Available items are shown." : ready;
}
