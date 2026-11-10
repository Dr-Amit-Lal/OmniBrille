using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace OmniBrille.Desktop.Rendering;

public sealed class PinnedFolderIcon : Control
{
    private bool _reducedEffects;

    public bool ReducedEffects
    {
        get => _reducedEffects;
        set { if (_reducedEffects != value) { _reducedEffects = value; InvalidateVisual(); } }
    }

    private readonly Pen _outline;
    private readonly Pen _highlight = new(new SolidColorBrush(Color.Parse("#B0E3EAF7")), 0.65);
    private readonly Pen _lightOutline = new(new SolidColorBrush(ScenePalette.Light.Node), 1.6);
    private readonly IBrush _fill;
    private readonly IBrush _glow;
    private static readonly IBrush DarkBackground = new SolidColorBrush(ScenePalette.Dark.Background);
    private static readonly IBrush LightBackground = new SolidColorBrush(ScenePalette.Light.Background);

    public PinnedFolderIcon(string path)
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        Width = 34;
        Height = 34;
        IsHitTestVisible = false;
        var color = NebulaFolderGeometry.AccentFor(path);
        _outline = new Pen(new SolidColorBrush(color), 1.6);
        _fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new(Color.FromArgb(135, color.R, color.G, color.B), 0),
                new(Color.FromArgb(35, color.R, color.G, color.B), 0.5),
                new(Color.FromArgb(65, color.R, color.G, color.B), 1),
            },
        };
        _glow = new RadialGradientBrush
        {
            GradientStops = new GradientStops
            {
                new(Color.FromArgb(65, color.R, color.G, color.B), 0),
                new(Color.FromArgb(30, color.R, color.G, color.B), 0.5),
                new(Color.FromArgb(0, color.R, color.G, color.B), 1),
            },
        };
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var light = ActualThemeVariant == ThemeVariant.Light;
        using (context.PushTransform(Matrix.CreateScale(0.52, 0.52) * Matrix.CreateTranslation(Bounds.Width / 2, Bounds.Height / 2)))
        {
            if (!light && !ReducedEffects) { context.DrawEllipse(_glow, null, new Point(0, 0), 34, 29); }
            context.DrawGeometry(light ? LightBackground : DarkBackground, light ? _lightOutline : _outline, NebulaFolderGeometry.Back);
            context.DrawGeometry(light ? LightBackground : DarkBackground, null, NebulaFolderGeometry.Front);
            context.DrawGeometry(light ? LightBackground : _fill, light ? _lightOutline : _outline, NebulaFolderGeometry.Front);
            if (!light && !ReducedEffects) { context.DrawGeometry(null, _highlight, NebulaFolderGeometry.Front); }
        }
    }
}
