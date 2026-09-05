namespace PvpDuel.Core.Duel;

/// <summary>
/// A map coordinate, mirroring the game's <c>MapCoord(col,row)</c> shape while
/// staying game-independent. Column = horizontal lane position, row = floor.
/// </summary>
public sealed record MapCoord(int Col, int Row);

/// <summary>Host-authoritative per-player path position within one act.</summary>
public sealed record BranchState(
    int ActIndex,
    MapCoord Coord,
    bool InBossWait)
{
    /// <summary>Neutral state used before a player moves on the act map.</summary>
    public static BranchState StartOfAct(int actIndex) => new(actIndex, new MapCoord(0, 0), InBossWait: false);
}

/// <summary>
/// Host-authoritative table of per-player branch states for the current act.
/// Every mutation is expected to be broadcast via <c>DuelBranchMessage</c> by the
/// host; clients only apply received states. The table resets whenever the act changes.
/// </summary>
public sealed class BranchTable
{
    private readonly Dictionary<ulong, BranchState> _states = [];

    public int ActIndex { get; private set; } = -1;

    /// <summary>Two players waiting in the boss room of the same act = rendezvous reached.</summary>
    public static bool IsBossRendezvous(BranchState? a, BranchState? b) =>
        a is { InBossWait: true } && b is { InBossWait: true } && a.ActIndex == b.ActIndex;

    /// <summary>
    /// True when at least two players are recorded at different map coordinates —
    /// the session is split across branch paths. Drives checksum exclusion and
    /// branch-scoped room views on both ends (each derives it from the mirrored,
    /// host-authoritative table, so the answers always agree).
    /// </summary>
    public bool Diverged
    {
        get
        {
            if (_states.Count < 2)
            {
                return false;
            }

            MapCoord? first = null;
            foreach (var state in _states.Values)
            {
                if (first is null)
                {
                    first = state.Coord;
                }
                else if (state.Coord != first)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Whether both players are known and share the same map coordinate (same branch or reconverged).</summary>
    public bool SameBranch(ulong a, ulong b) =>
        _states.TryGetValue(a, out var stateA)
        && _states.TryGetValue(b, out var stateB)
        && stateA.Coord == stateB.Coord;

    public BranchState Get(ulong playerNetId)
    {
        if (_states.TryGetValue(playerNetId, out var state))
        {
            return state;
        }

        throw new KeyNotFoundException($"No branch state recorded for player {playerNetId}.");
    }

    public bool TryGet(ulong playerNetId, out BranchState state) => _states.TryGetValue(playerNetId, out state!);

    /// <summary>Sets (or overwrites) one player's branch state. Returns true when the stored value changed.</summary>
    public bool Set(ulong playerNetId, BranchState state)
    {
        if (_states.TryGetValue(playerNetId, out var existing) && existing == state)
        {
            return false;
        }

        _states[playerNetId] = state;
        return true;
    }

    public IReadOnlyDictionary<ulong, BranchState> Snapshot => _states;

    /// <summary>Clears the table for a new act; players start from <see cref="BranchState.StartOfAct"/> again.</summary>
    public void Reset(int actIndex)
    {
        if (actIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actIndex), actIndex, "Act index cannot be negative.");
        }

        _states.Clear();
        ActIndex = actIndex;
    }
}
