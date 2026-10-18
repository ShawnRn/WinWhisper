using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace MemoAI.App;

public sealed class TranscriptDiffView : ContentControl
{
    public static readonly DependencyProperty DiffProperty = DependencyProperty.Register(nameof(Diff), typeof(TranscriptDiff), typeof(TranscriptDiffView), new PropertyMetadata(null, (d, _) => ((TranscriptDiffView)d).Render()));
    public TranscriptDiff? Diff { get => (TranscriptDiff?)GetValue(DiffProperty); set => SetValue(DiffProperty, value); }
    public TranscriptDiffView()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch; IsTabStop = false;
        ActualThemeChanged += (_, _) => Render();
    }
    private void Render()
    {
        if (Diff is null) { Content = null; return; }
        var comparison = TranscriptTextDiff.Compare(Diff.Before, Diff.After);
        var rows = new StackPanel { Spacing = 5 };
        if (Diff.Context.Length > 0) rows.Children.Add(new TextBlock { Text = Diff.Context, FontSize = 14, LineHeight = 23, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        void Row(IReadOnlyList<TranscriptTextPart> parts, bool removed)
        {
            if (parts.Count == 0) return;
            var rich = new RichTextBlock { FontSize = 14, LineHeight = 23, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = (FontFamily)Application.Current.Resources["ContentControlThemeFontFamily"] };
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run { Text = removed ? "− " : "+ ", Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
            foreach (var part in parts)
            {
                var run = new Run { Text = part.Text };
                if (part.Changed)
                {
                    run.Foreground = (Brush)Application.Current.Resources[removed ? "SystemFillColorCriticalBrush" : "SystemFillColorSuccessBrush"];
                    if (removed) run.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
                    else run.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                }
                paragraph.Inlines.Add(run);
            }
            rich.Blocks.Add(paragraph); rows.Children.Add(rich);
        }
        Row(comparison.Before, true); Row(comparison.After, false); Content = rows;
    }
}
