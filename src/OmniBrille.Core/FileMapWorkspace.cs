namespace OmniBrille.Core;

public sealed record FileMapRoot(string Path, string Label);

public sealed record FileMapLocation(string RootPath, string Path);

public sealed record FileMapPin(string RootPath, string Path, string Name);

public sealed record FileMapPage(string RootPath, string Path, int PageIndex);

/// <summary>Local, user-selected filesystem locations. Never contains connected provider identities.</summary>
public sealed record FileMapWorkspace(
    IReadOnlyList<FileMapRoot> Roots,
    IReadOnlyList<FileMapLocation> ExpandedFolders,
    IReadOnlyList<FileMapPin> Pins,
    double TextSize = 18,
    int SchemaVersion = 1,
    IReadOnlyList<FileMapPage>? Pages = null)
{
    public const int MaximumRoots = 8;
    public const int MaximumExpandedFolders = 24;
    public const int MaximumPins = 6;

    public static FileMapWorkspace Empty => new([], [], []);

    public FileMapWorkspace Normalize()
    {
        if (SchemaVersion != 1)
        {
            return Empty;
        }

        var roots = new List<FileMapRoot>();
        foreach (var root in (Roots ?? []).Take(64))
        {
            if (root is null || !TryNormalizePath(root.Path, out var path) ||
                roots.Any(item => PathBoundary.Comparer.Equals(item.Path, path)))
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(root.Label) ? System.IO.Path.GetFileName(path) : root.Label;
            label = new string(label.Where(character => !char.IsControl(character)).Take(80).ToArray());
            roots.Add(new FileMapRoot(path, string.IsNullOrWhiteSpace(label) ? path : label));
            if (roots.Count == MaximumRoots)
            {
                break;
            }
        }

        bool TryLocation(string? rootPath, string? targetPath, out FileMapLocation location)
        {
            location = new FileMapLocation(string.Empty, string.Empty);
            if (!TryNormalizePath(rootPath, out var root) || !TryNormalizePath(targetPath, out var target) ||
                !roots.Any(item => PathBoundary.Comparer.Equals(item.Path, root)) || !IsWithin(root, target))
            {
                return false;
            }

            location = new FileMapLocation(root, target);
            return true;
        }

        var expanded = new List<FileMapLocation>();
        foreach (var item in (ExpandedFolders ?? []).Take(256))
        {
            if (item is not null && TryLocation(item.RootPath, item.Path, out var location) &&
                !expanded.Any(existing => SameLocation(existing, location)))
            {
                expanded.Add(location);
                if (expanded.Count == MaximumExpandedFolders)
                {
                    break;
                }
            }
        }

        var pins = new List<FileMapPin>();
        foreach (var item in (Pins ?? []).Take(128))
        {
            if (item is not null && TryLocation(item.RootPath, item.Path, out var location) &&
                !pins.Any(existing => PathBoundary.Comparer.Equals(existing.Path, location.Path)))
            {
                pins.Add(new FileMapPin(location.RootPath, location.Path, System.IO.Path.GetFileName(location.Path)));
                if (pins.Count == MaximumPins)
                {
                    break;
                }
            }
        }

        var pages = new List<FileMapPage>();
        foreach (var item in (Pages ?? []).Take(256))
        {
            if (item is not null && TryLocation(item.RootPath, item.Path, out var location) &&
                expanded.Any(existing => SameLocation(existing, location)) &&
                !pages.Any(existing => PathBoundary.Comparer.Equals(existing.RootPath, location.RootPath) &&
                                       PathBoundary.Comparer.Equals(existing.Path, location.Path)))
            {
                pages.Add(new FileMapPage(location.RootPath, location.Path, Math.Clamp(item.PageIndex, 0, 416)));
            }
        }

        return new FileMapWorkspace(roots, expanded, pins, double.IsFinite(TextSize) ? Math.Clamp(TextSize, 16, 28) : 18, Pages: pages);
    }

    public static bool TryNormalizePath(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096 || value.Any(char.IsControl) ||
            !System.IO.Path.IsPathFullyQualified(value))
        {
            return false;
        }

        try
        {
            normalized = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(value));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
        {
            return false;
        }
    }

    public static bool IsWithin(string root, string target) =>
        PathBoundary.Comparer.Equals(root, target) || target.StartsWith(
            System.IO.Path.EndsInDirectorySeparator(root) ? root : root + System.IO.Path.DirectorySeparatorChar,
            PathBoundary.Comparison);

    private static bool SameLocation(FileMapLocation left, FileMapLocation right) =>
        PathBoundary.Comparer.Equals(left.RootPath, right.RootPath) && PathBoundary.Comparer.Equals(left.Path, right.Path);
}

public interface IFileMapWorkspaceStore
{
    public FileMapWorkspace Load();

    public bool TrySave(FileMapWorkspace workspace);
}

public sealed record FileMapNode(
    string Key,
    string? ParentKey,
    string RootPath,
    ExplorerEntry Entry,
    int Depth,
    bool Expanded,
    int PageIndex = 0,
    int PageCount = 1,
    int TotalChildren = 0,
    bool Truncated = false);

public sealed record FileMapSearchHit(string RootPath, ExplorerSearchHit Hit)
{
    public string Name => Hit.Name;

    public string Path => Hit.Path;

    public ExplorerNodeKind Kind => Hit.Kind;
}
