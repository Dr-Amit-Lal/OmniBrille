using OmniBrille.Core;
using OmniBrille.Infrastructure;

namespace OmniBrille.Tests;

public sealed class FileSystemPathClassifierTests
{
    [Theory]
    [InlineData(0x9000001Au)]
    [InlineData(0x9000101Au)]
    [InlineData(0x9000201Au)]
    [InlineData(0x9000301Au)]
    [InlineData(0x9000401Au)]
    [InlineData(0x9000501Au)]
    [InlineData(0x9000601Au)]
    [InlineData(0x9000701Au)]
    [InlineData(0x9000801Au)]
    [InlineData(0x9000901Au)]
    [InlineData(0x9000A01Au)]
    [InlineData(0x9000B01Au)]
    [InlineData(0x9000C01Au)]
    [InlineData(0x9000D01Au)]
    [InlineData(0x9000E01Au)]
    [InlineData(0x9000F01Au)]
    public void CloudTagPolicy_AllowsOnlyTheDocumentedCloudFilesFamily(uint tag)
    {
        Assert.True(FileSystemPathClassifier.IsSupportedCloudReparseTag(tag));
    }

    [Theory]
    [InlineData(0xA0000003u)] // Junction/mount point.
    [InlineData(0xA000000Cu)] // Windows symbolic link.
    [InlineData(0xA000001Du)] // Unix symbolic link on Windows.
    [InlineData(0x80000021u)] // Legacy ONEDRIVE tag is documented as unused.
    [InlineData(0x80000015u)] // Legacy placeholder, not Cloud Files.
    [InlineData(0x9000001Cu)] // Projected filesystem is not a Cloud Files placeholder.
    [InlineData(0x8000001Eu)] // Azure Storage Sync is a different filter.
    [InlineData(0x90000027u)] // Azure Storage Sync folder.
    [InlineData(0xB000001Au)] // Cloud-shaped tag carrying the name-surrogate bit.
    [InlineData(0xB000F01Au)]
    [InlineData(0x9001001Au)] // Reserved bits cannot extend the allowlist.
    [InlineData(0x1000001Au)] // Missing Microsoft ownership bit.
    [InlineData(0u)]
    public void CloudTagPolicy_RejectsLinksUnknownTagsAndCloudLookalikes(uint tag)
    {
        Assert.False(FileSystemPathClassifier.IsSupportedCloudReparseTag(tag));
    }

    [Fact]
    public void CloudTagPolicy_FailsClosedWhenTagCannotBeRead()
    {
        Assert.False(FileSystemPathClassifier.IsSupportedCloudReparseTag(null));
        var missing = Path.Combine(Path.GetTempPath(), $"OmniBrilleMissing-{Guid.NewGuid():N}");

        Assert.Equal(FileSystemPathKind.Unavailable, FileSystemPathClassifier.Classify(missing));
        Assert.Equal(FileSystemPathKind.BlockedReparsePoint,
            FileSystemPathClassifier.Classify(missing, FileAttributes.ReparsePoint));
    }

    [Fact]
    public void OrdinaryFilesAndDirectoriesRemainAccessibleWithoutCloudSupport()
    {
        using var root = new TemporaryDirectory();
        var folder = Directory.CreateDirectory(Path.Combine(root.Path, "Documents"));
        var file = Path.Combine(folder.FullName, "note.txt");
        File.WriteAllText(file, "metadata only");

        Assert.Equal(FileSystemPathKind.Ordinary, FileSystemPathClassifier.Classify(folder.FullName));
        Assert.Equal(FileSystemPathKind.Ordinary, FileSystemPathClassifier.Classify(file));
        Assert.True(FileMapPathGuard.IsOrdinaryPath(root.Path, folder.FullName, requireDirectory: true));
        Assert.True(FileMapPathGuard.IsOrdinaryPath(root.Path, file, requireDirectory: false));
        Assert.False(FileMapPathGuard.IsOrdinaryPath(root.Path, file, requireDirectory: true));
        Assert.False(FileMapPathGuard.IsOrdinaryPath(file, file, requireDirectory: false));
    }

    [UnixSymlinkFact]
    public async Task DirectoryLinksNeverBecomeCloudFoldersOrAuthorizeOutsideEntries()
    {
        using var root = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var nested = Directory.CreateDirectory(Path.Combine(outside.Path, "nested"));
        await File.WriteAllTextAsync(Path.Combine(nested.FullName, "private.txt"), "private");
        var link = Path.Combine(root.Path, "linked-folder");
        Directory.CreateSymbolicLink(link, outside.Path);
        try
        {
            Assert.Equal(FileSystemPathKind.BlockedReparsePoint, FileSystemPathClassifier.Classify(link));
            Assert.False(FileMapPathGuard.IsOrdinaryPath(root.Path, link, requireDirectory: true));
            Assert.False(FileMapPathGuard.IsOrdinaryPath(link, link, requireDirectory: true));
            Assert.False(FileMapPathGuard.IsOrdinaryPath(
                Path.Combine(link, "nested"), Path.Combine(link, "nested"), requireDirectory: true));

            var provider = new FileSystemExplorerProvider(root.Path);
            var rootSnapshot = await provider.GetDirectoryAsync(root.Path, CancellationToken.None);
            var linkedEntry = Assert.Single(rootSnapshot.Children);
            Assert.True(linkedEntry.IsReparsePoint);
            Assert.False(linkedEntry.IsNavigable);

            var direct = await provider.GetDirectoryAsync(link, CancellationToken.None);
            var nestedDirect = await provider.GetDirectoryAsync(Path.Combine(link, "nested"), CancellationToken.None);
            var preview = await provider.GetDirectoryPreviewAsync(link, CancellationToken.None);
            Assert.All(new[] { direct, nestedDirect, preview }, snapshot =>
            {
                Assert.Equal(ExplorerFailureKind.AccessDenied, snapshot.Failure);
                Assert.Empty(snapshot.Children);
            });

            var search = await provider.SearchAsync(new SearchRequest(root.Path, "private"), CancellationToken.None);
            var linkedSearch = await provider.SearchAsync(new SearchRequest(link, "private"), CancellationToken.None);
            var nestedRootProvider = new FileSystemExplorerProvider(Path.Combine(link, "nested"));
            var nestedRoot = await nestedRootProvider.GetDirectoryAsync(nestedRootProvider.AccessRoot, CancellationToken.None);
            Assert.Empty(search.Hits);
            Assert.Empty(linkedSearch.Hits);
            Assert.NotNull(linkedSearch.Warning);
            Assert.Equal(ExplorerFailureKind.AccessDenied, nestedRoot.Failure);
            Assert.Empty(nestedRoot.Children);
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [UnixSymlinkFact]
    public async Task FolderReplacementBetweenBatchesCannotRedirectMetadataReads()
    {
        using var root = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var selected = Directory.CreateDirectory(Path.Combine(root.Path, "selected")).FullName;
        await File.WriteAllTextAsync(Path.Combine(selected, "note.txt"), "ordinary");
        await File.WriteAllTextAsync(Path.Combine(outside.Path, "note.txt"), "private outside content");
        var provider = new FileSystemExplorerProvider(root.Path);
        await using var batches = provider.GetDirectoryBatchesAsync(selected, 1, CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await batches.MoveNextAsync());
        Assert.Empty(batches.Current.AddedChildren);
        Directory.Move(selected, Path.Combine(root.Path, "moved"));
        Directory.CreateSymbolicLink(selected, outside.Path);
        try
        {
            Assert.True(await batches.MoveNextAsync());
            Assert.Equal(ExplorerFailureKind.AccessDenied, batches.Current.Failure);
            Assert.Empty(batches.Current.AddedChildren);
            Assert.True(batches.Current.IsComplete);
            Assert.False(await batches.MoveNextAsync());
        }
        finally
        {
            Directory.Delete(selected);
        }
    }

    [UnixSymlinkFact]
    public async Task FileAndRootLinksAreRejectedBeforeShellActivation()
    {
        using var root = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var actual = Path.Combine(outside.Path, "note.txt");
        await File.WriteAllTextAsync(actual, "private");
        var fileLink = Path.Combine(root.Path, "linked.txt");
        var directoryLink = Path.Combine(root.Path, "linked-folder");
        File.CreateSymbolicLink(fileLink, actual);
        Directory.CreateSymbolicLink(directoryLink, outside.Path);
        try
        {
            var service = new ShellFileActivationService();
            Assert.Equal(FileSystemPathKind.BlockedReparsePoint, FileSystemPathClassifier.Classify(fileLink));
            Assert.Equal(FileActivationResult.ReparsePointBlocked, await service.OpenAsync(root.Path, fileLink));
            Assert.Equal(FileActivationResult.ReparsePointBlocked,
                await service.OpenAsync(root.Path, Path.Combine(directoryLink, "note.txt")));
            Assert.Equal(FileActivationResult.ReparsePointBlocked,
                await service.OpenAsync(directoryLink, Path.Combine(directoryLink, "note.txt")));
        }
        finally
        {
            File.Delete(fileLink);
            Directory.Delete(directoryLink);
        }
    }

    [Fact]
    public async Task ShellActivation_CancellationPreventsFileInspectionAndLaunch()
    {
        using var root = new TemporaryDirectory();
        var path = Path.Combine(root.Path, "note.txt");
        await File.WriteAllTextAsync(path, "ordinary file");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ShellFileActivationService().OpenAsync(root.Path, path, new CancellationToken(canceled: true)));
    }

    private sealed class UnixSymlinkFactAttribute : FactAttribute
    {
        public UnixSymlinkFactAttribute()
        {
            if (OperatingSystem.IsWindows())
            {
                Skip = "Unprivileged Unix symlink fixture; native Windows cloud/junction behavior needs a platform check.";
            }
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"OmniBrillePathTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
