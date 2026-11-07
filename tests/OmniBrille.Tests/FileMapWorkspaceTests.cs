using OmniBrille.Core;
using OmniBrille.Infrastructure;

namespace OmniBrille.Tests;

public sealed class FileMapWorkspaceTests
{
    [Fact]
    public void Store_RoundTripsChosenLocationsAndNormalizesBounds()
    {
        using var directory = new TemporaryDirectory();
        var root = Path.Combine(directory.Path, "Documents");
        var child = Path.Combine(root, "Teaching");
        var file = Path.Combine(child, "Lesson.docx");
        var outside = Path.Combine(directory.Path, "Outside", "secret.txt");
        var store = new JsonFileMapWorkspaceStore(directory.Path);
        var expected = new FileMapWorkspace(
            [new(root, "Teaching"), new(root, "Duplicate"), new("relative", "Invalid")],
            [new(root, child), new(root, outside)],
            [new(root, file, "stale name"), new(root, outside, "secret")], 300);

        Assert.True(store.TrySave(expected));
        var actual = store.Load();

        Assert.Equal(new FileMapRoot(root, "Teaching"), Assert.Single(actual.Roots));
        Assert.Equal(new FileMapLocation(root, child), Assert.Single(actual.ExpandedFolders));
        Assert.Equal(new FileMapPin(root, file, "Lesson.docx"), Assert.Single(actual.Pins));
        Assert.Equal(28, actual.TextSize);
        Assert.False(File.Exists(Path.Combine(directory.Path, "visual-preferences.json")));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"SchemaVersion\":99}")]
    [InlineData("{\"Roots\":null,\"ExpandedFolders\":null,\"Pins\":null}")]
    public void Store_CorruptOrUnsupportedInputReturnsEmptyWorkspace(string json)
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, JsonFileMapWorkspaceStore.FileName), json);

        var actual = new JsonFileMapWorkspaceStore(directory.Path).Load();

        Assert.Empty(actual.Roots);
        Assert.Empty(actual.Pins);
        Assert.Empty(actual.ExpandedFolders);
        Assert.Equal(18, actual.TextSize);
    }

    [Fact]
    public void Store_OversizedFileAndUnwritableDestinationFailSafely()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, JsonFileMapWorkspaceStore.FileName),
            new string(' ', JsonFileMapWorkspaceStore.MaximumFileBytes + 1));
        Assert.Empty(new JsonFileMapWorkspaceStore(directory.Path).Load().Roots);

        var blocker = Path.Combine(directory.Path, "file-not-directory");
        File.WriteAllText(blocker, "x");
        Assert.False(new JsonFileMapWorkspaceStore(blocker).TrySave(FileMapWorkspace.Empty));
    }

    [Fact]
    public void Normalization_RejectsPrefixSiblingAndSupportsFilesystemRoot()
    {
        using var directory = new TemporaryDirectory();
        var root = Path.Combine(directory.Path, "Documents");
        Assert.False(FileMapWorkspace.IsWithin(root, root + "-outside"));
        Assert.True(FileMapWorkspace.IsWithin(Path.GetPathRoot(directory.Path)!, directory.Path));
        Assert.True(PathBoundary.IsWithin(Path.GetPathRoot(directory.Path)!, directory.Path));
        Assert.False(FileMapWorkspace.TryNormalizePath("relative", out _));
        Assert.False(FileMapWorkspace.TryNormalizePath(directory.Path + "\n", out _));
        Assert.Equal(18, (FileMapWorkspace.Empty with { TextSize = double.NaN }).Normalize().TextSize);
    }

    [UnixSymlinkFact]
    public void Guard_RejectsLinkedRootAndLinkedIntermediateDirectory()
    {
        using var directory = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var secret = Path.Combine(outside.Path, "note.txt");
        File.WriteAllText(secret, "x");
        var link = Path.Combine(directory.Path, "linked");
        Directory.CreateSymbolicLink(link, outside.Path);
        try
        {
            Assert.False(FileMapPathGuard.IsOrdinaryPath(link, link, true));
            Assert.False(FileMapPathGuard.IsOrdinaryPath(directory.Path, Path.Combine(link, "note.txt"), false));
            Assert.False(FileMapPathGuard.IsOrdinaryPath(directory.Path, outside.Path, true));
            Assert.True(FileMapPathGuard.IsOrdinaryPath(outside.Path, secret, false));
        }
        finally
        {
            Directory.Delete(link);
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"OmniBrilleFileMapTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
