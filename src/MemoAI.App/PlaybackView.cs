using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Media.Playback;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private bool updatingPlaybackSeek, seekingPlayback;
    private static string PlaybackTime(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");
    private void RefreshPlaybackControls()
    {
        if (Player.Source is null || Player.MediaPlayer is null) return;
        var playback = Player.MediaPlayer.PlaybackSession;
        updatingPlaybackSeek = true;
        try
        {
            PlaybackSeek.Maximum = Math.Max(1, playback.NaturalDuration.TotalSeconds);
            if (!seekingPlayback) PlaybackSeek.Value = playback.Position.TotalSeconds;
            PlaybackPosition.Text = PlaybackTime(playback.Position.TotalSeconds); PlaybackDuration.Text = PlaybackTime(playback.NaturalDuration.TotalSeconds);
            PlaybackGlyph.Glyph = playback.PlaybackState == MediaPlaybackState.Playing ? "\uE769" : "\uE768";
        }
        finally { updatingPlaybackSeek = false; }
    }
    private void PlaybackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Player.Source is null || Player.MediaPlayer is null) return;
        if (Player.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) Player.MediaPlayer.Pause(); else Player.MediaPlayer.Play();
        RefreshPlaybackControls();
    }
    private void PlaybackSeek_Changed(object sender, RangeBaseValueChangedEventArgs e)
    { if (!updatingPlaybackSeek && Player?.Source is not null && Player.MediaPlayer is not null) Player.MediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Max(0, e.NewValue)); }
    private void PlaybackVolume_Changed(object sender, RangeBaseValueChangedEventArgs e)
    { if (Player?.MediaPlayer is not null) Player.MediaPlayer.Volume = e.NewValue / 100; }
    private void PlaybackRate_Changed(object sender, SelectionChangedEventArgs e)
    { if (Player?.MediaPlayer is not null && (sender as ComboBox)?.SelectedItem is ComboBoxItem item && double.TryParse(item.Tag as string, CultureInfo.InvariantCulture, out var speed)) Player.MediaPlayer.PlaybackSession.PlaybackRate = speed; }
}
