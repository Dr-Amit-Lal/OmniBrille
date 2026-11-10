using OmniBrille.Core;

namespace OmniBrille.Infrastructure;

/// <summary>Revalidates ordinary paths and verified Cloud Files placeholders before reads or activation.</summary>
public static class FileMapPathGuard
{
    public static bool IsOrdinaryPath(string rootPath, string path, bool requireDirectory)
    {
        if (!FileMapWorkspace.TryNormalizePath(rootPath, out var root) ||
            !FileMapWorkspace.TryNormalizePath(path, out var target) || !FileMapWorkspace.IsWithin(root, target))
        {
            return false;
        }

        try
        {
            // Check from the volume/share root inward, before inspecting children of
            // any potential link. This includes ancestors above an explicitly selected
            // root: a normal folder underneath a junction is still redirected authority.
            var ancestors = new Stack<string>();
            var current = target;
            while (current is not null)
            {
                ancestors.Push(current);
                current = Path.GetDirectoryName(current);
            }

            while (ancestors.TryPop(out current))
            {
                var attributes = File.GetAttributes(current);
                if (FileSystemPathClassifier.Classify(current, attributes) is not
                    (FileSystemPathKind.Ordinary or FileSystemPathKind.CloudPlaceholder) ||
                    ((requireDirectory || !PathBoundary.Comparer.Equals(current, target) ||
                      PathBoundary.Comparer.Equals(current, root)) &&
                     (attributes & FileAttributes.Directory) == 0))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
