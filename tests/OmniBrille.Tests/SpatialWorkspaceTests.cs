using OmniBrille.Core;
using OmniBrille.Desktop.Presentation;
using OmniBrille.Infrastructure;

namespace OmniBrille.Tests;

public sealed class SpatialWorkspaceTests
{
    [Fact]
    public void Normalize_RetainsIndependentPinsAndUnavailableRootsWithoutProbing()
    {
        using var files = new WorkspaceFiles();
        var missing = Path.Combine(files.Path, "missing");
        var pin = Path.Combine(files.Path, "separate");
        var normalized = new SpatialWorkspace([new(missing, "M\nissing"), new(missing, "Duplicate"), new("relative", "Bad")],
            [new(pin, "Separate")], pin, Path.Combine(pin, "child")).Normalize();
        Assert.Equal(new FileMapRoot(missing, "Missing"), Assert.Single(normalized.Roots));
        Assert.Equal(pin, Assert.Single(normalized.Pins).Path);
        Assert.Equal(pin, normalized.CurrentRoot);
        Assert.Equal(Path.Combine(pin, "child"), normalized.CurrentPath);
        Assert.Null((normalized with { Pins = [] }).Normalize().CurrentRoot);
        Assert.Empty((normalized with { SchemaVersion = 2 }).Normalize().Roots);
    }

    [Fact]
    public void Store_MigratesExplicitRootsAndPrioritizesNewestPinsWithoutPromotingThem()
    {
        using var files = new WorkspaceFiles();
        var root = Path.Combine(files.Path, "Root");
        var oldPin = Path.Combine(root, "Legacy");
        var rootsStore = new JsonFileMapWorkspaceStore(files.Path);
        Assert.True(rootsStore.TrySave(new FileMapWorkspace([new(root, "Root")], [], [new(root, oldPin, "Legacy")])));
        var newestPins = Enumerable.Range(0, 12).Select(index => new PinnedFolder(Path.Combine(files.Path, $"Pin-{index}"), $"Pin {index}")).ToArray();
        Assert.True(new JsonPinnedFoldersStore(files.Path).TrySave(new PinnedFolders(newestPins)));
        var store = new JsonSpatialWorkspaceStore(files.Path);
        var migrated = store.Load();
        Assert.Equal(root, Assert.Single(migrated.Roots).Path);
        Assert.Equal(newestPins, migrated.Pins);
        Assert.True(store.TrySave(SpatialWorkspace.Empty));
        Assert.Empty(store.Load().Roots);
        Assert.Empty(store.Load().Pins);
        Assert.Equal(root, Assert.Single(rootsStore.Load().Roots).Path);
    }

    [Fact]
    public void Store_RoundTripsOrderAndNavigation_RejectsOversizedAndMalformedInput()
    {
        using var files = new WorkspaceFiles();
        var a = files.Directory("A");
        var b = files.Directory("B");
        var store = new JsonSpatialWorkspaceStore(files.Path);
        var state = new SpatialWorkspace([new(a, "A"), new(b, "B")], [new(b, "B"), new(a, "A")], a, a);
        Assert.True(store.TrySave(state));
        Assert.Equal(state.Roots, store.Load().Roots);
        Assert.Equal(state.Pins, store.Load().Pins);
        Assert.Equal(a, store.Load().CurrentPath);
        Assert.Empty(System.IO.Directory.GetFiles(files.Path, "*.tmp"));
        File.WriteAllText(Path.Combine(files.Path, JsonSpatialWorkspaceStore.FileName), "{invalid");
        Assert.Empty(store.Load().Roots);
        File.WriteAllText(Path.Combine(files.Path, JsonSpatialWorkspaceStore.FileName), new string(' ', JsonSpatialWorkspaceStore.MaximumFileBytes + 1));
        Assert.Empty(store.Load().Roots);
        var blocker = Path.Combine(files.Path, "blocker");
        File.WriteAllText(blocker, "original");
        Assert.False(new JsonSpatialWorkspaceStore(blocker).TrySave(state));
        Assert.Equal("original", File.ReadAllText(blocker));
    }

    [Theory]
    [InlineData("{\"SchemaVersion\":2,\"Roots\":[],\"Pins\":[]}")]
    [InlineData("not-json")]
    public async Task InitializationDoesNotOverwriteAnUnreadableOrFutureWorkspace(string existing)
    {
        using var files = new WorkspaceFiles();
        var path = Path.Combine(files.Path, JsonSpatialWorkspaceStore.FileName);
        File.WriteAllText(path, existing);
        using var session = new SpatialWorkspaceSession(new JsonSpatialWorkspaceStore(files.Path));
        await session.InitializeAsync();
        Assert.Equal(existing, File.ReadAllText(path));
    }

    [Fact]
    public async Task PinExcursionsNeverReplaceRoots_AndUseDeepestContainingRoot()
    {
        using var files = new WorkspaceFiles();
        var outer = files.Directory("Work");
        var inner = files.Directory("Work/Project");
        var target = files.Directory("Work/Project/Notes");
        var elsewhere = files.Directory("Personal");
        var store = new MemoryStore(new SpatialWorkspace([new(outer, "Work"), new(inner, "Project")], [new(target, "Notes"), new(elsewhere, "Personal")]));
        using var session = new SpatialWorkspaceSession(store);
        await session.InitializeAsync();
        await session.OpenPinAsync(target);
        Assert.Equal(inner, session.CurrentRoot);
        await session.GoUpAsync();
        Assert.Equal(inner, session.CurrentPath);
        await session.GoUpAsync();
        Assert.True(session.IsOverview);
        await session.OpenPinAsync(elsewhere);
        Assert.Equal(elsewhere, session.CurrentRoot);
        Assert.Equal(new[] { outer, inner }, session.State.Roots.Select(root => root.Path));
        await session.GoUpAsync();
        Assert.True(session.IsOverview);
        await session.OpenPinAsync(elsewhere);
        session.RemovePin(elsewhere);
        Assert.True(session.IsOverview);
        Assert.DoesNotContain(session.State.Pins, pin => pin.Path == elsewhere);
    }

    [Fact]
    public async Task RestoreKeepsUnavailableRoot_AndPinOrderAndExcursionAcrossRestart()
    {
        using var files = new WorkspaceFiles();
        var unavailable = Path.Combine(files.Path, "Offline");
        var first = files.Directory("First");
        var second = files.Directory("Second");
        var store = new JsonSpatialWorkspaceStore(files.Path);
        Assert.True(store.TrySave(new SpatialWorkspace([new(unavailable, "Offline")], [new(first, "First"), new(second, "Second")])));
        using (var session = new SpatialWorkspaceSession(store))
        {
            await session.InitializeAsync();
            session.MovePin(second, -1);
            await session.OpenPinAsync(second);
        }
        using var restored = new SpatialWorkspaceSession(store);
        await restored.InitializeAsync();
        Assert.Equal(unavailable, Assert.Single(restored.State.Roots).Path);
        Assert.Equal(new[] { second, first }, restored.State.Pins.Select(pin => pin.Path));
        Assert.Equal(second, restored.CurrentPath);
        await restored.ShowWorkspaceAsync();
        Assert.Contains("unavailable", restored.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(Assert.Single(restored.Scene!.Nodes).IsNavigable);
    }

    [Fact]
    public async Task SearchIncludesAllRoots_HoverMarksNearestVisibleAncestorWithoutChangingScene()
    {
        using var files = new WorkspaceFiles();
        var a = files.Directory("A");
        var b = files.Directory("B");
        files.Directory("B/Nested/Deeper");
        File.WriteAllText(Path.Combine(a, "needle-a.txt"), "a");
        var target = Path.Combine(b, "Nested", "Deeper", "needle-b.txt");
        File.WriteAllText(target, "b");
        var store = new MemoryStore(new SpatialWorkspace([new(a, "A"), new(b, "B")], []));
        using var session = new SpatialWorkspaceSession(store);
        await session.InitializeAsync();
        await session.SearchAsync("needle");
        Assert.Equal(2, session.SearchResults.Count);
        var hit = Assert.Single(session.SearchResults, hit => hit.RootPath == b);
        var writes = store.Writes;
        var before = session.Scene;
        session.Select(before!.Nodes[0].Id);
        var selection = session.SelectedNodeId;
        await session.PreviewSearchHitAsync(hit);
        Assert.True(session.IsOverview);
        Assert.Equal(writes, store.Writes);
        Assert.Equal(selection, session.SelectedNodeId);
        Assert.Equal(Path.Combine(b, "Nested"), session.NodeById(session.PreviewNodeId)!.Path);
        Assert.Same(before, session.Scene);
        foreach (var edge in session.Scene!.Edges)
        {
            var child = session.NodeById(edge.TargetId)!;
            var parent = session.NodeById(edge.SourceId)!;
            if (child.Kind != ExplorerNodeKind.Aggregate) { Assert.Equal(parent.Path, Path.GetDirectoryName(child.Path)); }
        }
        session.SetOverviewChildLimit(0);
        Assert.Equal(2, session.Scene!.Nodes.Count);
        Assert.Equal(b, session.NodeById(session.PreviewNodeId)!.Path);
        session.SetOverviewChildLimit(6);
        Assert.Equal(Path.Combine(b, "Nested"), session.NodeById(session.PreviewNodeId)!.Path);
        await session.PreviewSearchHitAsync(null);
        Assert.Same(before, session.Scene);
        Assert.Null(session.PreviewNodeId);
        await session.ActivateSearchHitAsync(hit);
        Assert.Equal(Path.GetDirectoryName(target), session.CurrentPath);
        Assert.Equal(target, session.NodeById(session.SelectedNodeId)!.Path);
    }

    [Fact]
    public async Task SearchHoverDoesNotAddAnotherRootOrRevealRemovedOrUnavailableItems()
    {
        using var files = new WorkspaceFiles();
        var a = files.Directory("A");
        var b = files.Directory("B");
        var target = Path.Combine(b, "needle.txt");
        File.WriteAllText(target, "");
        using var session = new SpatialWorkspaceSession(new MemoryStore(new SpatialWorkspace([new(a, "A"), new(b, "B")], [])));
        await session.InitializeAsync();
        await session.OpenRootAsync(a);
        await session.SearchAsync("needle");
        var hit = Assert.Single(session.SearchResults);
        var scene = session.Scene;
        await session.PreviewSearchHitAsync(hit);
        Assert.Same(scene, session.Scene);
        Assert.Null(session.PreviewNodeId);
        Assert.Equal(a, session.CurrentPath);
        await session.ShowWorkspaceAsync();
        await session.PreviewSearchHitAsync(hit);
        Assert.Equal(target, session.NodeById(session.PreviewNodeId)!.Path);
        File.Delete(target);
        await session.PreviewSearchHitAsync(hit);
        Assert.Null(session.PreviewNodeId);
        session.RemoveRoot(b);
        await session.PreviewSearchHitAsync(hit);
        Assert.Null(session.PreviewNodeId);
        await session.ShowWorkspaceAsync();
        Assert.DoesNotContain(session.Scene!.Nodes, node => node.Path == b || node.Path == target);
    }

    [Fact]
    public async Task SceneBudgetAndPagingKeepOverflowReachable_CompactDensityUsesNoReadsOrSaves()
    {
        using var files = new WorkspaceFiles();
        var root = files.Directory("Root");
        for (var index = 0; index < 125; index++) { File.WriteAllText(Path.Combine(root, $"File-{index:D3}.txt"), ""); }
        var provider = new ControlledProvider(root);
        var store = new MemoryStore(new SpatialWorkspace([new(root, "Root")], []));
        using var session = new SpatialWorkspaceSession(store, _ => provider);
        await session.InitializeAsync();
        var reads = provider.Reads;
        var writes = store.Writes;
        session.SetOverviewChildLimit(0);
        Assert.Single(session.Scene!.Nodes);
        session.SetOverviewChildLimit(2);
        Assert.Equal(3, session.Scene!.Nodes.Count);
        Assert.DoesNotContain(session.Scene.Nodes, node => node.Kind == ExplorerNodeKind.Aggregate);
        Assert.Equal(reads, provider.Reads);
        Assert.Equal(writes, store.Writes);
        await session.NavigateAsync(session.ClusterRootIds[0]);
        var reached = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            Assert.InRange(session.Scene!.Nodes.Count, 1, SpatialWorkspaceSession.MaximumSceneNodes);
            foreach (var node in session.Scene.Nodes.Where(node => node.Kind == ExplorerNodeKind.File)) { reached.Add(node.Path); }
            var next = session.Scene.Nodes.FirstOrDefault(node => node.Name == "Next items");
            if (next is null) { break; }
            await session.NavigateAsync(next.Id);
        }
        Assert.Equal(125, reached.Count);
        await session.OpenRootAsync(root);
        await session.SearchAsync("File-010");
        await session.PreviewSearchHitAsync(Assert.Single(session.SearchResults));
        Assert.Equal(Path.Combine(root, "File-010.txt"), session.NodeById(session.PreviewNodeId)!.Path);
        var focusedReads = provider.Reads;
        session.SetFocusedPageSize(4);
        Assert.Equal(6, session.Scene!.Nodes.Count);
        Assert.Equal(root, session.NodeById(session.PreviewNodeId)!.Path);
        Assert.Equal(focusedReads, provider.Reads);
    }

    [Fact]
    public async Task LateNavigationCannotUndoPinMutationsOrResurrectRemovedRoots()
    {
        using var files = new WorkspaceFiles();
        var root = files.Directory("Root");
        var child = files.Directory("Root/Child");
        var first = files.Directory("First");
        var second = files.Directory("Second");
        var added = files.Directory("Added");
        var provider = new ControlledProvider(root);
        var store = new MemoryStore(new SpatialWorkspace([new(root, "Root")], [new(first, "First"), new(second, "Second")]));
        using var session = new SpatialWorkspaceSession(store, _ => provider);
        await session.InitializeAsync();
        var hold = provider.HoldNextRead(child);
        var navigation = session.NavigateAsync(SpatialWorkspaceSession.Key(root, child));
        await hold.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.RemovePin(first);
        await session.PinFolderAsync(added);
        session.MovePin(added, -1);
        hold.Release.TrySetResult();
        await navigation;
        Assert.Equal(new[] { added, second }, session.State.Pins.Select(pin => pin.Path));
        await session.GoUpAsync();
        hold = provider.HoldNextRead(child);
        navigation = session.NavigateAsync(SpatialWorkspaceSession.Key(root, child));
        await hold.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.RemoveRoot(root);
        hold.Release.TrySetResult();
        await navigation;
        Assert.Empty(session.State.Roots);
        Assert.True(session.IsOverview);
    }

    [Fact]
    public async Task LateRefreshDoesNotOverwriteNewSearchOrRestoreDismissedPreview()
    {
        using var files = new WorkspaceFiles();
        var root = files.Directory("Root");
        File.WriteAllText(Path.Combine(root, "alpha.txt"), "");
        File.WriteAllText(Path.Combine(root, "beta.txt"), "");
        var provider = new ControlledProvider(root);
        using var session = new SpatialWorkspaceSession(new MemoryStore(new SpatialWorkspace([new(root, "Root")], [])), _ => provider);
        await session.InitializeAsync();
        await session.SearchAsync("alpha");
        await session.PreviewSearchHitAsync(Assert.Single(session.SearchResults));
        var hold = provider.HoldNextRead(root);
        var refresh = session.RefreshAsync();
        await hold.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.SearchAsync("beta");
        hold.Release.TrySetResult();
        await refresh;
        Assert.Equal("beta", session.SearchQuery);
        Assert.Equal("beta.txt", Assert.Single(session.SearchResults).Name);
        Assert.Null(session.PreviewNodeId);
        await session.PreviewSearchHitAsync(Assert.Single(session.SearchResults));
        hold = provider.HoldNextRead(root);
        refresh = session.RefreshAsync();
        await hold.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.PreviewSearchHitAsync(null);
        hold.Release.TrySetResult();
        await refresh;
        Assert.Null(session.PreviewNodeId);
    }

    [Fact]
    public async Task FailedSaveIsVisibleWhilePreviousPersistedVersionSurvives()
    {
        using var files = new WorkspaceFiles();
        var pin = files.Directory("Pin");
        var store = new MemoryStore(SpatialWorkspace.Empty) { FailWrites = true };
        using var session = new SpatialWorkspaceSession(store);
        await session.InitializeAsync();
        await session.PinFolderAsync(pin);
        Assert.Single(session.State.Pins);
        Assert.Empty(store.Load().Pins);
        Assert.Contains("could not be saved", session.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SidebarRootNavigationWorksOutsideScene_AndMissingPinKeepsCurrentGraph()
    {
        using var files = new WorkspaceFiles();
        var a = files.Directory("A");
        var child = files.Directory("A/Child");
        var b = files.Directory("B");
        var missing = Path.Combine(files.Path, "MissingPin");
        using var session = new SpatialWorkspaceSession(new MemoryStore(new SpatialWorkspace([new(a, "A"), new(b, "B")], [new(missing, "Missing")])));
        await session.InitializeAsync();
        await session.NavigateAsync(SpatialWorkspaceSession.Key(a, child));
        Assert.Equal(child, session.CurrentPath);
        Assert.Null(session.NodeById(SpatialWorkspaceSession.Key(b, b)));
        await session.OpenRootAsync(b);
        Assert.Equal(b, session.CurrentPath);
        var scene = session.Scene;
        await session.OpenPinAsync(missing);
        Assert.Equal(b, session.CurrentPath);
        Assert.Same(scene, session.Scene);
        Assert.Contains("unavailable", session.Status, StringComparison.OrdinalIgnoreCase);
        await session.OpenRootAsync(files.Path);
        Assert.Equal(b, session.CurrentRoot);
    }

    [Fact]
    public async Task PinReorderMovesAcrossSeveralPositions_AndFocusedPreviewKeepsItsRoot()
    {
        using var files = new WorkspaceFiles();
        var root = files.Directory("Root");
        var child = files.Directory("Root/Child");
        var nested = files.Directory("Root/Child/Nested");
        File.WriteAllText(Path.Combine(nested, "match.txt"), "");
        var paths = Enumerable.Range(0, 4).Select(index => files.Directory($"Pin{index}")).ToArray();
        var store = new MemoryStore(new SpatialWorkspace([new(root, "Root")], paths.Select(path => new PinnedFolder(path, System.IO.Path.GetFileName(path))).ToArray()));
        using var session = new SpatialWorkspaceSession(store);
        await session.InitializeAsync();
        session.MovePin(paths[0], 3);
        Assert.Equal(new[] { paths[1], paths[2], paths[3], paths[0] }, session.State.Pins.Select(pin => pin.Path));
        await session.NavigateAsync(SpatialWorkspaceSession.Key(root, child));
        await session.SearchAsync("match");
        var focus = session.Scene!.FocusNodeId;
        await session.PreviewSearchHitAsync(Assert.Single(session.SearchResults));
        Assert.Equal(focus, Assert.Single(session.ClusterRootIds));
        Assert.Equal(nested, session.NodeById(session.PreviewNodeId)!.Path);
        Assert.Equal(child, session.CurrentPath);
    }

    [Fact]
    public async Task LateSearchCannotReplaceNewQueryOrReturnHitsFromRemovedRoot()
    {
        using var files = new WorkspaceFiles();
        var root = files.Directory("Root");
        File.WriteAllText(Path.Combine(root, "alpha.txt"), "");
        File.WriteAllText(Path.Combine(root, "beta.txt"), "");
        var provider = new ControlledProvider(root);
        using var session = new SpatialWorkspaceSession(new MemoryStore(new SpatialWorkspace([new(root, "Root")], [])), _ => provider);
        await session.InitializeAsync();
        var hold = provider.HoldNextSearch("alpha");
        var oldSearch = session.SearchAsync("alpha");
        await hold.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.SearchAsync("beta");
        hold.Release.TrySetResult();
        await oldSearch;
        Assert.Equal("beta", session.SearchQuery);
        Assert.Equal("beta.txt", Assert.Single(session.SearchResults).Name);
        Assert.False(session.IsSearching);
        hold = provider.HoldNextSearch("alpha");
        oldSearch = session.SearchAsync("alpha");
        await hold.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.RemoveRoot(root);
        hold.Release.TrySetResult();
        await oldSearch;
        Assert.Empty(session.SearchResults);
        Assert.Empty(session.State.Roots);
    }

    [Fact]
    public async Task FocusedResizeRepagesCachedChildrenAndKeepsSelectedFileOnLaterPage()
    {
        using var files = new WorkspaceFiles();
        var root = files.Directory("Root");
        for (var index = 0; index < 90; index++) { File.WriteAllText(Path.Combine(root, $"File-{index:D3}.txt"), ""); }
        var provider = new ControlledProvider(root);
        var store = new MemoryStore(new SpatialWorkspace([new(root, "Root")], []));
        using var session = new SpatialWorkspaceSession(store, _ => provider);
        await session.InitializeAsync();
        await session.OpenRootAsync(root);
        await session.NavigateAsync(Assert.Single(session.Scene!.Nodes, node => node.Name == "Next items").Id);
        var selected = Assert.Single(session.Scene.Nodes, node => node.Name == "File-070.txt");
        session.Select(selected.Id);
        var reads = provider.Reads;
        var writes = store.Writes;
        session.SetFocusedPageSize(4);
        Assert.Equal(68, session.Scene!.AggregatePage!.Offset);
        Assert.Equal(4, session.Scene.AggregatePage.PageSize);
        Assert.Equal(selected.Id, session.NodeById(session.SelectedNodeId)!.Id);
        Assert.Equal(reads, provider.Reads);
        Assert.Equal(writes, store.Writes);
        Assert.Equal(7, session.Scene.Nodes.Count);
        session.SetFocusedPageSize(24);
        Assert.Equal(48, session.Scene!.AggregatePage!.Offset);
        Assert.Equal(selected.Id, session.NodeById(session.SelectedNodeId)!.Id);
    }

    [Fact]
    public void GraphKeysUseNativePathCaseSemanticsButAlwaysOrdinalGraphIdentity()
    {
        using var files = new WorkspaceFiles();
        var root = Path.Combine(files.Path, "Root");
        var child = Path.Combine(root, "Child");
        Assert.Equal(OperatingSystem.IsWindows(), string.Equals(SpatialWorkspaceSession.Key(root, child),
            SpatialWorkspaceSession.Key(root.ToUpperInvariant(), child.ToUpperInvariant()), StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddingNewRootsShowsTheirClusters_WhileDuplicateOrRejectedAddsPreserveNavigation()
    {
        using var files = new WorkspaceFiles();
        var root = files.Directory("Root");
        var child = files.Directory("Root/Child");
        var second = files.Directory("Second");
        var third = files.Directory("Third");
        var pin = files.Directory("Pin");
        var store = new MemoryStore(new SpatialWorkspace([new(root, "Root")], [new(pin, "Pin")]));
        using var session = new SpatialWorkspaceSession(store);
        await session.InitializeAsync();
        await session.NavigateAsync(SpatialWorkspaceSession.Key(root, child));
        var focusedScene = session.Scene;
        var writes = store.Writes;
        await session.AddRootsAsync([root]);
        Assert.Equal(child, session.CurrentPath);
        Assert.Same(focusedScene, session.Scene);
        await session.AddRootsAsync([Path.Combine(files.Path, "missing")]);
        Assert.Equal(child, session.CurrentPath);
        Assert.Same(focusedScene, session.Scene);
        Assert.Equal(writes, store.Writes);
        await session.AddRootsAsync([second, third]);
        Assert.True(session.IsOverview);
        Assert.Equal(new[] { root, second, third }, session.State.Roots.Select(item => item.Path));
        Assert.Equal(new[] { root, second, third }, session.ClusterRootIds.Select(id => session.NodeById(id)!.Path));
        Assert.Equal(pin, Assert.Single(session.State.Pins).Path);
        Assert.Null(store.Load().CurrentPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestartWithDuplicateOrInvalidStartupRootStillRestoresSavedView(bool invalidStartupRoot)
    {
        using var files = new WorkspaceFiles();
        var root = files.Directory("Root");
        var child = files.Directory("Root/Child");
        var pin = files.Directory("Pin");
        var store = new JsonSpatialWorkspaceStore(files.Path);
        Assert.True(store.TrySave(new SpatialWorkspace([new(root, "Root")], [new(pin, "Pin")], root, child)));
        using var session = new SpatialWorkspaceSession(store);
        await session.InitializeAsync(invalidStartupRoot ? Path.Combine(files.Path, "Missing") : root);
        Assert.NotNull(session.Scene);
        Assert.Equal(child, session.Scene.Focus.Path);
        Assert.Equal(root, session.CurrentRoot);
        Assert.Equal(child, session.CurrentPath);
        Assert.Equal(root, Assert.Single(session.State.Roots).Path);
        Assert.Equal(pin, Assert.Single(session.State.Pins).Path);
        Assert.Equal(child, store.Load().CurrentPath);
    }

    private sealed class MemoryStore(SpatialWorkspace initial) : ISpatialWorkspaceStore
    {
        private SpatialWorkspace _saved = initial;
        public int Writes { get; private set; }
        public bool FailWrites { get; init; }
        public SpatialWorkspace Load() => _saved;
        public bool TrySave(SpatialWorkspace workspace)
        {
            Writes++;
            if (FailWrites) { return false; }
            _saved = workspace;
            return true;
        }
    }

    private sealed class ControlledProvider(string root) : IExplorerProvider, IExplorerSearchProvider
    {
        private readonly FileSystemExplorerProvider _inner = new(root);
        private string? _holdPath;
        private ReadHold? _hold;
        private string? _holdQuery;
        private ReadHold? _searchHold;
        public string AccessRoot => root;
        public int Reads { get; private set; }
        public ReadHold HoldNextRead(string path)
        {
            _holdPath = path;
            _hold = new ReadHold();
            return _hold;
        }
        public ReadHold HoldNextSearch(string query)
        {
            _holdQuery = query;
            _searchHold = new ReadHold();
            return _searchHold;
        }
        public async Task<ExplorerDirectorySnapshot> GetDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            Reads++;
            if (_hold is { } hold && path == _holdPath)
            {
                _hold = null;
                hold.Started.TrySetResult();
                await hold.Release.Task; // Deliberately ignores cancellation to exercise late-result guards.
            }
            return await _inner.GetDirectoryAsync(path, CancellationToken.None);
        }
        public async Task<ExplorerSearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            if (_searchHold is { } hold && request.Query == _holdQuery)
            {
                _searchHold = null;
                hold.Started.TrySetResult();
                await hold.Release.Task;
            }
            return await _inner.SearchAsync(request, CancellationToken.None);
        }
    }

    private sealed class ReadHold
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class WorkspaceFiles : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OmniBrille-workspace-tests-" + Guid.NewGuid().ToString("N"));
        public WorkspaceFiles() => System.IO.Directory.CreateDirectory(Path);
        public string Directory(string relative)
        {
            var path = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            System.IO.Directory.CreateDirectory(path);
            return path;
        }
        public void Dispose() => System.IO.Directory.Delete(Path, recursive: true);
    }
}
