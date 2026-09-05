using PvpDuel.Core.Duel;

using Xunit;

namespace PvpDuel.Core.Tests;

/// <summary>
/// Ticket #19 (T7): the local duel-outcome event source — per-combat tracker
/// lifecycle, idempotent settlement, sink recording (the mod layer binds the
/// sink to the run-scoped history the #21 save side-channel persists) and the
/// settlement event the net ticket (T11) consumes.
/// </summary>
public class DuelResultSourceTests : IDisposable
{
    private readonly DuelHistory _sinkHistory = new();

    public DuelResultSourceTests()
    {
        DuelResultSource.EndDuelCombat();
        DuelResultSource.SettledSink = _sinkHistory.Record;
    }

    public void Dispose()
    {
        DuelResultSource.EndDuelCombat();
        DuelResultSource.SettledSink = null;
    }

    [Fact]
    public void BeginDuelCombat_ArmsFreshTracker_EndDropsIt()
    {
        DuelResultSource.BeginDuelCombat();
        var tracker = DuelResultSource.Current;
        Assert.NotNull(tracker);
        Assert.False(tracker!.HasObservations);

        DuelResultSource.EndDuelCombat();
        Assert.Null(DuelResultSource.Current);
    }

    [Fact]
    public void ReBeginDuelCombat_PreviousObservationsNotCarriedOver()
    {
        DuelResultSource.BeginDuelCombat();
        var first = DuelResultSource.Current!;
        first.ObserveLocalDeath();

        DuelResultSource.BeginDuelCombat();

        var second = DuelResultSource.Current!;
        Assert.NotSame(first, second);
        Assert.False(second.HasObservations);
    }

    [Fact]
    public void Observations_SettleIntoSink_AndRaiseEvent()
    {
        DuelResult? raised = null;
        DuelResultSource.OutcomeSettledLocally += result => raised = result;

        DuelResultSource.BeginDuelCombat();
        DuelResultSource.Current!.ObserveLocalDeath();

        var resolved = DuelResultSource.Current!.TryResolve(
            actIndex: 1, localNetId: 10, opponentNetId: 20, firstHandNetId: 0);

        Assert.NotNull(resolved);
        Assert.Equal((ulong)20, resolved!.WinnerNetId);
        Assert.Same(resolved, DuelResultSource.Current.Resolved);
        Assert.Same(resolved, raised);
        Assert.Same(resolved, _sinkHistory.GetForAct(1));
    }

    [Fact]
    public void TryResolve_IsIdempotent_AndPostResolveObservationsAreIgnored()
    {
        DuelResultSource.BeginDuelCombat();
        var tracker = DuelResultSource.Current!;
        tracker.ObserveOpponentDeath();

        var first = tracker.TryResolve(2, localNetId: 10, opponentNetId: 20, firstHandNetId: 0);
        tracker.ObserveLocalDeath(); // late observation must not rewrite the settled result
        var second = tracker.TryResolve(2, localNetId: 10, opponentNetId: 20, firstHandNetId: 10);

        Assert.Equal(DuelEndReason.Kill, first!.Reason);
        Assert.Same(first, second);
        Assert.False(tracker.LocalDied);
    }

    [Fact]
    public void ReSettledSameAct_ReplacesSinkEntry_LastWriteWins()
    {
        DuelResultSource.BeginDuelCombat();
        var tracker = DuelResultSource.Current!;
        tracker.ObserveLocalDeath();
        tracker.TryResolve(1, localNetId: 10, opponentNetId: 20, firstHandNetId: 0);

        // Re-entering the same act: fresh tracker, opposite observation.
        DuelResultSource.BeginDuelCombat();
        var retry = DuelResultSource.Current!;
        retry.ObserveOpponentDeath();
        retry.TryResolve(1, localNetId: 10, opponentNetId: 20, firstHandNetId: 0);

        var recorded = _sinkHistory.GetForAct(1);
        Assert.NotNull(recorded);
        Assert.Equal((ulong)10, recorded!.WinnerNetId);
        Assert.Single(_sinkHistory.Results);
    }

    [Fact]
    public void UnobservedTracker_ResolvesToNull_AndRecordsNothing()
    {
        DuelResultSource.BeginDuelCombat();

        var resolved = DuelResultSource.Current!.TryResolve(3, localNetId: 10, opponentNetId: 20, firstHandNetId: 0);

        Assert.Null(resolved);
        Assert.Null(_sinkHistory.GetForAct(3));
        Assert.Null(DuelResultSource.Current.Resolved);
    }

    [Fact]
    public void NullSink_StillRaisesEvent_WithoutRecording()
    {
        DuelResultSource.SettledSink = null;
        DuelResult? raised = null;
        DuelResultSource.OutcomeSettledLocally += result => raised = result;

        DuelResultSource.BeginDuelCombat();
        DuelResultSource.Current!.ObserveLocalDeath();
        var resolved = DuelResultSource.Current!.TryResolve(4, localNetId: 10, opponentNetId: 20, firstHandNetId: 0);

        Assert.NotNull(resolved);
        Assert.Same(resolved, raised);
        Assert.Empty(_sinkHistory.Results);
    }
}
