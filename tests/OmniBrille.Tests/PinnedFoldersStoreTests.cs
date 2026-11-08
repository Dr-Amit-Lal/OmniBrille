using OmniBrille.Core;
using OmniBrille.Infrastructure;

namespace OmniBrille.Tests;

public sealed class PinnedFoldersStoreTests
{
    [Fact]
    public void Store_RetainsOnlyExplicitPinsInTheirChosenOrder()
    {
        using var directory = new TemporaryDirectory();
        var first = new PinnedFolder(Path.Combine(directory.Path, "Teaching"), "My courses");
        var second = new PinnedFolder(Path.Combine(directory.Path, "Research"), "Research");
        var store = new JsonPinnedFoldersStore(directory.Path);
        Assert.True(store.TrySave(new PinnedFolders([second, first])));

        Assert.Equal(new[] { second, first }, store.Load().Folders);
        Assert.Equal(JsonPinnedFoldersStore.FileName, Path.GetFileName(Assert.Single(Directory.GetFiles(directory.Path))));
        Assert.False(Directory.Exists(first.Path));
        Assert.False(Directory.Exists(second.Path));

        Assert.True(store.TrySave(new PinnedFolders([first])));
        Assert.Equal(first, Assert.Single(store.Load().Folders));
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void Normalize_RejectsMalformedPathsAndPreservesFirstDuplicateName()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Teaching");
        var normalized = new PinnedFolders([
            new("relative", "Invalid"),
            new(path + "\n", "Invalid"),
            new(path, "My\ncourses"),
            new(Path.Combine(path, "."), "Duplicate"),
            new(Path.Combine(directory.Path, "Research"), "\t\n")]).Normalize();

        Assert.Equal(new[] { new PinnedFolder(path, "Mycourses"), new PinnedFolder(Path.Combine(directory.Path, "Research"), "Research") }, normalized.Folders);
    }

    [Fact]
    public void Normalize_BoundsCountNamesAndUsesNativePathCaseSemantics()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Teaching");
        var casePair = new PinnedFolders([new(path, "Teaching"), new(Path.Combine(directory.Path, "teaching"), "Lowercase")]).Normalize();
        Assert.Equal(OperatingSystem.IsWindows() ? 1 : 2, casePair.Folders.Count);

        var bounded = new PinnedFolders(Enumerable.Range(0, 150)
            .Select(index => new PinnedFolder(Path.Combine(directory.Path, $"Folder-{index}"), new string('x', 200))).ToArray()).Normalize();
        Assert.Equal(PinnedFolders.MaximumFolders, bounded.Folders.Count);
        Assert.All(bounded.Folders, folder => Assert.Equal(80, folder.Name.Length));
        Assert.EndsWith("Folder-0", bounded.Folders[0].Path, StringComparison.Ordinal);
        Assert.EndsWith("Folder-11", bounded.Folders[^1].Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_RetainsFilesystemRootWithNonemptyLabel()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var folder = Assert.Single(new PinnedFolders([new(root, "")]).Normalize().Folders);
        Assert.Equal(root, folder.Path);
        Assert.False(string.IsNullOrWhiteSpace(folder.Name));
    }

    [Fact]
    public void Normalize_DoesNotSearchUnboundedInputForMoreValidPins()
    {
        var folders = Enumerable.Repeat(new PinnedFolder("relative", "Invalid"), 128)
            .Append(new PinnedFolder(Path.GetTempPath(), "Beyond input bound")).ToArray();
        Assert.Empty(new PinnedFolders(folders).Normalize().Folders);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"SchemaVersion\":99}")]
    [InlineData("{\"Folders\":null}")]
    [InlineData("{\"Folders\":[null, {\"Path\":null,\"Name\":null}]}")]
    [InlineData("{\"Folders\":{}}")]
    public void Store_CorruptOrUnsupportedInputReturnsEmpty(string json)
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, JsonPinnedFoldersStore.FileName), json);
        Assert.Empty(new JsonPinnedFoldersStore(directory.Path).Load().Folders);
    }

    [Fact]
    public void Store_OversizedFileAndUnwritableDestinationFailSafely()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonPinnedFoldersStore(directory.Path);
        Assert.Empty(store.Load().Folders);
        File.WriteAllText(Path.Combine(directory.Path, JsonPinnedFoldersStore.FileName), new string(' ', JsonPinnedFoldersStore.MaximumFileBytes + 1));
        Assert.Empty(store.Load().Folders);

        var blocker = Path.Combine(directory.Path, "file-not-directory");
        File.WriteAllText(blocker, "original");
        Assert.False(new JsonPinnedFoldersStore(blocker).TrySave(PinnedFolders.Empty));
        Assert.Equal("original", File.ReadAllText(blocker));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public void Store_DoesNotImportLegacyMyFilesWorkspace()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, JsonFileMapWorkspaceStore.FileName), "legacy state");
        Assert.Empty(new JsonPinnedFoldersStore(directory.Path).Load().Folders);
        Assert.Equal("legacy state", File.ReadAllText(Path.Combine(directory.Path, JsonFileMapWorkspaceStore.FileName)));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"OmniBrillePinnedFoldersTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
