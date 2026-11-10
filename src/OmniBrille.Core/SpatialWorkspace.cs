namespace OmniBrille.Core;

/// <summary>Explicit standalone roots and shortcuts. Loading this state never scans unrelated locations.</summary>
public sealed record SpatialWorkspace(
    IReadOnlyList<FileMapRoot> Roots,
    IReadOnlyList<PinnedFolder> Pins,
    string? CurrentRoot = null,
    string? CurrentPath = null,
    int SchemaVersion = 1)
{
    public const int MaximumRoots = 8;
    public const int MaximumPins = 12;

    public static SpatialWorkspace Empty => new([], []);

    public SpatialWorkspace Normalize()
    {
        if (SchemaVersion != 1) { return Empty; }
        var roots = new List<FileMapRoot>();
        foreach (var item in (Roots ?? []).Take(64))
        {
            if (item is null || !FileMapWorkspace.TryNormalizePath(item.Path, out var path) ||
                roots.Any(root => PathBoundary.Comparer.Equals(root.Path, path))) { continue; }
            var label = new string((item.Label ?? string.Empty).Take(256).Where(character => !char.IsControl(character)).Take(80).ToArray()).Trim();
            if (label.Length == 0) { label = Path.GetFileName(path); }
            if (label.Length == 0) { label = path; }
            roots.Add(new FileMapRoot(path, label.Length > 80 ? label[..80] : label));
            if (roots.Count == MaximumRoots) { break; }
        }
        var pins = new PinnedFolders(Pins ?? []).Normalize().Folders.Take(MaximumPins).ToArray();
        string? currentRoot = null;
        string? currentPath = null;
        if (FileMapWorkspace.TryNormalizePath(CurrentRoot, out var requestedRoot) &&
            FileMapWorkspace.TryNormalizePath(CurrentPath, out var requestedPath))
        {
            var root = roots.FirstOrDefault(item => PathBoundary.Comparer.Equals(item.Path, requestedRoot));
            var authorizedRoot = root?.Path ?? pins.FirstOrDefault(pin => PathBoundary.Comparer.Equals(pin.Path, requestedRoot))?.Path;
            if (authorizedRoot is not null && FileMapWorkspace.IsWithin(authorizedRoot, requestedPath))
            {
                currentRoot = authorizedRoot;
                currentPath = requestedPath;
            }
        }
        return new SpatialWorkspace(roots, pins, currentRoot, currentPath);
    }
}

public interface ISpatialWorkspaceStore
{
    public SpatialWorkspace Load();

    public bool TrySave(SpatialWorkspace workspace);
}
