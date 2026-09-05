using PvpDuel.Core.Duel;

using Xunit;

namespace PvpDuel.Core.Tests.Outcomes;

/// <summary>
/// Ticket #23 (spec §4.2/§8): the both-end settlement state machine. Three
/// paths must behave exactly as specified — agreement settles, disagreement
/// diverges, and a remote report that never arrives expires into divergence.
/// Pure logic: the caller supplies elapsed time, no clock inside.
/// </summary>
public class DuelOutcomeConsensusTests
{
    private const int TimeoutMs = 30_000;

    private static DuelResult Result(
        int actIndex = 0,
        ulong winner = 10,
        ulong loser = 20,
        DuelEndReason reason = DuelEndReason.Kill) => new(actIndex, winner, loser, reason);

    private static DuelOutcomeConsensus NewConsensus() =>
        new(TimeSpan.FromMilliseconds(TimeoutMs));

    // ---- path 1: agreement settles on the local report ----

    [Fact]
    public void LocalThenMatchingRemote_Settles_OnLocalResult()
    {
        var consensus = NewConsensus();
        var local = Result(reason: DuelEndReason.DoubleDeathFirstHandWins);

        consensus.ReportLocal(local);
        consensus.ReportRemote(Result(reason: DuelEndReason.DoubleDeathFirstHandWins));

        Assert.Equal(DuelOutcomeVerdict.Settled, consensus.Verdict);
        Assert.Equal(local, consensus.Settled);
        Assert.False(consensus.WaitOverdue(TimeSpan.FromDays(1)));
    }

    [Fact]
    public void RemoteThenMatchingLocal_Settles_OnLocalResult()
    {
        var consensus = NewConsensus();
        var local = Result(actIndex: 1, winner: 30, loser: 40);

        consensus.ReportRemote(Result(actIndex: 1, winner: 30, loser: 40));
        Assert.Equal(DuelOutcomeVerdict.Pending, consensus.Verdict); // remote report buffers, cannot settle alone
        consensus.ReportLocal(local);

        Assert.Equal(DuelOutcomeVerdict.Settled, consensus.Verdict);
        Assert.Equal(local, consensus.Settled);
    }

    // ---- path 2: disagreement diverges ----

    [Theory]
    [InlineData(1, 10, 20, 0)] // act differs
    [InlineData(0, 11, 20, 0)] // winner differs
    [InlineData(0, 10, 21, 0)] // loser differs
    [InlineData(0, 10, 20, 2)] // reason differs
    public void MismatchedReports_Diverge(
        int remoteAct, ulong remoteWinner, ulong remoteLoser, int remoteReason)
    {
        var consensus = NewConsensus();
        consensus.ReportLocal(Result());
        consensus.ReportRemote(Result(
            actIndex: remoteAct,
            winner: remoteWinner,
            loser: remoteLoser,
            reason: (DuelEndReason)remoteReason));

        Assert.Equal(DuelOutcomeVerdict.Diverged, consensus.Verdict);
        Assert.Null(consensus.Settled);
    }

    [Fact]
    public void UndecodableRemoteReport_Diverges()
    {
        var consensus = NewConsensus();
        consensus.ReportLocal(Result());

        consensus.ReportUndecodableRemote();

        Assert.Equal(DuelOutcomeVerdict.Diverged, consensus.Verdict);
        Assert.Null(consensus.Settled);
    }

    // ---- path 3: timeout without the remote report expires into divergence ----

    [Fact]
    public void WaitingForRemote_IsOverdueExactlyAtTimeout()
    {
        var consensus = NewConsensus();
        consensus.ReportLocal(Result());

        Assert.True(consensus.WaitingForRemote);
        Assert.False(consensus.WaitOverdue(TimeSpan.FromMilliseconds(TimeoutMs - 1)));
        Assert.True(consensus.WaitOverdue(TimeSpan.FromMilliseconds(TimeoutMs)));
    }

    [Fact]
    public void BufferedRemoteWithoutLocal_IsAlsoOverdueAtTimeout()
    {
        var consensus = NewConsensus();
        consensus.ReportRemote(Result());

        Assert.False(consensus.WaitingForRemote); // waiting for the local report instead
        Assert.False(consensus.WaitOverdue(TimeSpan.FromMilliseconds(TimeoutMs - 1)));
        Assert.True(consensus.WaitOverdue(TimeSpan.FromMilliseconds(TimeoutMs)));
    }

    [Fact]
    public void DefaultTimeout_IsThirtySeconds()
    {
        var consensus = new DuelOutcomeConsensus();
        consensus.ReportLocal(Result());

        Assert.True(consensus.WaitOverdue(TimeSpan.FromSeconds(30)));
        Assert.False(consensus.WaitOverdue(TimeSpan.FromSeconds(30) - TimeSpan.FromMilliseconds(1)));
    }

    // ---- verdict stickiness (spec: settlement/divergence happen exactly once) ----

    [Fact]
    public void Verdict_IsSticky_AfterSettlement()
    {
        var consensus = NewConsensus();
        consensus.ReportLocal(Result());
        consensus.ReportRemote(Result());
        Assert.Equal(DuelOutcomeVerdict.Settled, consensus.Verdict);

        // A later contradicting report (echo, rebroadcast, skew) cannot flip it.
        consensus.ReportLocal(Result(winner: 99, loser: 1));
        consensus.ReportRemote(Result(winner: 99, loser: 1));

        Assert.Equal(DuelOutcomeVerdict.Settled, consensus.Verdict);
        Assert.Equal(Result(), consensus.Settled);
    }

    [Fact]
    public void Verdict_IsSticky_AfterDivergence()
    {
        var consensus = NewConsensus();
        consensus.ReportLocal(Result());
        consensus.ReportRemote(Result(winner: 21));
        Assert.Equal(DuelOutcomeVerdict.Diverged, consensus.Verdict);

        consensus.ReportRemote(Result());
        consensus.ReportLocal(Result());

        Assert.Equal(DuelOutcomeVerdict.Diverged, consensus.Verdict);
        Assert.Null(consensus.Settled);
    }

    [Fact]
    public void DuplicateReports_AreIdempotent()
    {
        var consensus = NewConsensus();
        consensus.ReportLocal(Result());
        consensus.ReportLocal(Result());
        consensus.ReportRemote(Result());
        consensus.ReportRemote(Result());

        Assert.Equal(DuelOutcomeVerdict.Settled, consensus.Verdict);
        Assert.Equal(Result(), consensus.Settled);
    }

    [Fact]
    public void MirrorsAgainstNullRemote_NeverSettles_Alone()
    {
        // A lone local report must not settle by itself — the settlement only
        // exists once both ends reported (spec §4.2: 双方消息一致才落定).
        var consensus = NewConsensus();
        consensus.ReportLocal(Result());

        Assert.Equal(DuelOutcomeVerdict.Pending, consensus.Verdict);
        Assert.Null(consensus.Settled);
    }

    [Fact]
    public void Reports_RejectNull()
    {
        var consensus = NewConsensus();

        Assert.Throws<ArgumentNullException>(() => consensus.ReportLocal(null!));
        Assert.Throws<ArgumentNullException>(() => consensus.ReportRemote(null!));
    }
}
