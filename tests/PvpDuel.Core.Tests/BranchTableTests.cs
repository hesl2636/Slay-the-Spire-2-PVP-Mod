using PvpDuel.Core.Duel;
using Xunit;

namespace PvpDuel.Core.Tests;

public class BranchTableTests
{
    private static BranchState State(int act = 0, int col = 1, int row = 2, bool boss = false) =>
        new(act, new MapCoord(col, row), boss);

    [Fact]
    public void Set_Then_Get_RoundTrips()
    {
        var table = new BranchTable();
        table.Reset(1);
        table.Set(42, State(boss: false));

        Assert.Equal(State(), table.Get(42));
        Assert.Equal(1, table.ActIndex);
    }

    [Fact]
    public void Set_SameState_ReturnsFalse_ChangedState_ReturnsTrue()
    {
        var table = new BranchTable();
        table.Reset(0);
        table.Set(7, State());

        Assert.False(table.Set(7, State()));
        Assert.True(table.Set(7, State(boss: true)));
        Assert.True(table.Get(7).InBossWait);
    }

    [Fact]
    public void Get_UnknownPlayer_Throws_TryGet_ReturnsFalse()
    {
        var table = new BranchTable();
        Assert.Throws<KeyNotFoundException>(() => table.Get(1));
        Assert.False(table.TryGet(1, out _));
    }

    [Fact]
    public void Reset_ClearsAllStates()
    {
        var table = new BranchTable();
        table.Reset(0);
        table.Set(1, State());
        table.Set(2, State());

        table.Reset(1);
        Assert.Empty(table.Snapshot);
        Assert.Equal(1, table.ActIndex);
        Assert.Throws<KeyNotFoundException>(() => table.Get(1));
    }

    [Fact]
    public void BossRendezvous_RequiresBothWaiting_SameAct()
    {
        Assert.True(BranchTable.IsBossRendezvous(State(boss: true, act: 2), State(boss: true, act: 2)));
        Assert.False(BranchTable.IsBossRendezvous(State(boss: true, act: 2), State(boss: false, act: 2)));
        Assert.False(BranchTable.IsBossRendezvous(State(boss: true, act: 2), State(boss: true, act: 1)));
        Assert.False(BranchTable.IsBossRendezvous(null, State(boss: true)));
    }

    [Fact]
    public void StartOfAct_IsNeutral()
    {
        var start = BranchState.StartOfAct(3);
        Assert.Equal(3, start.ActIndex);
        Assert.False(start.InBossWait);
    }

    [Fact]
    public void Reset_NegativeAct_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BranchTable().Reset(-1));
    }
}
