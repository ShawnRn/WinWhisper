using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MemoAI.App;

internal sealed class FloatingSubtitleWindow : Window
{
    private readonly TextBlock text = new() { FontSize = 20, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    public FloatingSubtitleWindow()
    {
        Title = "MemoAI · 悬浮字幕";
        Content = new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(240, 20, 23, 31)), Padding = new Thickness(20), Child = text };
        AppWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay); AppWindow.Resize(new Windows.Graphics.SizeInt32(700, 180));
    }
    public void SetSubtitle(string value) { if (text.Text != value) text.Text = value; }
}
