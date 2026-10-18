using System.Numerics;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace MemoAI.App;

// ComboBox requires Popup > Border > ScrollViewer > ItemsPresenter. Paint the
// original Border without inserting a visual into that native contract.
public sealed class ContinuousPopupBrush : XamlCompositionBrushBase
{
    public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register(nameof(Owner), typeof(FrameworkElement), typeof(ContinuousPopupBrush), new PropertyMetadata(null, (d, e) => ((ContinuousPopupBrush)d).OwnerChanged(e)));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(ContinuousPopupBrush), new PropertyMetadata(null, (d, _) => ((ContinuousPopupBrush)d).Draw()));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(ContinuousPopupBrush), new PropertyMetadata(null, (d, _) => ((ContinuousPopupBrush)d).Draw()));
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(nameof(Radius), typeof(CornerRadius), typeof(ContinuousPopupBrush), new PropertyMetadata(new CornerRadius(8), (d, _) => ((ContinuousPopupBrush)d).Draw()));
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(nameof(Thickness), typeof(Thickness), typeof(ContinuousPopupBrush), new PropertyMetadata(new Thickness(1), (d, _) => ((ContinuousPopupBrush)d).Draw()));
    public FrameworkElement? Owner { get => (FrameworkElement?)GetValue(OwnerProperty); set => SetValue(OwnerProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public CornerRadius Radius { get => (CornerRadius)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public Thickness Thickness { get => (Thickness)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    private ShapeVisual? visual;
    private CompositionVisualSurface? surface;
    private CompositionSurfaceBrush? brush;
    private CompositionPathGeometry? fillGeometry, strokeGeometry;
    private CompositionSpriteShape? fillShape, strokeShape;
    private bool connected;
    private void OwnerChanged(DependencyPropertyChangedEventArgs args)
    {
        if (args.OldValue is FrameworkElement old) { old.SizeChanged -= SizeChanged; old.Loaded -= OwnerLoaded; }
        if (args.NewValue is FrameworkElement owner) { owner.SizeChanged += SizeChanged; owner.Loaded += OwnerLoaded; }
        if (connected && visual is null) Connect();
        Draw();
    }
    private void SizeChanged(object sender, SizeChangedEventArgs args) => Draw();
    private void OwnerLoaded(object sender, RoutedEventArgs args) => Draw();
    protected override void OnConnected() { connected = true; Connect(); }
    private void Connect()
    {
        if (Owner is null || visual is not null) return;
        var compositor = ElementCompositionPreview.GetElementVisual(Owner).Compositor;
        visual = compositor.CreateShapeVisual(); visual.BorderMode = CompositionBorderMode.Soft;
        fillGeometry = compositor.CreatePathGeometry(); strokeGeometry = compositor.CreatePathGeometry();
        fillShape = compositor.CreateSpriteShape(fillGeometry); strokeShape = compositor.CreateSpriteShape(strokeGeometry);
        visual.Shapes.Add(fillShape); visual.Shapes.Add(strokeShape);
        surface = compositor.CreateVisualSurface(); surface.SourceVisual = visual;
        brush = compositor.CreateSurfaceBrush(surface); brush.Stretch = CompositionStretch.Fill; brush.BitmapInterpolationMode = CompositionBitmapInterpolationMode.Linear;
        CompositionBrush = brush; Draw();
    }
    private void Draw()
    {
        if (Owner is not { ActualWidth: > 0, ActualHeight: > 0 } owner || visual is null) return;
        var width = (float)owner.ActualWidth; var height = (float)owner.ActualHeight;
        var scale = (float)(owner.XamlRoot?.RasterizationScale ?? 1) * 2;
        visual.Size = new(width * scale, height * scale); surface!.SourceSize = visual.Size;
        using var outer = ContinuousCorners.Outline(width, height, Radius);
        using var scaled = outer.Transform(Matrix3x2.CreateScale(scale)); fillGeometry!.Path = new(scaled);
        var t = Thickness; var r = Radius;
        var innerRadius = new CornerRadius(Math.Max(0, r.TopLeft - Math.Max(t.Left, t.Top)), Math.Max(0, r.TopRight - Math.Max(t.Right, t.Top)), Math.Max(0, r.BottomRight - Math.Max(t.Right, t.Bottom)), Math.Max(0, r.BottomLeft - Math.Max(t.Left, t.Bottom)));
        using var inner = ContinuousCorners.Outline((float)Math.Max(0, width - t.Left - t.Right), (float)Math.Max(0, height - t.Top - t.Bottom), innerRadius);
        using var ring = outer.CombineWith(inner, Matrix3x2.CreateTranslation((float)t.Left, (float)t.Top), CanvasGeometryCombine.Exclude);
        using var scaledRing = ring.Transform(Matrix3x2.CreateScale(scale)); strokeGeometry!.Path = new(scaledRing);
        var compositor = visual.Compositor;
        var oldFill = fillShape!.FillBrush; fillShape.FillBrush = ContinuousPaintBrush.CompositionColor(compositor, Fill, owner.ActualTheme); oldFill?.Dispose();
        var oldStroke = strokeShape!.FillBrush; strokeShape.FillBrush = ContinuousPaintBrush.CompositionColor(compositor, Stroke, owner.ActualTheme); oldStroke?.Dispose();
    }
    protected override void OnDisconnected()
    {
        connected = false; CompositionBrush = null;
        brush?.Dispose(); surface?.Dispose(); fillShape?.Dispose(); strokeShape?.Dispose(); fillGeometry?.Dispose(); strokeGeometry?.Dispose(); visual?.Dispose();
        brush = null; surface = null; fillShape = strokeShape = null; fillGeometry = strokeGeometry = null; visual = null;
    }
}
