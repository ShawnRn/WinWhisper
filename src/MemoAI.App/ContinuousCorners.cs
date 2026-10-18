using System.Numerics;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Xaml;

namespace MemoAI.App;

// Zero endpoint curvature and edge-aligned tangents give G2 joins.
internal static class ContinuousCorners
{
    internal static Vector2[] CornerControls(float width, float radius) => [new(width - radius, 0), new(width, 0), new(width, 0), new(width, radius)];
    // Match a circular corner's diagonal inset while extending the zero-curvature
    // approach along each edge. Using the unscaled radius makes small corners look square.
    internal static float CornerExtent(double radius, float limit) => Math.Clamp((float)radius * (8 * (1 - MathF.Sqrt(.5f))), 0, limit);
    internal static CanvasGeometry Outline(float width, float height, CornerRadius radius, float inset = 0)
    {
        width = Math.Max(0, width - inset * 2); height = Math.Max(0, height - inset * 2);
        var limit = Math.Min(width, height) / 2;
        float R(double r) => CornerExtent(Math.Max(0, r - inset), limit);
        var tl = R(radius.TopLeft); var tr = R(radius.TopRight); var br = R(radius.BottomRight); var bl = R(radius.BottomLeft);
        using var path = new CanvasPathBuilder(null);
        path.BeginFigure(new Vector2(inset + tl, inset));
        path.AddLine(new Vector2(inset + width - tr, inset));
        var corner = CornerControls(width, tr); var origin = new Vector2(inset, inset);
        path.AddCubicBezier(corner[1] + origin, corner[2] + origin, corner[3] + origin);
        path.AddLine(new Vector2(inset + width, inset + height - br));
        path.AddCubicBezier(new(inset + width, inset + height), new(inset + width, inset + height), new(inset + width - br, inset + height));
        path.AddLine(new Vector2(inset + bl, inset + height));
        path.AddCubicBezier(new(inset, inset + height), new(inset, inset + height), new(inset, inset + height - bl));
        path.AddLine(new Vector2(inset, inset + tl));
        path.AddCubicBezier(new(inset, inset), new(inset, inset), new(inset + tl, inset));
        path.EndFigure(CanvasFigureLoop.Closed);
        return CanvasGeometry.CreatePath(path);
    }
}
