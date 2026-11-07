using OmniBrille.Core;
using OmniBrille.Desktop.Presentation;
using OmniBrille.Infrastructure;

namespace OmniBrille.Tests;

public sealed class MyFilesSessionTests
{
    [Fact]
    public async Task AddExpandSelectOpenAndRestore_UseRealFilesWithoutMovingThem()
    {
        using var directory = new TemporaryDirectory();
        var child = Directory.CreateDirectory(Path.Combine(directory.Path, "Teaching")).FullName;
        var file = Path.Combine(child, "Database syllabus.docx");
        await File.WriteAllTextAsync(file, "test");
        var store = new MemoryStore();
        var activation = new RecordingActivation();
        using (var session = new MyFilesSession(store, activation))
        {
            await session.AddRootAsync(directory.Path, "Documents");
            var folder = Assert.Single(session.VisibleNodes, node => node.Entry.Path == child);
            await session.ToggleFolderAsync(folder.Key);
            session.Select(Assert.Single(session.VisibleNodes, node => node.Entry.Path == file).Key);
            session.PinSelected();
            Assert.Empty(activation.Calls);
            await session.OpenSelectedAsync();
            Assert.Equal((directory.Path, file), Assert.Single(activation.Calls));
            session.SetTextSize(24);
        }

        using var restored = new MyFilesSession(store);
        await restored.InitializeAsync();
        Assert.Contains(restored.VisibleNodes, node => node.Entry.Path == file);
        Assert.Equal(file, Assert.Single(restored.State.Pins).Path);
        Assert.Equal(24, restored.State.TextSize);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task PagingAndReveal_MakeLaterPageFileSelectable()
    {
        using var directory = new TemporaryDirectory();
        for (var index = 0; index < 30; index++)
        {
            await File.WriteAllTextAsync(Path.Combine(directory.Path, $"File-{index:D2}.txt"), "x");
        }

        using var session = new MyFilesSession(new MemoryStore());
        await session.AddRootAsync(directory.Path);
        var root = Assert.Single(session.VisibleNodes, node => node.ParentKey is null);
        Assert.Equal(3, root.PageCount);
        Assert.Equal(MyFilesSession.PageSize + 1, session.VisibleNodes.Count);
        await session.ChangePageAsync(root.Key, 1);
        Assert.DoesNotContain(session.VisibleNodes, node => node.Entry.Name == "File-00.txt");
        Assert.Contains(session.VisibleNodes, node => node.Entry.Name == "File-12.txt");

        var target = Path.Combine(directory.Path, "File-29.txt");
        await session.RevealAsync(directory.Path, target);
        Assert.Equal(target, session.SelectedNode?.Entry.Path);
        Assert.Equal(2, Assert.Single(session.VisibleNodes, node => node.ParentKey is null).PageIndex);
    }

    [Fact]
    public async Task RestartAndBack_RestoreExpandedFolderOnLaterPage()
    {
        using var directory = new TemporaryDirectory();
        for (var index = 0; index < 20; index++)
        {
            Directory.CreateDirectory(Path.Combine(directory.Path, $"Course-{index:D2}"));
        }

        var course = Path.Combine(directory.Path, "Course-19");
        var file = Path.Combine(course, "Syllabus.txt");
        await File.WriteAllTextAsync(file, "x");
        var store = new MemoryStore();
        FileMapLocation[] branches;
        using (var session = new MyFilesSession(store))
        {
            await session.AddRootAsync(directory.Path);
            await session.ChangePageAsync(Assert.Single(session.VisibleNodes, node => node.ParentKey is null).Key, 1);
            await session.ToggleFolderAsync(Assert.Single(session.VisibleNodes, node => node.Entry.Path == course).Key);
            branches = session.State.ExpandedFolders.ToArray();
        }

        using var restored = new MyFilesSession(store);
        await restored.InitializeAsync();
        Assert.Contains(restored.VisibleNodes, node => node.Entry.Path == file);
        Assert.Equal(1, Assert.Single(restored.VisibleNodes, node => node.ParentKey is null).PageIndex);
        restored.Home();
        await restored.RestoreBranchesAsync(branches);
        Assert.Contains(restored.VisibleNodes, node => node.Entry.Path == file);
    }

    [Fact]
    public async Task MultipleRoots_StayVisibleWithinBudgetAndRemovingOnePreservesOthers()
    {
        using var directory = new TemporaryDirectory();
        using var session = new MyFilesSession(new MemoryStore());
        var paths = new List<string>();
        for (var rootIndex = 0; rootIndex < 8; rootIndex++)
        {
            var root = Directory.CreateDirectory(Path.Combine(directory.Path, $"root-{rootIndex}")).FullName;
            paths.Add(root);
            for (var index = 0; index < 20; index++)
            {
                await File.WriteAllTextAsync(Path.Combine(root, $"note-{index}.txt"), "x");
            }

            await session.AddRootAsync(root);
        }

        Assert.Equal(8, session.VisibleNodes.Count(node => node.ParentKey is null));
        Assert.InRange(session.VisibleNodes.Count, 8, MyFilesSession.MaximumVisibleNodes);
        session.RemoveRoot(paths[0]);
        Assert.Equal(7, session.State.Roots.Count);
        Assert.Contains(session.VisibleNodes, node => node.RootPath == paths[1] && node.Entry.Kind == ExplorerNodeKind.File);
        Assert.DoesNotContain(session.VisibleNodes, node => node.RootPath == paths[0]);
    }

    [Fact]
    public async Task LateDirectoryResponse_CannotRestoreRemovedRoot()
    {
        using var directory = new TemporaryDirectory();
        var store = new MemoryStore
        {
            Workspace = new FileMapWorkspace([new(directory.Path, "Documents")], [], []),
        };
        var provider = new DeferredProvider(directory.Path) { DelayDirectory = true };
        using var session = new MyFilesSession(store, providerFactory: _ => provider);
        await session.InitializeAsync();
        var loading = session.ToggleFolderAsync(Assert.Single(session.VisibleNodes).Key);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.RemoveRoot(directory.Path);
        provider.DirectoryResult.SetResult(new ExplorerDirectorySnapshot(
            new ExplorerEntry(directory.Path, "Documents", directory.Path, ExplorerNodeKind.Folder), []));
        await loading;

        Assert.Empty(session.State.Roots);
        Assert.Empty(session.VisibleNodes);
        Assert.False(session.IsBusy);
    }

    [Fact]
    public async Task LateSearchResponse_CannotReplaceNewSearch()
    {
        using var directory = new TemporaryDirectory();
        var provider = new DeferredProvider(directory.Path);
        var store = new MemoryStore { Workspace = new FileMapWorkspace([new(directory.Path, "Documents")], [], []) };
        using var session = new MyFilesSession(store, providerFactory: _ => provider);
        await session.InitializeAsync();
        var first = session.SearchAsync("first");
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.SearchAsync("second");
        provider.SearchResult.SetResult(new ExplorerSearchResult(
            [new("first", "first.txt", Path.Combine(directory.Path, "first.txt"), ExplorerNodeKind.File)], false, 1));
        await first;

        Assert.Equal("second.txt", Assert.Single(session.SearchResults).Name);
        Assert.False(session.IsBusy);
    }

    [Fact]
    public async Task ExplicitSelection_CancelsPendingRevealAndKeepsCurrentBranches()
    {
        using var directory = new TemporaryDirectory();
        var first = Path.Combine(directory.Path, "first.txt");
        var second = Path.Combine(directory.Path, "second.txt");
        await File.WriteAllTextAsync(first, "x");
        await File.WriteAllTextAsync(second, "x");
        var snapshot = await new FileSystemExplorerProvider(directory.Path).GetDirectoryAsync(directory.Path, CancellationToken.None);
        var provider = new DeferredProvider(directory.Path) { Snapshot = snapshot };
        using var session = new MyFilesSession(new MemoryStore(), providerFactory: _ => provider);
        await session.AddRootAsync(directory.Path);
        var branches = session.State.ExpandedFolders.ToArray();
        provider.DelayDirectory = true;
        var reveal = session.RevealAsync(directory.Path, first);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.Select(Assert.Single(session.VisibleNodes, node => node.Entry.Path == second).Key);
        provider.DirectoryResult.SetResult(snapshot);
        await reveal;

        Assert.Equal(second, session.SelectedNode?.Entry.Path);
        Assert.Equal(branches, session.State.ExpandedFolders);
        Assert.False(session.IsBusy);
    }

    [Fact]
    public async Task ForeignPathAndMissingPinnedFile_AreNotActivated()
    {
        using var directory = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var file = Path.Combine(directory.Path, "note.txt");
        await File.WriteAllTextAsync(file, "x");
        var activation = new RecordingActivation();
        using var session = new MyFilesSession(new MemoryStore(), activation);
        await session.AddRootAsync(directory.Path);
        await session.RevealAsync(directory.Path, Path.Combine(outside.Path, "secret.txt"));
        Assert.Null(session.SelectedNode);
        session.Select(Assert.Single(session.VisibleNodes, node => node.Entry.Path == file).Key);
        File.Delete(file);
        await session.OpenSelectedAsync();
        Assert.Empty(activation.Calls);
        Assert.Contains("unavailable", session.Status, StringComparison.Ordinal);
    }

    [UnixSymlinkFact]
    public async Task RestoredExpansionAndActivation_RejectReplacedDirectory()
    {
        using var directory = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var child = Directory.CreateDirectory(Path.Combine(directory.Path, "Teaching")).FullName;
        await File.WriteAllTextAsync(Path.Combine(outside.Path, "secret.txt"), "x");
        var store = new MemoryStore
        {
            Workspace = new FileMapWorkspace([new(directory.Path, "Documents")],
                [new(directory.Path, directory.Path), new(directory.Path, child)], []),
        };
        Directory.Delete(child);
        Directory.CreateSymbolicLink(child, outside.Path);
        try
        {
            using var session = new MyFilesSession(store);
            await session.InitializeAsync();
            Assert.DoesNotContain(session.VisibleNodes, node => node.Entry.Name == "secret.txt");
            Assert.False(Assert.Single(session.VisibleNodes, node => node.Entry.Path == child).Entry.IsNavigable);
            await session.RevealAsync(directory.Path, Path.Combine(child, "secret.txt"));
            Assert.Null(session.SelectedNode);
        }
        finally
        {
            Directory.Delete(child);
        }
    }

    [UnixSymlinkFact]
    public async Task Activation_RejectsRootReplacedAfterSelection()
    {
        using var parent = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var root = Directory.CreateDirectory(Path.Combine(parent.Path, "Documents")).FullName;
        var file = Path.Combine(root, "note.txt");
        await File.WriteAllTextAsync(file, "original");
        await File.WriteAllTextAsync(Path.Combine(outside.Path, "note.txt"), "outside");
        var activation = new RecordingActivation();
        using var session = new MyFilesSession(new MemoryStore(), activation);
        await session.AddRootAsync(root);
        session.Select(Assert.Single(session.VisibleNodes, node => node.Entry.Path == file).Key);
        File.Delete(file);
        Directory.Delete(root);
        Directory.CreateSymbolicLink(root, outside.Path);
        try
        {
            await session.OpenSelectedAsync();
            Assert.Empty(activation.Calls);
            await session.RefreshAsync();
            Assert.DoesNotContain(session.VisibleNodes, node => node.Entry.Kind == ExplorerNodeKind.File);
        }
        finally
        {
            Directory.Delete(root);
        }
    }

    [Fact]
    public async Task SaveFailureAndSourceTruncation_AreVisible()
    {
        using var directory = new TemporaryDirectory();
        var provider = new DeferredProvider(directory.Path) { Truncated = true };
        using var session = new MyFilesSession(new MemoryStore { CanSave = false }, providerFactory: _ => provider);
        await session.AddRootAsync(directory.Path);
        Assert.True(Assert.Single(session.VisibleNodes).Truncated);
        Assert.Contains("5,000", session.Status, StringComparison.Ordinal);
        Assert.Contains("could not be saved", session.Status, StringComparison.Ordinal);
    }

    private sealed class MemoryStore : IFileMapWorkspaceStore
    {
        public FileMapWorkspace Workspace { get; set; } = FileMapWorkspace.Empty;

        public bool CanSave { get; init; } = true;

        public FileMapWorkspace Load() => Workspace;

        public bool TrySave(FileMapWorkspace workspace)
        {
            Workspace = workspace;
            return CanSave;
        }
    }

    private sealed class RecordingActivation : IFileActivationService
    {
        public List<(string Root, string Path)> Calls { get; } = [];

        public Task<FileActivationResult> OpenAsync(string accessRoot, string path, CancellationToken cancellationToken = default)
        {
            Calls.Add((accessRoot, path));
            return Task.FromResult(FileActivationResult.Opened);
        }
    }

    private sealed class DeferredProvider(string root) : IExplorerProvider, IExplorerSearchProvider
    {
        public string AccessRoot => root;

        public bool DelayDirectory { get; set; }

        public bool Truncated { get; init; }

        public ExplorerDirectorySnapshot? Snapshot { get; init; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<ExplorerDirectorySnapshot> DirectoryResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<ExplorerSearchResult> SearchResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ExplorerDirectorySnapshot> GetDirectoryAsync(string path, CancellationToken cancellationToken)
        {
            if (DelayDirectory)
            {
                Started.TrySetResult();
                return DirectoryResult.Task;
            }

            return Task.FromResult(Snapshot ?? new ExplorerDirectorySnapshot(
                new ExplorerEntry(root, "Documents", root, ExplorerNodeKind.Folder), [], WasTruncated: Truncated));
        }

        public Task<ExplorerSearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            if (request.Query == "first")
            {
                Started.TrySetResult();
                return SearchResult.Task;
            }

            return Task.FromResult(new ExplorerSearchResult(
                [new("second", "second.txt", Path.Combine(root, "second.txt"), ExplorerNodeKind.File)], false, 1));
        }
    }

    private sealed class UnixSymlinkFactAttribute : FactAttribute
    {
        public UnixSymlinkFactAttribute()
        {
            if (OperatingSystem.IsWindows())
            {
                Skip = "Uses unprivileged Unix symlinks; native Windows junction acceptance is separate.";
            }
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"OmniBrilleMapSession-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
