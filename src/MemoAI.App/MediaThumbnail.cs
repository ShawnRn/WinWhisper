using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoAI.App;

public sealed class MediaThumbnail : ContentControl
{
    private static readonly SemaphoreSlim decoders = new(2, 2);
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(nameof(Session), typeof(MediaSession), typeof(MediaThumbnail), new PropertyMetadata(null, (d, _) => ((MediaThumbnail)d).Reset()));
    public MediaSession? Session { get => (MediaSession?)GetValue(SessionProperty); set => SetValue(SessionProperty, value); }
    private readonly Image image = new() { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
    private readonly FontIcon placeholder = new() { Glyph = "\uE8B2", FontSize = 30 };
    private CancellationTokenSource? loadCancellation;
    private AppPaths? loadedPaths;
    public MediaThumbnail()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch; IsTabStop = false;
        var grid = new Grid(); grid.Children.Add(placeholder); grid.Children.Add(image);
        Content = new ContinuousSurface { CornerRadius = new(8), Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"], Child = grid };
        Unloaded += (_, _) => loadCancellation?.Cancel();
    }
    private void Reset()
    {
        ((ContinuousSurface)Content).Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        loadCancellation?.Cancel(); image.Source = null; image.Visibility = Visibility.Collapsed; placeholder.Visibility = Visibility.Visible;
        var extension = Path.GetExtension(Session?.MediaPath ?? "").ToLowerInvariant();
        placeholder.Glyph = extension is ".srt" or ".vtt" or ".ass" or ".txt" or "" ? "\uE8A5" : extension is ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg" or ".aac" ? "\uE8D6" : "\uE8B2";
        if (IsLoaded && loadedPaths is not null) _ = LoadAsync(loadedPaths);
    }
    public async Task LoadAsync(AppPaths paths)
    {
        loadedPaths = paths; var media = Session;
        if (media is null || !File.Exists(media.MediaPath) || placeholder.Glyph != "\uE8B2") return;
        loadCancellation?.Cancel(); using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20)); loadCancellation = cancellation;
        var acquired = false; string? pending = null;
        try
        {
            var cached = await Task.Run(() =>
            {
                var info = new FileInfo(media.MediaPath);
                var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}")))[..16];
                return Path.Combine(paths.SessionDirectory(media.Id), $"thumbnail-v1-{key}.jpg");
            }, cancellation.Token);
            await decoders.WaitAsync(cancellation.Token); acquired = true;
            if (!File.Exists(cached))
            {
                pending = Path.Combine(Path.GetDirectoryName(cached)!, Guid.NewGuid().ToString("N") + ".pending.jpg");
                var position = Math.Min(1, Math.Max(0, media.Duration * .1)).ToString(CultureInfo.InvariantCulture);
                var result = await Processes.RunAsync(new MediaTools(paths).Ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-ss", position, "-i", media.MediaPath, "-frames:v", "1", "-vf", "scale=480:-2", "-q:v", "4", pending], ct: cancellation.Token);
                if (result.ExitCode != 0 || !File.Exists(pending)) return;
                File.Move(pending, cached, true);
            }
            if (!cancellation.IsCancellationRequested && Session == media && IsLoaded)
            { image.Source = new BitmapImage(new Uri(cached)) { DecodePixelWidth = 480 }; image.Visibility = Visibility.Collapsed; placeholder.Visibility = Visibility.Collapsed; ((ContinuousSurface)Content).Background = new ImageBrush { ImageSource = image.Source, Stretch = Stretch.UniformToFill }; }
        }
        catch (Exception) { /* Keep a useful file-type placeholder when a file or decoder is unavailable. */ }
        finally
        {
            if (acquired) decoders.Release();
            if (pending is not null && File.Exists(pending)) { try { File.Delete(pending); } catch (IOException) { } }
            if (loadCancellation == cancellation) loadCancellation = null;
        }
    }
}
