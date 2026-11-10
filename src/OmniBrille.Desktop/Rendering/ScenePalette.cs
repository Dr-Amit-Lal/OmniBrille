using Avalonia.Media;

namespace OmniBrille.Desktop.Rendering;

internal sealed record ScenePalette(
    Color Background,
    Color BackgroundPoint,
    Color Edge,
    Color EdgeGlow,
    Color ContextEdge,
    Color ContextEdgeGlow,
    Color Node,
    Color Context,
    Color Focus,
    Color Search,
    Color Text,
    Color MutedText,
    Color Selection)
{
    public static ScenePalette Dark { get; } = new(
        Color.Parse("#080D17"),
        Color.Parse("#8496BC"),
        Color.Parse("#7288AF"),
        Color.Parse("#8CAFE8"),
        Color.Parse("#52D9FF"),
        Color.Parse("#75F1FF"),
        Color.Parse("#8AB8F5"),
        Color.Parse("#91A7CC"),
        Color.Parse("#EFF4FF"),
        Color.Parse("#B8F5DF"),
        Color.Parse("#E3EAF7"),
        Color.Parse("#99ABC7"),
        Color.Parse("#B2C8FF"));

    // These are presentation accents for existing containment branches, not
    // inferred topics or Context relationships. Keep them bright enough over
    // the maximum atmospheric wash, including MinimumInteractiveOpacity.
    internal static IReadOnlyList<Color> NebulaAccents { get; } = Array.AsReadOnly(new[]
    {
        Color.Parse("#8AB8F5"),
        Color.Parse("#B6A1F4"),
        Color.Parse("#70CEC8"),
        Color.Parse("#E7BB78"),
    });

    internal static Color NebulaWash { get; } = Color.Parse("#142038");

    internal static Color NebulaVioletWash { get; } = Color.Parse("#242338");

    public static ScenePalette Light { get; } = new(
        Color.Parse("#EAF5FF"),
        Color.Parse("#84B5D6"),
        Color.Parse("#4D8FC5"),
        Color.Parse("#008DE3"),
        Color.Parse("#005A78"),
        Color.Parse("#00A7D8"),
        Color.Parse("#0B62A5"),
        Color.Parse("#315F82"),
        Color.Parse("#004B91"),
        Color.Parse("#006D86"),
        Color.Parse("#082B4C"),
        Color.Parse("#527897"),
        Color.Parse("#0079D8"));
}
