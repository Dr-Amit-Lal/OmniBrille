using System.Diagnostics;
using OmniBrille.Core;

namespace OmniBrille.Infrastructure;

/// <summary>Launches local files after a last-moment authority and type check.</summary>
public sealed class ShellFileActivationService : IFileActivationService
{
    private static readonly HashSet<string> HighRiskExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".application", ".appref-ms", ".bat", ".chm", ".cmd", ".com", ".cpl", ".diagcab",
        ".exe", ".gadget", ".hta", ".inf", ".ins", ".isp", ".jar", ".js", ".jse", ".lnk",
        ".msc", ".msi", ".msix", ".msp", ".mst", ".pif", ".ps1", ".reg", ".scf", ".scr",
        ".sct", ".shb", ".shs", ".url", ".vb", ".vbe", ".vbs", ".vxd", ".website", ".wsf",
        ".wsh", ".xll",
    };

    public Task<FileActivationResult> OpenAsync(
        string accessRoot,
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => OpenFile(accessRoot, path, cancellationToken), cancellationToken);
    }

    private static FileActivationResult OpenFile(string accessRoot, string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!Path.IsPathFullyQualified(accessRoot) ||
                !Path.IsPathFullyQualified(path) ||
                !PathBoundary.IsWithin(accessRoot, path))
            {
                return FileActivationResult.OutsideAccessRoot;
            }

            var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(accessRoot));
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                return FileActivationResult.NotFound;
            }

            var info = new FileInfo(fullPath);
            if (!FileMapPathGuard.IsOrdinaryPath(fullRoot, fullPath, requireDirectory: false))
            {
                return FileActivationResult.ReparsePointBlocked;
            }

            if (HighRiskExtensions.Contains(info.Extension))
            {
                return FileActivationResult.HighRiskTypeBlocked;
            }

            // An explicit Open action may let the sync provider hydrate a cloud file.
            // Enumeration, search and classification never open its contents.
            cancellationToken.ThrowIfCancellationRequested();
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true,
                WorkingDirectory = info.DirectoryName ?? accessRoot,
            });
            return FileActivationResult.Opened;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or System.ComponentModel.Win32Exception or
                                          InvalidOperationException or NotSupportedException)
        {
            return FileActivationResult.Failed;
        }
    }
}
