using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.Media.Playback;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private bool followingPlaybackSelection;
    private void FollowPlaybackCue(SubtitleSegment cue)
    {
        // Do not replace text under an editing caret, nor select filtered-out cues.
        var focus = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
        if (Within(focus, OriginalEditor) || Within(focus, TranslationEditor) || !visibleSegments.Contains(cue)) return;
        followingPlaybackSelection = true;
        try { SubtitleList.SelectedItem = cue; SubtitleList.ScrollIntoView(cue, ScrollIntoViewAlignment.Default); lastFollowId = cue.Id; }
        finally { followingPlaybackSelection = false; }
    }
    private static bool Within(DependencyObject? element, DependencyObject ancestor)
    {
        while (element is not null) { if (ReferenceEquals(element, ancestor)) return true; element = VisualTreeHelper.GetParent(element); }
        return false;
    }
    private void Workspace_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Space || WorkspaceView.Visibility != Visibility.Visible || Player.Source is null || Player.MediaPlayer is null) return;
        var element = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
        while (element is not null)
        {
            if (element is TextBox or RichEditBox or PasswordBox or ComboBox or Slider or NumberBox) return;
            element = VisualTreeHelper.GetParent(element);
        }
        if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Count > 0) return;
        foreach (var key in new[] { VirtualKey.Control, VirtualKey.Menu, VirtualKey.Shift })
            if (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) return;
        e.Handled = true; if (e.KeyStatus.WasKeyDown) return;
        if (Player.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) Player.MediaPlayer.Pause(); else Player.MediaPlayer.Play();
    }
}
