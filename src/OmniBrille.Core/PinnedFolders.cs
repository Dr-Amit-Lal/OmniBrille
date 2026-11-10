namespace OmniBrille.Core;

public sealed record PinnedFolder(string Path, string Name);

/// <summary>
/// User-chosen standalone folders, in display order. Connected identities must never enter this model.
/// Loading pins does not authorize or open a filesystem location; activation must revalidate it.
/// </summary>
public sealed record PinnedFolders(IReadOnlyList<PinnedFolder> Folders, int SchemaVersion = 1)
{
    public const int MaximumFolders = 12;

    public static PinnedFolders Empty => new([]);

    public PinnedFolders Normalize()
    {
        if (SchemaVersion != 1)
        {
            return Empty;
        }

        var folders = new List<PinnedFolder>();
        var seen = new HashSet<string>(PathBoundary.Comparer);
        foreach (var folder in (Folders ?? []).Take(128))
        {
            if (folder is null || !FileMapWorkspace.TryNormalizePath(folder.Path, out var path) || !seen.Add(path))
            {
                continue;
            }

            var name = new string((folder.Name ?? string.Empty).Take(256)
                .Where(character => !char.IsControl(character)).Take(80).ToArray()).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = System.IO.Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = path;
                }
            }

            folders.Add(new PinnedFolder(path, name.Length > 80 ? name[..80] : name));
            if (folders.Count == MaximumFolders)
            {
                break;
            }
        }

        return new PinnedFolders(folders);
    }
}

public interface IPinnedFoldersStore
{
    public PinnedFolders Load();

    public bool TrySave(PinnedFolders folders);
}
