using System.Text.Json;
using OmniBrille.Core;
using OmniBrille.Infrastructure;

namespace OmniBrille.Tests;

public sealed class VisualPreferencesStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsReducedSettingsAndTheme()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonVisualPreferencesStore(directory.Path);
        var expected = new VisualPreferences("Light", ReducedMotion: true, ReducedEffects: true);
        store.Save(expected);
        Assert.Equal(expected, store.Load());
    }

    [Fact]
    public void Load_LegacyPreferencesKeepVisualSettingsAndRemoveRetiredValues()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "visual-preferences.json");
        File.WriteAllText(settingsPath,
            """
            {
              "Theme": "Light",
              "ReducedMotion": true,
              "ReducedEffects": true,
              "DiagnosticsVisible": true,
              "SoundEnabled": true,
              "VoiceEnabled": true,
              "VoiceLanguage": "auto",
              "VoiceRuntimePath": "C:\\private\\whisper-cli.exe",
              "VoiceModelPath": "C:\\private\\model.bin"
            }
            """);
        var preferences = new JsonVisualPreferencesStore(directory.Path).Load();
        Assert.Equal(new VisualPreferences("Light", true, true), preferences);
        using var migrated = JsonDocument.Parse(File.ReadAllText(settingsPath));
        Assert.Collection(migrated.RootElement.EnumerateObject(),
            property => Assert.Equal("Theme", property.Name),
            property => Assert.Equal("ReducedMotion", property.Name),
            property => Assert.Equal("ReducedEffects", property.Name));
        Assert.DoesNotContain("private", migrated.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("light", "Light")]
    [InlineData("invalid", "Dark")]
    public void Save_NormalizesTheme(string theme, string expected)
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonVisualPreferencesStore(directory.Path);
        store.Save(new VisualPreferences(theme));
        Assert.Equal(expected, store.Load().Theme);
    }

    [Fact]
    public void Load_UnknownPropertiesAreTolerated()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "visual-preferences.json"),
            """{"Theme":"Light","FutureSetting":{"value":true}}""");
        Assert.Equal(new VisualPreferences("Light"), new JsonVisualPreferencesStore(directory.Path).Load());
    }

    [Fact]
    public void Load_MalformedFileFallsBackToSafeDefaults()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "visual-preferences.json"), "not-json");
        Assert.Equal(new VisualPreferences(), new JsonVisualPreferencesStore(directory.Path).Load());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"OmniBrillePreferenceTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
