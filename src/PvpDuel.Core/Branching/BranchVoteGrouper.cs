using PvpDuel.Core.Duel;

namespace PvpDuel.Core.Branching;

/// <summary>One player's map-screen vote, game-independent mirror of <c>MapVote?</c>.</summary>
public sealed record BranchVote(int Slot, ulong NetId, MapCoord? Vote);

/// <summary>
/// A resolved branch: every player who voted for the same map coordinate travels
/// there together. Groups are ordered by their first member's slot so the host
/// resolves branches deterministically.
/// </summary>
public sealed record BranchGroup(MapCoord Coord, IReadOnlyList<ulong> PlayerNetIds);

/// <summary>
/// Forked-Road vote grouping (spec §6 / research #3 Q2): instead of vanilla's
/// "all votes in → one shared coordinate", each distinct voted coordinate becomes
/// a branch. Pure logic — the host patch collects the votes and this decides the
/// grouping; no RNG, no wall clock.
/// </summary>
public static class BranchVoteGrouper
{
    /// <summary>
    /// Groups votes by coordinate. Players without a vote are excluded (they are
    /// reported through <paramref name="unvoted"/> when that list is provided);
    /// a coordinate shared by several players forms one group. Group order
    /// follows the first member's slot; members are slot-ordered.
    /// </summary>
    public static IReadOnlyList<BranchGroup> Group(
        IEnumerable<BranchVote> votes,
        List<ulong>? unvoted = null)
    {
        ArgumentNullException.ThrowIfNull(votes);

        unvoted?.Clear();
        var byCoord = new Dictionary<MapCoord, List<ulong>>();
        var order = new List<MapCoord>();
        foreach (var vote in votes.OrderBy(v => v.Slot))
        {
            if (vote.Vote is not { } coord)
            {
                unvoted?.Add(vote.NetId);
                continue;
            }

            if (!byCoord.TryGetValue(coord, out var members))
            {
                members = [];
                byCoord[coord] = members;
                order.Add(coord);
            }

            members.Add(vote.NetId);
        }

        return order
            .Select(coord => new BranchGroup(coord, byCoord[coord]))
            .ToList();
    }
}
