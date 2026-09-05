using PvpDuel.Core.Branching;
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

    // ---- Ticket #20: divergence semantics (checksum exclusion / room scoping) ----

    [Fact]
    public void Diverged_TwoPlayersAtDifferentCoords_IsTrue()
    {
        var table = new BranchTable();
        table.Reset(0);
        table.Set(1, State(col: 1, row: 1));
        Assert.False(table.Diverged);
        table.Set(2, State(col: 5, row: 2));
        Assert.True(table.Diverged);
    }

    [Fact]
    public void Diverged_SinglePlayerOrSameCoords_IsFalse()
    {
        var table = new BranchTable();
        table.Reset(0);
        Assert.False(table.Diverged);
        table.Set(1, State(col: 3, row: 3));
        Assert.False(table.Diverged);
        table.Set(2, State(col: 3, row: 3));
        Assert.False(table.Diverged);
    }

    [Fact]
    public void Diverged_ReconvergenceAtBoss_ClearsIt()
    {
        var table = new BranchTable();
        table.Reset(2);
        table.Set(1, State(act: 2, col: 0, row: 1));
        table.Set(2, State(act: 2, col: 6, row: 1));
        Assert.True(table.Diverged);

        var boss = new MapCoord(3, 9);
        table.Set(1, new BranchState(2, boss, InBossWait: true));
        table.Set(2, new BranchState(2, boss, InBossWait: true));
        Assert.False(table.Diverged);
        Assert.True(BranchTable.IsBossRendezvous(table.Get(1), table.Get(2)));
    }

    [Fact]
    public void SameBranch_RequiresBothKnownAndEqualCoord()
    {
        var table = new BranchTable();
        table.Reset(0);
        table.Set(1, State(col: 2, row: 2));
        Assert.False(table.SameBranch(1, 2));
        table.Set(2, State(col: 2, row: 2));
        Assert.True(table.SameBranch(1, 2));
        table.Set(2, State(col: 4, row: 2));
        Assert.False(table.SameBranch(1, 2));
    }

    [Fact]
    public void Diverged_DifferentActs_StillCountsByCoord()
    {
        var table = new BranchTable();
        table.Reset(1);
        table.Set(1, State(act: 1, col: 1, row: 1));
        table.Set(2, State(act: 1, col: 5, row: 1));
        Assert.True(table.Diverged);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BranchExclusion_MatchesCoordDifference(bool sameCoord)
    {
        var a = State(col: 1, row: 1);
        var b = sameCoord ? State(col: 1, row: 1) : State(col: 5, row: 2);
        Assert.Equal(!sameCoord, BranchExclusion.Diverged(a, b));
        Assert.False(BranchExclusion.Diverged(a, null));
        Assert.False(BranchExclusion.Diverged(null, b));
    }
    [Fact]
    public void BranchScaling_DivergedFightCountsOneParticipant_ConvergedCountsRoster()
    {
        // Spec §12.2: branch combat enemy values scale by branch participants.
        Assert.Equal(1, BranchExclusion.CombatParticipantCount(diverged: true, runPlayerCount: 2));
        Assert.Equal(2, BranchExclusion.CombatParticipantCount(diverged: false, runPlayerCount: 2));
        Assert.Equal(1, BranchExclusion.CombatParticipantCount(diverged: false, runPlayerCount: 1));
    }
}
