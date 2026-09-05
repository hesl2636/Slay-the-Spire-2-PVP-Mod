using PvpDuel.Core.Duel;

using Xunit;

namespace PvpDuel.Core.Tests;

/// <summary>
/// Ticket #19 (T7): the pure kill-batch decision table and outcome resolution
/// for the duel-loss interception (spec §7 #4). Regression red line: every
/// non-duel / forced / non-player shape must pass through to vanilla.
/// </summary>
public class LossInterceptPolicyTests
{
    [Theory]
    [InlineData(true, false, false, true, false, true, KillInterceptAction.InterceptLocalDeath)] // harness local death
    [InlineData(true, false, false, true, true, true, KillInterceptAction.InterceptLocalDeath)]  // harness double death (one batch)
    [InlineData(true, false, false, true, false, false, KillInterceptAction.ObserveOnly)]         // co-op local death (vanilla mechanics)
    [InlineData(true, false, false, true, true, false, KillInterceptAction.ObserveOnly)]          // co-op double death
    [InlineData(true, false, false, false, true, true, KillInterceptAction.ObserveOnly)]          // opponent died (I won)
    [InlineData(true, false, false, false, true, false, KillInterceptAction.ObserveOnly)]         // opponent died in co-op
    [InlineData(false, false, false, true, false, true, KillInterceptAction.PassThrough)]         // non-duel room: red line
    [InlineData(true, true, false, true, false, true, KillInterceptAction.PassThrough)]           // forced batch (abandon): vanilla
    [InlineData(true, false, true, true, false, true, KillInterceptAction.PassThrough)]           // non-player creature in batch
    [InlineData(true, false, false, false, false, true, KillInterceptAction.PassThrough)]         // nothing player-shaped
    public void DecideKill_FollowsTheDecisionTable(
        bool isDuelRoom,
        bool isForced,
        bool hasNonPlayer,
        bool hasLocal,
        bool hasOpponent,
        bool isSingleMachine,
        KillInterceptAction expected)
    {
        var action = LossInterceptPolicy.DecideKill(isDuelRoom, isForced, hasNonPlayer, hasLocal, hasOpponent, isSingleMachine);

        Assert.Equal(expected, action);
    }

    [Fact]
    public void Resolve_LocalDeathOnly_OpponentWinsByKill()
    {
        var result = LossInterceptPolicy.ResolveOutcome(1, localNetId: 10, opponentNetId: 20, localDied: true, opponentDied: false, firstHandNetId: 0);

        Assert.NotNull(result);
        Assert.Equal((ulong)20, result!.WinnerNetId);
        Assert.Equal((ulong)10, result.LoserNetId);
        Assert.Equal(DuelEndReason.Kill, result.Reason);
    }

    [Fact]
    public void Resolve_OpponentDeathOnly_LocalWinsByKill()
    {
        var result = LossInterceptPolicy.ResolveOutcome(2, localNetId: 10, opponentNetId: 20, localDied: false, opponentDied: true, firstHandNetId: 0);

        Assert.NotNull(result);
        Assert.Equal((ulong)10, result!.WinnerNetId);
        Assert.Equal((ulong)20, result.LoserNetId);
        Assert.Equal(DuelEndReason.Kill, result.Reason);
    }

    [Fact]
    public void Resolve_DoubleDeath_FirstHandPlayerWins()
    {
        var localFirst = LossInterceptPolicy.ResolveOutcome(1, 10, 20, localDied: true, opponentDied: true, firstHandNetId: 10);
        var opponentFirst = LossInterceptPolicy.ResolveOutcome(1, 10, 20, localDied: true, opponentDied: true, firstHandNetId: 20);

        Assert.Equal((ulong)10, localFirst!.WinnerNetId);
        Assert.Equal(DuelEndReason.DoubleDeathFirstHandWins, localFirst.Reason);
        Assert.Equal((ulong)20, opponentFirst!.WinnerNetId);
        Assert.Equal(DuelEndReason.DoubleDeathFirstHandWins, opponentFirst.Reason);
    }

    [Fact]
    public void Resolve_DoubleDeath_UnknownFirstHand_FallsBackToLowerNetId_Deterministically()
    {
        // Placeholder first-hand (stub 0): both ends must derive the same winner.
        var result = LossInterceptPolicy.ResolveOutcome(1, 30, 20, localDied: true, opponentDied: true, firstHandNetId: 0);

        Assert.NotNull(result);
        Assert.Equal((ulong)20, result!.WinnerNetId);
        Assert.Equal((ulong)30, result.LoserNetId);
        Assert.Equal(DuelEndReason.DoubleDeathFirstHandWins, result.Reason);
    }

    [Fact]
    public void Resolve_NoDeaths_ReturnsNull()
    {
        Assert.Null(LossInterceptPolicy.ResolveOutcome(1, 10, 20, localDied: false, opponentDied: false, firstHandNetId: 10));
    }

    [Fact]
    public void FateThreadHp_IsOne()
    {
        Assert.Equal(1, LossInterceptPolicy.FateThreadHp);
    }
}
