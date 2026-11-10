using System.Text.Json;
using OmniBrille.Core;

namespace OmniBrille.Infrastructure;

public sealed class JsonSpatialWorkspaceStore : ISpatialWorkspaceStore
{
    public const string FileName = "spatial-workspace.json";
    public const int MaximumFileBytes = 128 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, MaxDepth = 8 };
    private readonly string _directory;
    private readonly string _path;

    public JsonSpatialWorkspaceStore(string? settingsRoot = null)
    {
        _directory = settingsRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniBrille");
        _path = Path.Combine(_directory, FileName);
    }

    public SpatialWorkspace Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                var migrated = MigrateExplicitLocations();
                // Mark even an empty migration once, without overwriting an existing
                // unreadable or future-schema workspace merely because the app opened.
                TrySave(migrated);
                return migrated;
            }
            using var stream = File.OpenRead(_path);
            if (stream.Length > MaximumFileBytes) { return SpatialWorkspace.Empty; }
            var bytes = new byte[MaximumFileBytes + 1];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = stream.Read(bytes, count, bytes.Length - count);
                if (read == 0) { break; }
                count += read;
            }
            if (count > MaximumFileBytes) { return SpatialWorkspace.Empty; }
            return (JsonSerializer.Deserialize<SpatialWorkspace>(bytes.AsSpan(0, count), Options) ?? SpatialWorkspace.Empty).Normalize();
        }
        catch (Exception exception) when (IsRecoverable(exception)) { return SpatialWorkspace.Empty; }
    }

    public bool TrySave(SpatialWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(workspace.Normalize(), Options);
            if (bytes.Length > MaximumFileBytes) { return false; }
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (IsRecoverable(exception)) { return false; }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception exception) when (IsRecoverable(exception)) { }
        }
    }

    private SpatialWorkspace MigrateExplicitLocations()
    {
        var legacy = new JsonFileMapWorkspaceStore(_directory).Load();
        var standalonePins = new JsonPinnedFoldersStore(_directory).Load().Folders;
        // When legacy stores together exceed twelve pins, the newer standalone
        // shortcuts win; the original legacy files are left untouched.
        var pins = standalonePins.Concat(legacy.Pins.Select(pin => new PinnedFolder(pin.Path, pin.Name))).ToArray();
        return new SpatialWorkspace(legacy.Roots, pins).Normalize();
    }

    private static bool IsRecoverable(Exception exception) => exception is
        IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException or NotSupportedException;
}
