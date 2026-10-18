using System.Collections.ObjectModel;
using MemoAI.Core;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    // Worker messages are cumulative snapshots deserialized into new objects.
    // Reuse identities so unchanged cues retain containers, selection and focus.
    private void ApplySubtitleSnapshot(MediaSession session, IReadOnlyList<SubtitleSegment> snapshot)
    {
        var existing = session.Segments.ToDictionary(s => s.Id);
        var next = new List<SubtitleSegment>(snapshot.Count);
        var wasSelecting = selecting; selecting = true;
        try
        {
            foreach (var incoming in snapshot)
            {
                if (!existing.TryGetValue(incoming.Id, out var cue)) cue = incoming;
                else if (!ReferenceEquals(cue, incoming))
                {
                    cue.Start = incoming.Start; cue.End = incoming.End;
                    cue.Text = incoming.Text; cue.Translation = incoming.Translation;
                    cue.Speaker = incoming.Speaker; cue.Words = incoming.Words;
                }
                next.Add(cue);
            }
            SyncItems(session.Segments, next);
        }
        finally { selecting = wasSelecting; }
    }

    private static void SyncItems<T>(ObservableCollection<T> items, IReadOnlyList<T> next) where T : class
    {
        var retained = next.ToHashSet();
        for (var i = items.Count - 1; i >= 0; i--)
            if (!retained.Contains(items[i])) items.RemoveAt(i);
        for (var i = 0; i < next.Count; i++)
        {
            if (i < items.Count && ReferenceEquals(items[i], next[i])) continue;
            var old = items.IndexOf(next[i]);
            if (old >= 0) items.Move(old, i); else items.Insert(i, next[i]);
        }
    }
}
