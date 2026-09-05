using PvpDuel.Core.Duel;
using Xunit;

namespace PvpDuel.Core.Tests;

public class DuelHistoryTests
{
    private static DuelResult Result(int act, ulong winner = 1, ulong loser = 2, DuelEndReason reason = DuelEndReason.Kill) =>
        new(act, winner, loser, reason);

    [Fact]
    public void Record_ThenGetForAct_RoundTrips()
    {
        var history = new DuelHistory();
        history.Record(Result(1, winner: 10, loser: 20));
        history.Record(Result(2, winner: 20, loser: 10, reason: DuelEndReason.DoubleDeathFirstHandWins));

        Assert.Equal(DuelEndReason.Kill, history.GetForAct(1)!.Reason);
        Assert.Equal((ulong)20, history.GetForAct(2)!.WinnerNetId);
        Assert.Null(history.GetForAct(3));
    }

    [Fact]
    public void Record_SameAct_ReplacesPrevious_KeepsActOrder()
    {
        var history = new DuelHistory();
        history.Record(Result(2, winner: 5));
        history.Record(Result(1, winner: 6));
        history.Record(Result(2, winner: 7, reason: DuelEndReason.Forfeit));

        Assert.Equal(2, history.Results.Count);
        Assert.Equal(1, history.Results[0].ActIndex);
        Assert.Equal((ulong)7, history.Results[1].WinnerNetId);
        Assert.Equal((ulong)7, history.Latest!.WinnerNetId);
    }

    [Fact]
    public void EmptyHistory_HasNoLatest()
    {
        var history = new DuelHistory();
        Assert.Empty(history.Results);
        Assert.Null(history.Latest);
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var history = new DuelHistory();
        history.Record(Result(1));
        history.Clear();
        Assert.Empty(history.Results);
    }

    [Fact]
    public void Mirrors_RequiresExactFieldMatch()
    {
        var a = Result(1, winner: 1, loser: 2, reason: DuelEndReason.Kill);
        Assert.True(a.Mirrors(new DuelResult(1, 1, 2, DuelEndReason.Kill)));
        Assert.False(a.Mirrors(new DuelResult(1, 1, 2, DuelEndReason.Forfeit)));
        Assert.False(a.Mirrors(new DuelResult(1, 2, 1, DuelEndReason.Kill)));
        Assert.False(a.Mirrors(new DuelResult(2, 1, 2, DuelEndReason.Kill)));
        Assert.False(a.Mirrors(null));
    }

    [Fact]
    public void Record_Null_Throws()
    {
        DuelResult? nullResult = null;
        Assert.Throws<ArgumentNullException>(() => new DuelHistory().Record(nullResult!));
    }
}
