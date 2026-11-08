using Avalonia.Media;

namespace OmniBrille.Desktop.Rendering;

internal static class NebulaFolderGeometry
{
    internal static readonly Geometry Back = Geometry.Parse("M -24,15 L -24,-15 Q -24,-19 -20,-19 L -9,-19 Q -7,-19 -5,-16 L -2,-12 L 18,-12 Q 22,-12 22,-8 L 22,15 Z");
    internal static readonly Geometry Front = Geometry.Parse("M -24,15 L -19,-5 Q -18,-8 -14,-8 L 23,-8 Q 27,-8 26,-4 L 21,15 Q 20,18 16,18 L -20,18 Q -24,18 -24,15 Z");

    internal static Color AccentFor(string identity)
    {
        var hash = 2166136261u;
        foreach (var character in identity) { hash = unchecked((hash ^ character) * 16777619u); }
        return ScenePalette.NebulaAccents[(int)(hash % (uint)ScenePalette.NebulaAccents.Count)];
    }
}
