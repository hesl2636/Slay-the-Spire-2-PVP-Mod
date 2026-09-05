namespace PvpDuel.Core.Duel;

/// <summary>What the <c>CreatureCmd.Kill</c> prefix should do with one kill batch.</summary>
public enum KillInterceptAction
{
    /// <summary>Run vanilla untouched (non-duel rooms, forced kills, non-player batches).</summary>
    PassThrough,

    /// <summary>
    /// Observe the dying players into the outcome tracker, then run vanilla: the
    /// death sticks and the vanilla win-condition check ends the combat. Co-op
    /// mode and opponent-only batches land here (observation only — no state
    /// mutation, so lockstep stays symmetric until T11 settles the outcome).
    /// </summary>
    ObserveOnly,

    /// <summary>
    /// Skip the vanilla kill: observe the deaths, Fate-Thread the local player
    /// back to 1 HP and force-kill the opponent so the vanilla win-condition
    /// path (no alive primary enemy → EndCombatInternal) ends the duel without
    /// ever arming PendingLoss or showing the vanilla failure screen.
    /// Single-machine (fake-opponent harness) local deaths land here.
    /// </summary>
    InterceptLocalDeath,
}

/// <summary>
/// Pure decision table for ticket #19 (T7, spec §7 #4): duel-loss interception
/// and the Fate-Thread safety net. Free of game types so every rule is
/// unit-testable headless (the game Logger static-ctor crash rule).
///
/// Evidence base (decomp v0.111.0):
/// - <c>CombatManager.LoseCombat</c> (CombatManager.cs:1267-1274) is the sole
///   <c>PendingLoss</c> set-point, but it only fires from
///   <c>CreatureCmd.Kill</c>'s all-players-dead branch (CreatureCmd.cs:476-481),
///   and that branch then calls <c>OnEnded(false)</c> +
///   <c>ShowGameOverScreen</c> directly (CreatureCmd.cs:487-488).
/// - In single-machine fake-player duels the fake opponent is never added to
///   <c>RunState.Players</c> (DuelHarness), so every local death reaches that
///   all-dead branch. Intercepting <c>LoseCombat</c> alone therefore cannot
///   keep the run off the failure screen — the kill batch itself must be
///   intercepted before the all-dead evaluation. LoseCombat remains patched as
///   the backstop for abnormal paths (spec §7 #4, "PendingLoss 唯一置位入口").
/// </summary>
public static class LossInterceptPolicy
{
    /// <summary>HP the Fate Thread restores a would-be-dead local player to.</summary>
    public const int FateThreadHp = 1;

    /// <summary>
    /// Decides what to do with one <c>CreatureCmd.Kill(IReadOnlyCollection, bool)</c>
    /// batch observed inside a duel room.
    /// </summary>
    public static KillInterceptAction DecideKill(
        bool isDuelRoom,
        bool isForced,
        bool hasNonPlayerCreature,
        bool hasLocalPlayer,
        bool hasOpponentPlayer,
        bool isSingleMachineDuel)
    {
        // Anything forced (abandon-run semantics: force bypasses death
        // prevention) stays vanilla — a forced batch must keep vanilla rules,
        // including the run-ending screen.
        if (!isDuelRoom || isForced || hasNonPlayerCreature)
        {
            return KillInterceptAction.PassThrough;
        }

        if (!hasLocalPlayer && !hasOpponentPlayer)
        {
            return KillInterceptAction.PassThrough;
        }

        // Single-machine (fake-opponent harness) local death: intercept — the
        // all-dead branch would fire and end the run otherwise.
        if (hasLocalPlayer && isSingleMachineDuel)
        {
            return KillInterceptAction.InterceptLocalDeath;
        }

        // Co-op duels and opponent-only batches: observe only. The co-op local
        // death keeps vanilla mechanics (no local mutation ⇒ no lockstep
        // divergence); T11 settles the mirrored outcome and the synced end.
        return KillInterceptAction.ObserveOnly;
    }

    /// <summary>
    /// Resolves the locally observed deaths into the mirrored outcome record.
    /// Returns null when nothing was observed (not a duel end).
    /// </summary>
    public static DuelResult? ResolveOutcome(
        int actIndex,
        ulong localNetId,
        ulong opponentNetId,
        bool localDied,
        bool opponentDied,
        ulong firstHandNetId)
    {
        if (!localDied && !opponentDied)
        {
            return null;
        }

        if (localDied && opponentDied)
        {
            // Same-turn double death: the first-hand player wins (spec §6). The
            // first-hand value is a placeholder until the timer ticket lands
            // (DuelContext.FirstHandNetId is a stub); when it is not one of the
            // duelists, fall back to the LOWER NetId — a deterministic,
            // end-independent tiebreak so both ends record the same winner.
            var winner = IsDuelist(firstHandNetId, localNetId, opponentNetId)
                ? firstHandNetId
                : Math.Min(localNetId, opponentNetId);
            var loser = winner == localNetId ? opponentNetId : localNetId;
            return new DuelResult(actIndex, winner, loser, DuelEndReason.DoubleDeathFirstHandWins);
        }

        return opponentDied
            ? new DuelResult(actIndex, localNetId, opponentNetId, DuelEndReason.Kill)
            : new DuelResult(actIndex, opponentNetId, localNetId, DuelEndReason.Kill);
    }

    private static bool IsDuelist(ulong netId, ulong a, ulong b) => netId != 0 && (netId == a || netId == b);
}
