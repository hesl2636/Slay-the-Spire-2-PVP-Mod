using PvpDuel.Core.Duel;

namespace PvpDuel.Core.Branching;

/// <summary>
/// Branch-based validation/scaling exclusion (spec §5.5, research #3 step 5):
/// a player at a different map coordinate is not part of this branch — they
/// take no part in the branch's checksum validation and are not counted as a
/// second combat participant for scaling. Pure predicate so both ends derive
/// the same answer from the mirrored, host-authoritative branch table.
/// </summary>
public static class BranchExclusion
{
    /// <summary>
    /// Whether the two players' states are known and sit at different coordinates —
    /// i.e. the session is currently split across branches and shared-state
    /// validation (checksums, multiplayer scaling) must be skipped until the
    /// paths reconverge.
    /// </summary>
    public static bool Diverged(BranchState? a, BranchState? b) =>
        a is not null && b is not null && a.Coord != b.Coord;

    /// <summary>
    /// Branch combat participant count for scaling (spec §12.2): a diverged
    /// branch fight involves exactly the branch's players (two-player duel
    /// branch → 1), while a converged fight counts the full run roster —
    /// the rule behind the MultiplayerScalingModel postfix.
    /// </summary>
    public static int CombatParticipantCount(bool diverged, int runPlayerCount) =>
        diverged ? 1 : runPlayerCount;
}
