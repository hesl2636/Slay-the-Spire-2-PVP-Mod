using PvpDuel.Core.Branching;
using PvpDuel.Core.Duel;
using Xunit;

namespace PvpDuel.Core.Tests;

/// <summary>
/// Ticket #20: host-side vote grouping (Forked Road mode) — each distinct voted
/// coordinate becomes a branch instead of vanilla's single converged coordinate.
/// </summary>
public class BranchVoteGrouperTests
{
    private static BranchVote Vote(int slot, ulong netId, int col, int row) =>
        new(slot, netId, new MapCoord(col, row));

    private static BranchVote Unvoted(int slot, ulong netId) => new(slot, netId, null);

    [Fact]
    public void SameCoord_VotesFormOneGroup()
    {
        var groups = BranchVoteGrouper.Group(
        [
            Vote(0, 100, 1, 1),
            Vote(1, 200, 1, 1),
        ]);

        var group = Assert.Single(groups);
        Assert.Equal(new MapCoord(1, 1), group.Coord);
        Assert.Equal(new ulong[] { 100, 200 }, group.PlayerNetIds);
    }

    [Fact]
    public void DivergentVotes_GroupedByCoord_InFirstVoterSlotOrder()
    {
        var groups = BranchVoteGrouper.Group(
        [
            Vote(1, 200, 5, 2),
            Vote(0, 100, 1, 1),
        ]);

        Assert.Equal(2, groups.Count);
        Assert.Equal(new MapCoord(1, 1), groups[0].Coord);
        Assert.Equal(new ulong[] { 100 }, groups[0].PlayerNetIds);
        Assert.Equal(new MapCoord(5, 2), groups[1].Coord);
        Assert.Equal(new ulong[] { 200 }, groups[1].PlayerNetIds);
    }

    [Fact]
    public void UnvotedPlayers_AreExcluded_AndReported()
    {
        var unvoted = new List<ulong>();
        var groups = BranchVoteGrouper.Group(
        [
            Vote(0, 100, 1, 1),
            Unvoted(1, 200),
        ], unvoted);

        var group = Assert.Single(groups);
        Assert.Equal(new ulong[] { 100 }, group.PlayerNetIds);
        Assert.Equal(new ulong[] { 200 }, unvoted);
    }

    [Fact]
    public void DuplicateCoordVotes_MergeIntoSingleGroup()
    {
        var groups = BranchVoteGrouper.Group(
        [
            Vote(0, 100, 3, 4),
            Unvoted(1, 200),
            Vote(2, 300, 3, 4),
            Vote(3, 400, 3, 4),
        ]);

        var group = Assert.Single(groups);
        Assert.Equal(new MapCoord(3, 4), group.Coord);
        Assert.Equal(new ulong[] { 100, 300, 400 }, group.PlayerNetIds);
    }

    [Fact]
    public void EmptyVotes_ProduceNoGroups()
    {
        Assert.Empty(BranchVoteGrouper.Group([]));
    }
}
