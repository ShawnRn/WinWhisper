using System.Numerics;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Xaml;

namespace MemoAI.App;

// Zero endpoint curvature and edge-aligned tangents give G2 joins.
internal static class ContinuousCorners
{
    internal static Vector2[] CornerControls(float width, float extent)
    {
        var r = extent / 1.5f; var d = r * (1 - MathF.Sqrt(.5f));
        // Two symmetric cubics join at 45 degrees with matching curvature.
        // Their outer endpoints have horizontal/vertical tangents and zero
        // curvature. A shorter approach leaves large-radius tiles rounder.
        var a = extent - 2 * d - 1.5f * MathF.Sqrt(2) * d * d / Math.Max(.0001f, r);
        return [new(width - extent, 0), new(width - extent + a, 0), new(width - 2 * d, 0), new(width - d, d), new(width, 2 * d), new(width, extent - a), new(width, extent)];
    }
    internal static float CornerExtent(double radius, float limit) => Math.Clamp((float)radius * 1.5f, 0, limit);
    internal static CanvasGeometry Outline(float width, float height, CornerRadius radius, float inset = 0)
    {
        width = Math.Max(0, width - inset * 2); height = Math.Max(0, height - inset * 2);
        var limit = Math.Min(width, height) / 2;
        float R(double r) => CornerExtent(Math.Max(0, r - inset), limit);
        var tl = R(radius.TopLeft); var tr = R(radius.TopRight); var br = R(radius.BottomRight); var bl = R(radius.BottomLeft);
        using var path = new CanvasPathBuilder(null);
        path.BeginFigure(new Vector2(inset + tl, inset));
        path.AddLine(new Vector2(inset + width - tr, inset));
        void AddCorner(float extent, Vector2 origin, int quarter)
        {
            var p = CornerControls(0, extent);
            Vector2 T(Vector2 v) => (quarter switch { 1 => new Vector2(-v.Y, v.X), 2 => -v, 3 => new Vector2(v.Y, -v.X), _ => v }) + origin;
            path.AddCubicBezier(T(p[1]), T(p[2]), T(p[3])); path.AddCubicBezier(T(p[4]), T(p[5]), T(p[6]));
        }
        AddCorner(tr, new(inset + width, inset), 0);
        path.AddLine(new Vector2(inset + width, inset + height - br));
        AddCorner(br, new(inset + width, inset + height), 1);
        path.AddLine(new Vector2(inset + bl, inset + height));
        AddCorner(bl, new(inset, inset + height), 2);
        path.AddLine(new Vector2(inset, inset + tl));
        AddCorner(tl, new(inset, inset), 3);
        path.EndFigure(CanvasFigureLoop.Closed);
        return CanvasGeometry.CreatePath(path);
    }
}
