using System.Text.Json;
using OmniBrille.Core;

namespace OmniBrille.Infrastructure;

public sealed class JsonPinnedFoldersStore : IPinnedFoldersStore
{
    public const string FileName = "pinned-folders.json";
    public const int MaximumFileBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, MaxDepth = 8 };
    private readonly string _path;

    public JsonPinnedFoldersStore(string? settingsRoot = null)
    {
        _path = Path.Combine(settingsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniBrille"), FileName);
    }

    public PinnedFolders Load()
    {
        try
        {
            using var stream = File.OpenRead(_path);
            if (stream.Length > MaximumFileBytes)
            {
                return PinnedFolders.Empty;
            }

            return (JsonSerializer.Deserialize<PinnedFolders>(stream, Options) ?? PinnedFolders.Empty).Normalize();
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return PinnedFolders.Empty;
        }
    }

    public bool TrySave(PinnedFolders folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(folders.Normalize(), Options);
            if (bytes.Length > MaximumFileBytes)
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
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
                // A locked profile must not stop browsing or replace an existing pin list.
            }
        }
    }

    private static bool IsRecoverable(Exception exception) => exception is
        IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException or NotSupportedException;
}
