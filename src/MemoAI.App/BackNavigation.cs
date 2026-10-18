using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private sealed record NavigationEntry(UIElement Page, MediaSession? Session);
    private readonly Stack<NavigationEntry> navigationHistory = [];
    private UIElement? navigationPage;
    private MediaSession? navigationSession;
    private bool navigatingBack;
    private void RecordNavigation(UIElement page)
    {
        if (navigationPage != page || page == WorkspaceView && navigationSession?.Id != current?.Id)
        {
            if (!navigatingBack && navigationPage is not null) navigationHistory.Push(new(navigationPage, navigationSession));
            navigationPage = page; navigationSession = page == WorkspaceView ? current : null;
        }
        WorkspaceBack.IsEnabled = navigationHistory.Count > 0;
    }
    private async void Back_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        if (navigatingBack) return;
        navigatingBack = true;
        try
        {
            while (navigationHistory.TryPop(out var entry))
            {
                if (entry.Page == WorkspaceView)
                {
                    if (entry.Session is null || !sessions.Any(s => s.Id == entry.Session.Id)) continue;
                    if (current?.Id == entry.Session.Id) ShowPage(WorkspaceView); else await OpenSessionAsync(entry.Session);
                }
                else ShowPage(entry.Page);
                break;
            }
            WorkspaceBack.IsEnabled = navigationHistory.Count > 0;
        }
        finally { navigatingBack = false; }
    });
    private void Root_BackPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(Root).Properties.IsXButton1Pressed && navigationHistory.Count > 0)
        { e.Handled = true; Back_Click(this, new()); }
    }
}
