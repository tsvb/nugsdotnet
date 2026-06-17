using System.Collections.Generic;
using Nugsdotnet.UI.Services;
using Xunit;

namespace Nugsdotnet.Tests;

public class PlayerServiceTests
{
    private static NowPlaying T(string id) => new(id, id, "Artist");

    private static (PlayerService player, List<TrackChangeKind> kinds) NewPlayer()
    {
        var p = new PlayerService();
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        return (p, kinds);
    }

    private static PlayerService Playing(params string[] ids)
    {
        var p = new PlayerService();
        p.Play(new PlayRequest(new List<NowPlaying>(System.Array.ConvertAll(ids, T)), 0));
        return p;
    }

    [Fact]
    public void Play_emits_Fresh_and_sets_NextTrackId()
    {
        var (p, kinds) = NewPlayer();
        p.Play(new PlayRequest(new List<NowPlaying> { T("a"), T("b"), T("c") }, 0));
        Assert.Equal(new[] { TrackChangeKind.Fresh }, kinds);
        Assert.Equal("a", p.Current!.TrackId);
        Assert.Equal("b", p.NextTrackId);
    }

    [Fact]
    public void Next_emits_Advance()
    {
        var p = Playing("a", "b", "c");
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.Next();
        Assert.Equal(new[] { TrackChangeKind.Advance }, kinds);
        Assert.Equal("b", p.Current!.TrackId);
        Assert.Equal("c", p.NextTrackId);
    }

    [Fact]
    public void Previous_and_JumpTo_emit_Fresh()
    {
        var p = Playing("a", "b", "c");
        p.Next(); // now on b
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.Previous();
        p.JumpTo(2);
        Assert.Equal(new[] { TrackChangeKind.Fresh, TrackChangeKind.Fresh }, kinds);
    }

    [Fact]
    public void HandleEnded_midqueue_advances_endofqueue_preloadonly()
    {
        var p = Playing("a", "b");
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.HandleEnded();                 // a -> b (Advance)
        Assert.Equal("b", p.Current!.TrackId);
        Assert.Null(p.NextTrackId);
        p.HandleEnded();                 // queue end -> PreloadOnly, stays on b
        Assert.Equal(new[] { TrackChangeKind.Advance, TrackChangeKind.PreloadOnly }, kinds);
        Assert.Equal("b", p.Current!.TrackId);
    }

    [Fact]
    public void Enqueue_behind_active_emits_PreloadOnly_and_updates_NextTrackId()
    {
        var p = Playing("a");            // last track, no next
        Assert.Null(p.NextTrackId);
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.Enqueue(new List<NowPlaying> { T("b") });
        Assert.Equal(new[] { TrackChangeKind.PreloadOnly }, kinds);
        Assert.Equal("a", p.Current!.TrackId);
        Assert.Equal("b", p.NextTrackId);
    }

    [Fact]
    public void PlayNext_behind_active_emits_PreloadOnly_and_inserts_next()
    {
        var p = Playing("a", "c");       // on a, next is c
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.PlayNext(new List<NowPlaying> { T("b") });
        Assert.Equal(new[] { TrackChangeKind.PreloadOnly }, kinds);
        Assert.Equal("b", p.NextTrackId);
    }

    [Fact]
    public void RemoveAt_ondeck_emits_PreloadOnly_and_repoints_next()
    {
        var p = Playing("a", "b", "c");  // on a, next is b
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.RemoveAt(1);                   // remove b (on-deck)
        Assert.Equal(new[] { TrackChangeKind.PreloadOnly }, kinds);
        Assert.Equal("c", p.NextTrackId);
    }

    [Fact]
    public void RemoveAt_before_cursor_emits_PreloadOnly_and_keeps_current()
    {
        var p = Playing("a", "b", "c");
        p.Next();                        // on b (index 1), next is c
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.RemoveAt(0);                   // remove a (before cursor)
        Assert.Equal(new[] { TrackChangeKind.PreloadOnly }, kinds);
        Assert.Equal("b", p.Current!.TrackId);
        Assert.Equal("c", p.NextTrackId);
    }

    [Fact]
    public void RemoveAt_current_emits_Fresh()
    {
        var p = Playing("a", "b", "c");
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.RemoveAt(0);                   // remove current a
        Assert.Equal(new[] { TrackChangeKind.Fresh }, kinds);
        Assert.Equal("b", p.Current!.TrackId);
    }

    [Fact]
    public void Clear_emits_Fresh_with_null_current()
    {
        var p = Playing("a", "b");
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.Clear();
        Assert.Equal(new[] { TrackChangeKind.Fresh }, kinds);
        Assert.Null(p.Current);
        Assert.Null(p.NextTrackId);
    }

    [Fact]
    public void AdvanceFromPreload_advances_cursor_and_emits_PreloadOnly()
    {
        // The JS hot-path already swapped the audio to the preloaded next track;
        // .NET only syncs the cursor. It must NOT emit Advance (that would make
        // the layout swap a second time) — it emits PreloadOnly so the layout
        // just re-points the idle element at the new next track.
        var p = Playing("a", "b", "c");     // on a, b preloaded
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.AdvanceFromPreload();
        Assert.Equal(new[] { TrackChangeKind.PreloadOnly }, kinds);
        Assert.Equal("b", p.Current!.TrackId);
        Assert.Equal("c", p.NextTrackId);
    }

    [Fact]
    public void AdvanceFromPreload_at_last_track_is_noop()
    {
        var p = Playing("a", "b");
        p.Next();                            // on b (last) — nothing preloaded
        var kinds = new List<TrackChangeKind>();
        p.TrackChangeRequested += k => kinds.Add(k);
        p.AdvanceFromPreload();              // no next — must not advance or emit
        Assert.Empty(kinds);
        Assert.Equal("b", p.Current!.TrackId);
    }
}
