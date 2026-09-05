using PvpDuel.Core.Timing;

using Xunit;

namespace PvpDuel.Core.Tests.Timing;

/// <summary>
/// Ticket #22: pure per-act timing accumulation (spec §4.2/§7). The clock is
/// injected as caller-supplied timestamps, so every scenario below runs on a
/// fake timeline — no real time, no game singletons.
/// </summary>
public class ActTimingTrackerTests
{
    private const int Act = 0;
    private const ulong HostPlayer = 11;
    private const ulong ClientPlayer = 22;

    [Fact]
    public void Arrivals_AreMeasuredFromActStart()
    {
        var tracker = new ActTimingTracker();
        tracker.ActStarted(Act, nowMs: 1_000);
        Assert.True(tracker.RecordArrival(Act, HostPlayer, nowMs: 3_000));
        Assert.True(tracker.RecordArrival(Act, ClientPlayer, nowMs: 5_500));

        Assert.True(tracker.TryGetElapsedMs(Act, HostPlayer, out var hostMs));
        Assert.Equal(2_000, hostMs);
        Assert.True(tracker.TryGetElapsedMs(Act, ClientPlayer, out var clientMs));
        Assert.Equal(4_500, clientMs);
    }

    [Fact]
    public void RendezvousPair_Completes_WhenBothDuelistsArrived()
    {
        var tracker = new ActTimingTracker();
        tracker.ActStarted(Act, 0);
        Assert.False(tracker.TryGetRendezvousPair(Act, out _));

        tracker.RecordArrival(Act, ClientPlayer, 800);
        Assert.False(tracker.TryGetRendezvousPair(Act, out _));

        tracker.RecordArrival(Act, HostPlayer, 9_100);
        Assert.True(tracker.TryGetRendezvousPair(Act, out var pair));
        Assert.Equal(Act, pair.ActIndex);
        // Pair keeps confirmation order, not net-id order.
        Assert.Equal(ClientPlayer, pair.FirstNetId);
        Assert.Equal(800, pair.FirstElapsedMs);
        Assert.Equal(HostPlayer, pair.SecondNetId);
        Assert.Equal(9_100, pair.SecondElapsedMs);
    }

    [Fact]
    public void Arrival_BeforeActStart_IsRejected()
    {
        var tracker = new ActTimingTracker();
        Assert.False(tracker.RecordArrival(Act, HostPlayer, 100));
        Assert.False(tracker.TryGetRendezvousPair(Act, out _));
    }

    [Fact]
    public void RepeatArrival_SamePlayer_IsIgnored()
    {
        var tracker = new ActTimingTracker();
        tracker.ActStarted(Act, 0);
        Assert.True(tracker.RecordArrival(Act, HostPlayer, 500));
        Assert.False(tracker.RecordArrival(Act, HostPlayer, 9_999));

        Assert.True(tracker.TryGetElapsedMs(Act, HostPlayer, out var elapsed));
        Assert.Equal(500, elapsed); // first stamp wins, re-broadcasts cannot skew
    }

    [Fact]
    public void UnsetNetId_IsRejected()
    {
        var tracker = new ActTimingTracker();
        tracker.ActStarted(Act, 0);
        Assert.False(tracker.RecordArrival(Act, playerNetId: 0, nowMs: 10));
    }

    [Fact]
    public void ReArm_SameAct_ClearsPriorArrivals()
    {
        var tracker = new ActTimingTracker();
        tracker.ActStarted(Act, 0);
        tracker.RecordArrival(Act, HostPlayer, 100);

        tracker.ActStarted(Act, 5_000); // e.g. a re-fired act start (SL restore re-entry)

        Assert.False(tracker.TryGetElapsedMs(Act, HostPlayer, out _));
        Assert.False(tracker.TryGetRendezvousPair(Act, out _));
        Assert.True(tracker.RecordArrival(Act, HostPlayer, 6_500));
        Assert.True(tracker.TryGetElapsedMs(Act, HostPlayer, out var elapsed));
        Assert.Equal(1_500, elapsed); // measured from the new t0
    }

    [Fact]
    public void Acts_AreIndependentlyScoped()
    {
        var tracker = new ActTimingTracker();
        tracker.ActStarted(0, 0);
        tracker.ActStarted(1, 10);
        tracker.RecordArrival(0, HostPlayer, 100);
        tracker.RecordArrival(1, HostPlayer, 110);

        Assert.True(tracker.TryGetElapsedMs(0, HostPlayer, out var act0));
        Assert.Equal(100, act0);
        Assert.True(tracker.TryGetElapsedMs(1, HostPlayer, out var act1));
        Assert.Equal(100, act1);
    }
}
