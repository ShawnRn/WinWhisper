using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private void LibraryMode_Click(object sender, RoutedEventArgs e)
    {
        if (settings is null || sender is not FrameworkElement button) return;
        var selectedMedia = (LibraryGrid.Visibility == Visibility.Visible ? LibraryGrid.SelectedItems : LibraryList.SelectedItems).OfType<MediaSession>().ToArray();
        var previous = settings.LibraryViewMode;
        settings.LibraryViewMode = button.Tag as string == "Grid" ? "Grid" : "List";
        ApplyLibraryView();
        var view = settings.LibraryViewMode == "Grid" ? (UIElement)LibraryGrid : LibraryList;
        if (previous != settings.LibraryViewMode) { ResetPageMotion(view); AnimatePage(view, true); }
        var selection = LibraryGrid.Visibility == Visibility.Visible ? LibraryGrid.SelectedItems : LibraryList.SelectedItems;
        selection.Clear(); foreach (var media in selectedMedia) selection.Add(media);
        store.SaveSettings(settings);
    }
    private void ApplyLibraryView()
    {
        var grid = settings.LibraryViewMode == "Grid";
        LibraryGrid.Visibility = grid ? Visibility.Visible : Visibility.Collapsed; LibraryList.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
        LibraryGridMode.IsChecked = grid; LibraryListMode.IsChecked = !grid;
    }
    private void LibraryGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (LibraryGrid.ItemsPanelRoot is ItemsWrapGrid panel && e.NewSize.Width > 0)
        { var columns = Math.Max(1, (int)(e.NewSize.Width / 260)); panel.ItemWidth = Math.Max(180, (e.NewSize.Width - 24) / columns); panel.ItemHeight = (panel.ItemWidth - 28) * 9 / 16 + 112; }
    }
    private void GridThumbnail_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not MediaThumbnail cover || e.NewSize.Width <= 0) return;
        var height = e.NewSize.Width * 9 / 16;
        if (double.IsNaN(cover.Height) || Math.Abs(cover.Height - height) > .1) cover.Height = height;
    }
    private async void Thumbnail_Loaded(object sender, RoutedEventArgs e)
    { if (sender is MediaThumbnail thumbnail) await thumbnail.LoadAsync(paths); }
}
