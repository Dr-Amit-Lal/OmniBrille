namespace OmniBrille.Core;

/// <summary>Clips a connector outside both glyph envelopes, including endpoint-dot clearance.</summary>
public static class GraphEdgeGeometry
{
    public static bool CrossesBox(double x1, double y1, double x2, double y2, LabelBox box)
        => TryIntersectBox(x1, y1, x2, y2, box, out _, out _);

    public static bool TryIntersectBox(double x1, double y1, double x2, double y2, LabelBox box,
        out double start, out double end)
    {
        var lower = 0d;
        var upper = 1d;
        bool IntersectAxis(double origin, double delta, double minimum, double maximum)
        {
            if (Math.Abs(delta) < 0.000001) { return origin >= minimum && origin <= maximum; }
            var first = (minimum - origin) / delta;
            var second = (maximum - origin) / delta;
            lower = Math.Max(lower, Math.Min(first, second));
            upper = Math.Min(upper, Math.Max(first, second));
            return lower <= upper;
        }
        var intersects = IntersectAxis(x1, x2 - x1, box.X, box.X + box.Width) &&
            IntersectAxis(y1, y2 - y1, box.Y, box.Y + box.Height);
        start = lower;
        end = upper;
        return intersects;
    }

    public static (double X, double Y) PeripheralPoint(
        double centerX, double centerY, double towardX, double towardY,
        double halfWidth, double halfHeight, double clearance = 5)
    {
        var dx = towardX - centerX;
        var dy = towardY - centerY;
        var fraction = Math.Max(Math.Abs(dx) / (halfWidth + clearance), Math.Abs(dy) / (halfHeight + clearance));
        return fraction <= 0 ? (centerX, centerY) : (centerX + dx / fraction, centerY + dy / fraction);
    }
}
