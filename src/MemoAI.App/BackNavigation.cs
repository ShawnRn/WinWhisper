using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private sealed record NavigationEntry(UIElement Page, MediaSession? Session);
    private readonly Stack<NavigationEntry> navigationHistory = [];
    private readonly Stack<NavigationEntry> forwardHistory = [];
    private UIElement? navigationPage;
    private MediaSession? navigationSession;
    private bool navigatingHistory;
    private int navigationDirection;
    private int navigationAnimationCount;
    private readonly Windows.UI.ViewManagement.UISettings motionSettings = new();
    private bool RecordNavigation(UIElement page)
    {
        var changed = navigationPage != page || page == WorkspaceView && navigationSession?.Id != current?.Id;
        if (changed)
        {
            if (!navigatingHistory && navigationPage is not null)
            { navigationHistory.Push(new(navigationPage, navigationSession)); forwardHistory.Clear(); }
            navigationPage = page; navigationSession = page == WorkspaceView ? current : null;
        }
        UpdateHistoryButtons();
        return changed;
    }
    private void UpdateHistoryButtons()
    {
        NavigationBack.IsEnabled = navigationHistory.Count > 0;
    }
    private async void Back_Click(object sender, RoutedEventArgs e) => await SafeAsync(() => NavigateHistoryAsync(false));
    private async void Forward_Click(object sender, RoutedEventArgs e) => await SafeAsync(() => NavigateHistoryAsync(true));
    private async Task NavigateHistoryAsync(bool forward)
    {
        if (navigatingHistory) return;
        navigatingHistory = true; navigationDirection = forward ? 1 : -1;
        try
        {
            var source = forward ? forwardHistory : navigationHistory;
            while (source.TryPop(out var entry))
            {
                if (entry.Page == WorkspaceView && (entry.Session is null || !sessions.Any(s => s.Id == entry.Session.Id))) continue;
                var previous = navigationPage is null ? null : new NavigationEntry(navigationPage, navigationSession);
                // Commit the reverse entry only after navigation succeeds (saving media can fail).
                try
                {
                    if (entry.Page == WorkspaceView && current?.Id != entry.Session!.Id) await OpenSessionAsync(entry.Session);
                    else ShowPage(entry.Page);
                }
                catch { source.Push(entry); throw; }
                if (previous is not null) (forward ? navigationHistory : forwardHistory).Push(previous);
                break;
            }
        }
        finally { navigatingHistory = false; navigationDirection = 0; UpdateHistoryButtons(); }
    }
    private void ResetPageMotion(UIElement page)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(page, true);
        var visual = ElementCompositionPreview.GetElementVisual(page);
        visual.StopAnimation("Opacity"); visual.StopAnimation("Translation");
        visual.Opacity = 1; visual.Properties.InsertVector3("Translation", Vector3.Zero);
    }
    private void AnimatePage(UIElement page, bool changed)
    {
        if (!changed || !Root.IsLoaded || settings.ReduceMotion || !motionSettings.AnimationsEnabled) return;
        ElementCompositionPreview.SetIsTranslationEnabled(page, true);
        var visual = ElementCompositionPreview.GetElementVisual(page);
        var compositor = visual.Compositor;
        using var easing = compositor.CreateCubicBezierEasingFunction(new(.2f, 0), new(0, 1));
        var offset = navigationDirection == 0 ? new Vector3(0, 10, 0) : new Vector3(navigationDirection * 18, 0, 0);
        using var slide = compositor.CreateVector3KeyFrameAnimation(); slide.Duration = TimeSpan.FromMilliseconds(200);
        slide.InsertKeyFrame(0, offset); slide.InsertKeyFrame(1, Vector3.Zero, easing);
        using var fade = compositor.CreateScalarKeyFrameAnimation(); fade.Duration = TimeSpan.FromMilliseconds(160);
        fade.InsertKeyFrame(0, 0); fade.InsertKeyFrame(1, 1, easing);
        visual.StartAnimation("Translation", slide); visual.StartAnimation("Opacity", fade);
        navigationAnimationCount++;
    }
    private void Root_BackPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(Root).Properties;
        if (properties.IsXButton1Pressed && navigationHistory.Count > 0)
        { e.Handled = true; Back_Click(this, new()); }
        else if (properties.IsXButton2Pressed && forwardHistory.Count > 0)
        { e.Handled = true; Forward_Click(this, new()); }
    }
}
