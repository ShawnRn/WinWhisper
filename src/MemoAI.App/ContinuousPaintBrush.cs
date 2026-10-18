using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoAI.App;

// Paint a G2 silhouette into an owned composition surface. Never replace the Clip
// of a XAML-owned visual: WinUI expects that clip to remain an InsetClip.
public sealed class ContinuousPaintBrush : XamlCompositionBrushBase
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(Brush), typeof(ContinuousPaintBrush), new PropertyMetadata(null, (d, _) => ((ContinuousPaintBrush)d).SourceUpdated()));
    public Brush? Source { get => (Brush?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    private FrameworkElement owner = null!;
    private bool transparent;
    private CornerRadius radius;
    private ShapeVisual? shapeVisual;
    private CompositionSpriteShape? shape;
    private CompositionPathGeometry? geometry;
    private CompositionVisualSurface? surface;
    private LoadedImageSurface? image;
    private CompositionSurfaceBrush? imageBrush, surfaceBrush;
    private CompositionMaskBrush? mask;
    private double width, height;
    private long colorToken;
    private SolidColorBrush? observedColor;
    private bool connected;
    private (double Width, double Height, CornerRadius Radius, float Scale)? paintedLayout;
    internal int GeometryUpdates { get; private set; }
    internal int Connections { get; private set; }
    internal float RasterScale { get; private set; } = 1;
    internal event Action? SourceChanged;
    internal bool Ready => CompositionBrush is not null;
    public ContinuousPaintBrush() { }
    public ContinuousPaintBrush(FrameworkElement owner, Brush source, CornerRadius radius, bool transparent = false)
    { this.owner = owner; Source = source; this.radius = radius; this.transparent = transparent; Opacity = source.Opacity; }
    protected override void OnConnected()
    {
        connected = true;
        Connections++; paintedLayout = null;
        var compositor = ElementCompositionPreview.GetElementVisual(owner).Compositor;
        if (transparent) { CompositionBrush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)); ObserveColor(); return; }
        geometry = compositor.CreatePathGeometry(); shape = compositor.CreateSpriteShape(geometry);
        shapeVisual = compositor.CreateShapeVisual(); shapeVisual.BorderMode = CompositionBorderMode.Soft; shapeVisual.Shapes.Add(shape);
        surface = compositor.CreateVisualSurface(); surface.SourceVisual = shapeVisual;
        surfaceBrush = compositor.CreateSurfaceBrush(surface); surfaceBrush.Stretch = CompositionStretch.Fill; surfaceBrush.BitmapInterpolationMode = CompositionBitmapInterpolationMode.Linear;
        if (Source is ImageBrush { ImageSource: BitmapImage { UriSource: { } uri } })
        {
            shape.FillBrush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
            image = LoadedImageSurface.StartLoadFromUri(uri);
            imageBrush = compositor.CreateSurfaceBrush(image); imageBrush.Stretch = CompositionStretch.UniformToFill; imageBrush.BitmapInterpolationMode = CompositionBitmapInterpolationMode.Linear;
            mask = compositor.CreateMaskBrush(); mask.Source = imageBrush; mask.Mask = surfaceBrush; CompositionBrush = mask;
        }
        else { UpdateColor(); CompositionBrush = surfaceBrush; }
        ObserveColor();
        Resize(width, height, radius);
    }
    internal void Resize(double width, double height, CornerRadius radius)
    {
        this.width = width; this.height = height; this.radius = radius;
        if (width <= 0 || height <= 0 || geometry is null || shapeVisual is null || surface is null) return;
        // Composition surface sizes are pixels, while XAML sizes are DIPs. An
        // unparented visual also inherits hard (aliased) borders unless set explicitly.
        // Supersample small masks; large backgrounds stay at display resolution.
        RasterScale = (float)(owner.XamlRoot?.RasterizationScale ?? 1) * (width * height <= 262144 ? 2 : 1);
        var layout = (width, height, radius, RasterScale);
        if (paintedLayout == layout) return;
        paintedLayout = layout; GeometryUpdates++;
        var size = new Vector2((float)width, (float)height); shapeVisual.Size = size * RasterScale; surface.SourceSize = size * RasterScale;
        using var outline = ContinuousCorners.Outline(size.X, size.Y, radius);
        using var pixels = outline.Transform(Matrix3x2.CreateScale(RasterScale));
        geometry.Path = new CompositionPath(pixels);
    }
    private void UpdateColor()
    {
        if (shape is null || shapeVisual is null) return;
        if (shape.FillBrush is CompositionColorBrush existing && Source is SolidColorBrush solid)
        { if (existing.Color != solid.Color) existing.Color = solid.Color; return; }
        var previous = shape.FillBrush; shape.FillBrush = CompositionColor(shapeVisual.Compositor, Source, owner.ActualTheme); previous?.Dispose();
    }
    private void SourceUpdated() { if (connected) ObserveColor(); UpdateColor(); SourceChanged?.Invoke(); }
    private void ObserveColor()
    {
        if (observedColor is not null && colorToken != 0) observedColor.UnregisterPropertyChangedCallback(SolidColorBrush.ColorProperty, colorToken);
        observedColor = Source as SolidColorBrush; colorToken = observedColor?.RegisterPropertyChangedCallback(SolidColorBrush.ColorProperty, (_, _) => { UpdateColor(); SourceChanged?.Invoke(); }) ?? 0;
    }
    internal static CompositionBrush CompositionColor(Compositor compositor, Brush? source, ElementTheme theme)
    {
        if (source is SolidColorBrush color) return compositor.CreateColorBrush(color.Color);
        if (source is LinearGradientBrush gradient)
        {
            var brush = compositor.CreateLinearGradientBrush(); brush.StartPoint = new((float)gradient.StartPoint.X, (float)gradient.StartPoint.Y); brush.EndPoint = new((float)gradient.EndPoint.X, (float)gradient.EndPoint.Y);
            foreach (var stop in gradient.GradientStops) brush.ColorStops.Add(compositor.CreateColorGradientStop((float)stop.Offset, stop.Color));
            return brush;
        }
        if (source is AcrylicBrush acrylic) return compositor.CreateColorBrush(acrylic.FallbackColor);
        return compositor.CreateColorBrush(theme == ElementTheme.Light ? Windows.UI.Color.FromArgb(255, 249, 249, 249) : Windows.UI.Color.FromArgb(255, 44, 44, 44));
    }
    protected override void OnDisconnected()
    {
        connected = false;
        if (observedColor is not null && colorToken != 0) observedColor.UnregisterPropertyChangedCallback(SolidColorBrush.ColorProperty, colorToken);
        observedColor = null;
        colorToken = 0; var brush = CompositionBrush; CompositionBrush = null; brush?.Dispose();
        imageBrush?.Dispose(); image?.Dispose(); surfaceBrush?.Dispose(); surface?.Dispose(); shape?.Dispose(); geometry?.Dispose(); shapeVisual?.Dispose();
        imageBrush = null; image = null; surfaceBrush = null; surface = null; shape = null; geometry = null; shapeVisual = null; mask = null;
    }
}
