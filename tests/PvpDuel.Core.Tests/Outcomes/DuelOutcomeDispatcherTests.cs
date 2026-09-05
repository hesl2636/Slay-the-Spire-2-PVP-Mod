using PvpDuel.Core.Duel;

using Xunit;

namespace PvpDuel.Core.Tests.Outcomes;

/// <summary>
/// Ticket #23 (spec §6): post-settlement distribution. Acts 1/2 winners drive
/// the Ancient first-pick flow (act event, T12 consumer); the act 3 result ends
/// the whole run (terminal hook, empty consumer must be testable).
/// </summary>
public class DuelOutcomeDispatcherTests : IDisposable
{
    private static DuelResult Result(int actIndex, ulong winner = 10, ulong loser = 20) =>
        new(actIndex, winner, loser, DuelEndReason.Kill);

    public void Dispose() => DuelOutcomeDispatcher.Reset();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Act12Settlement_RaisesActOutcomeSettled(int actIndex)
    {
        var raised = new List<DuelResult>();
        DuelOutcomeDispatcher.ActOutcomeSettled += raised.Add;
        var finalRaised = new List<DuelResult>();
        DuelOutcomeDispatcher.FinalOutcomeSettled += finalRaised.Add;

        var settled = Result(actIndex);
        DuelOutcomeDispatcher.Dispatch(settled);

        var act = Assert.Single(raised);
        Assert.Same(settled, act);
        Assert.Empty(finalRaised);
    }

    [Fact]
    public void FinalActSettlement_RaisesFinalOutcomeSettled()
    {
        var raised = new List<DuelResult>();
        DuelOutcomeDispatcher.ActOutcomeSettled += raised.Add;
        var finalRaised = new List<DuelResult>();
        DuelOutcomeDispatcher.FinalOutcomeSettled += finalRaised.Add;

        var settled = Result(DuelOutcomeDispatcher.FinalActIndex, winner: 30, loser: 40);
        DuelOutcomeDispatcher.Dispatch(settled);

        var final = Assert.Single(finalRaised);
        Assert.Same(settled, final);
        Assert.Empty(raised);
    }

    [Fact]
    public void FinalActIndex_IsActThree()
    {
        // Acts are zero-based throughout the mod (RunState.CurrentActIndex):
        // act 3 of 3 is index 2.
        Assert.Equal(2, DuelOutcomeDispatcher.FinalActIndex);
    }

    [Fact]
    public void Dispatch_WithoutSubscribers_DoesNotThrow()
    {
        DuelOutcomeDispatcher.Dispatch(Result(0));
        DuelOutcomeDispatcher.Dispatch(Result(DuelOutcomeDispatcher.FinalActIndex));
    }

    [Fact]
    public void Dispatch_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => DuelOutcomeDispatcher.Dispatch(null!));
    }
}
