using System.Numerics;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace MemoAI.App;

// This control owns its drawing from template creation. It observes Background
// and BorderBrush without ever replacing the properties controlled by WinUI/VSM.
public sealed class ContinuousSurface : ContentControl
{
    private Border? fill;
    private Grid? strokeHost;
    private ContinuousPaintBrush? paint;
    private ShapeVisual? strokeVisual;
    private CompositionPathGeometry? strokeGeometry;
    private CompositionSpriteShape? stroke;
    private SolidColorBrush? observedStroke;
    private long strokeToken;
    private XamlRoot? observedRoot;
    private double observedScale;
    private (double Width, double Height, CornerRadius Radius, Thickness Thickness, Brush Brush, double Opacity)? paintedStroke;
    public UIElement? Child { get => Content as UIElement; set => Content = value; }
    internal bool HasGeometry => paint?.Ready == true;
    internal object? PaintIdentity => paint;
    internal int GeometryUpdates => paint?.GeometryUpdates ?? 0;
    internal int Connections => paint?.Connections ?? 0;
    public ContinuousSurface()
    {
        Template = (ControlTemplate)Application.Current.Resources["ContinuousSurfaceTemplate"];
        IsTabStop = false; HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        RegisterPropertyChangedCallback(BackgroundProperty, (_, _) => UpdateFill());
        RegisterPropertyChangedCallback(BorderBrushProperty, (_, _) => { paintedStroke = null; ObserveStroke(); UpdateStroke(); });
        RegisterPropertyChangedCallback(BorderThicknessProperty, (_, _) => UpdateStroke());
        RegisterPropertyChangedCallback(CornerRadiusProperty, (_, _) => Refresh());
        SizeChanged += (_, _) => Refresh();
        Loaded += (_, _) => { observedRoot = XamlRoot; observedScale = observedRoot?.RasterizationScale ?? 1; if (observedRoot is not null) observedRoot.Changed += RootChanged; ObserveStroke(); Refresh(); };
        Unloaded += (_, _) => { if (observedRoot is not null) observedRoot.Changed -= RootChanged; observedRoot = null; DetachStroke(); };
    }
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        fill = GetTemplateChild("FillSurface") as Border; strokeHost = GetTemplateChild("StrokeSurface") as Grid;
        strokeVisual?.Dispose(); strokeGeometry?.Dispose(); stroke?.Dispose(); strokeVisual = null; strokeGeometry = null; stroke = null;
        paint = null; paintedStroke = null; UpdateFill(); Refresh();
    }
    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    { if (sender.RasterizationScale != observedScale) { observedScale = sender.RasterizationScale; Refresh(); } }
    private void UpdateFill()
    {
        if (fill is null) return;
        var source = Background ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        // An image needs a new decoder/mask; solid visual-state changes keep the
        // same surface and update its color synchronously before the next frame.
        if (paint is null || source is ImageBrush || paint.Source is ImageBrush)
        { paint = new ContinuousPaintBrush(fill, source, CornerRadius); fill.Background = paint; }
        else { paint.Source = source; paint.Opacity = source.Opacity; }
        paint.Resize(ActualWidth, ActualHeight, CornerRadius);
    }
    private void Refresh() { paint?.Resize(ActualWidth, ActualHeight, CornerRadius); UpdateStroke(); }
    private void DetachStroke()
    {
        if (observedStroke is not null && strokeToken != 0) observedStroke.UnregisterPropertyChangedCallback(SolidColorBrush.ColorProperty, strokeToken);
        observedStroke = null; strokeToken = 0;
    }
    private void ObserveStroke()
    {
        DetachStroke(); if (!IsLoaded) return; observedStroke = BorderBrush as SolidColorBrush;
        if (observedStroke is not null) strokeToken = observedStroke.RegisterPropertyChangedCallback(SolidColorBrush.ColorProperty, (_, _) => { paintedStroke = null; UpdateStroke(); });
    }
    private void UpdateStroke()
    {
        if (strokeHost is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        var t = BorderThickness;
        if (BorderBrush is null || Math.Max(Math.Max(t.Left, t.Right), Math.Max(t.Top, t.Bottom)) <= 0)
        { if (strokeVisual is not null) strokeVisual.Opacity = 0; paintedStroke = null; return; }
        var layout = (ActualWidth, ActualHeight, CornerRadius, t, BorderBrush, BorderBrush.Opacity);
        if (paintedStroke == layout) return;
        paintedStroke = layout;
        var compositor = ElementCompositionPreview.GetElementVisual(strokeHost).Compositor;
        if (strokeVisual is null)
        {
            strokeVisual = compositor.CreateShapeVisual(); strokeVisual.BorderMode = CompositionBorderMode.Soft;
            strokeGeometry = compositor.CreatePathGeometry(); stroke = compositor.CreateSpriteShape(strokeGeometry); strokeVisual.Shapes.Add(stroke);
            ElementCompositionPreview.SetElementChildVisual(strokeHost, strokeVisual);
        }
        strokeVisual.Size = new((float)ActualWidth, (float)ActualHeight);
        using var outer = ContinuousCorners.Outline((float)ActualWidth, (float)ActualHeight, CornerRadius);
        var r = CornerRadius;
        var innerRadius = new CornerRadius(Math.Max(0, r.TopLeft - Math.Max(t.Left, t.Top)), Math.Max(0, r.TopRight - Math.Max(t.Right, t.Top)), Math.Max(0, r.BottomRight - Math.Max(t.Right, t.Bottom)), Math.Max(0, r.BottomLeft - Math.Max(t.Left, t.Bottom)));
        using var inner = ContinuousCorners.Outline((float)Math.Max(0, ActualWidth - t.Left - t.Right), (float)Math.Max(0, ActualHeight - t.Top - t.Bottom), innerRadius);
        using var ring = outer.CombineWith(inner, Matrix3x2.CreateTranslation((float)t.Left, (float)t.Top), CanvasGeometryCombine.Exclude);
        strokeGeometry!.Path = new CompositionPath(ring);
        var old = stroke!.FillBrush; stroke.FillBrush = ContinuousPaintBrush.CompositionColor(compositor, BorderBrush, ActualTheme); old?.Dispose();
        strokeVisual.Opacity = (float)BorderBrush.Opacity;
    }
}
