using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private double videoAspect = 16d / 9;
    private bool arrangingWorkspace;
    private string mediaLayout = "stacked";
    private void MediaWorkspace_SizeChanged(object sender, SizeChangedEventArgs e) => ArrangeWorkspace();
    private void ArrangeWorkspace()
    {
        if (arrangingWorkspace || MediaWorkspace.ActualWidth < 1) return;
        arrangingWorkspace = true;
        try
        {
            var width = MediaWorkspace.ActualWidth; var height = MediaWorkspace.ActualHeight;
            var wide = width >= 760; var medium = !wide && width >= 620 && height >= 500;
            mediaLayout = wide ? "columns" : medium ? "compact" : "stacked";
            VideoColumn.Width = wide ? new(Math.Clamp(width * .44, 320, 420)) : medium ? new(.56, GridUnitType.Star) : new(1, GridUnitType.Star);
            TranscriptColumn.Width = wide || medium ? new(1, GridUnitType.Star) : new(0);
            MediaTopRow.Height = GridLength.Auto; MediaMiddleRow.Height = new(1, GridUnitType.Star); MediaBottomRow.Height = wide || medium ? new(0) : GridLength.Auto;
            Grid.SetColumn(VideoPane, 0); Grid.SetRow(VideoPane, 0); Grid.SetColumnSpan(VideoPane, wide || medium ? 1 : 2);
            Grid.SetColumn(TranscriptPane, wide ? 1 : 0); Grid.SetRow(TranscriptPane, wide ? 0 : 1); Grid.SetColumnSpan(TranscriptPane, wide ? 1 : 2); Grid.SetRowSpan(TranscriptPane, wide ? 3 : 1);
            Grid.SetColumn(SubtitleEditorPane, medium ? 1 : 0); Grid.SetRow(SubtitleEditorPane, wide ? 1 : medium ? 0 : 2); Grid.SetColumnSpan(SubtitleEditorPane, wide || medium ? 1 : 2);
            var columnWidth = wide ? VideoColumn.Width.Value : medium ? (width - MediaWorkspace.ColumnSpacing) * .56 : width;
            var maxHeight = Math.Clamp(height * (wide ? .50 : .37), 160, 320);
            VideoPane.Width = Math.Max(120, Math.Min(columnWidth, maxHeight * videoAspect));
            VideoPane.HorizontalAlignment = wide || medium ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            VideoSurface.Height = VideoPane.Width / videoAspect;
        }
        finally { arrangingWorkspace = false; }
    }
}
