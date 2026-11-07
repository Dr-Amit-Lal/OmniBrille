using OmniBrille.Core;

namespace OmniBrille.Infrastructure;

/// <summary>Revalidates saved local selections before every filesystem read or activation.</summary>
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
            var attributes = File.GetAttributes(target);
            if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                (requireDirectory && (attributes & FileAttributes.Directory) == 0))
            {
                return false;
            }

            var current = (attributes & FileAttributes.Directory) != 0 ? target : Path.GetDirectoryName(target);
            while (current is not null)
            {
                var ancestor = File.GetAttributes(current);
                if ((ancestor & FileAttributes.ReparsePoint) != 0 || (ancestor & FileAttributes.Directory) == 0)
                {
                    return false;
                }

                if (PathBoundary.Comparer.Equals(current, root))
                {
                    return true;
                }

                current = Path.GetDirectoryName(current);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return false;
        }

        return false;
    }
}
