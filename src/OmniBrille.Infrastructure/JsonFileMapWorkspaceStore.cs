using System.Text.Json;
using OmniBrille.Core;

namespace OmniBrille.Infrastructure;

public sealed class JsonFileMapWorkspaceStore : IFileMapWorkspaceStore
{
    public const string FileName = "file-map-workspace.json";
    public const int MaximumFileBytes = 256 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, MaxDepth = 12 };
    private readonly string _path;

    public JsonFileMapWorkspaceStore(string? settingsRoot = null)
    {
        _path = Path.Combine(settingsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniBrille"), FileName);
    }

    public FileMapWorkspace Load()
    {
        try
        {
            using var stream = File.OpenRead(_path);
            if (stream.Length > MaximumFileBytes)
            {
                return FileMapWorkspace.Empty;
            }

            return (JsonSerializer.Deserialize<FileMapWorkspace>(stream, Options) ?? FileMapWorkspace.Empty).Normalize();
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return FileMapWorkspace.Empty;
        }
    }

    public bool TrySave(FileMapWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(workspace.Normalize(), Options);
            if (bytes.Length > MaximumFileBytes)
            {
                return false;
            }

            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return false;
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                // A locked profile must not stop browsing.
            }
        }
    }

    private static bool IsRecoverable(Exception exception) => exception is
        IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException or NotSupportedException;
}
