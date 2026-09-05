namespace PvpDuel.Combat;

/// <summary>
/// Pure decision logic for ticket #18 (T6): the opponent-turn driver
/// (spec §7 #3). Deliberately free of game singletons so every decision is
/// unit-testable headless (the game Logger static ctor 0xC0000005 rule).
///
/// Turn model: <c>CombatState.Players</c> still contains the flipped duel
/// opponent (docs/is-enemy-consumers.md #63), but the vanilla player-side
/// turn flow must only ever process the LOCAL player — the opponent's setup,
/// play phase and end turn are driven by the ExecuteEnemyTurn replacement on
/// the Enemy side. Every gate below is fed precomputed booleans by the patch
/// class so the decisions stay trivially testable.
/// </summary>
public static class OpponentTurnPolicy
{
    /// <summary>
    /// ExecuteEnemyTurn replacement gate: inside a duel room with a live turn
    /// state the vanilla monster loop is replaced by the opponent-turn driver;
    /// everywhere else the vanilla enemy turn runs untouched (spec §5.1).
    /// </summary>
    public static bool ShouldDriveOpponentTurn(bool isDuelRoom, bool hasTurnState, bool isInProgress) =>
        isDuelRoom && hasTurnState && isInProgress;

    /// <summary>
    /// Vanilla <c>SetupPlayerTurn</c>/<c>RunAutoPrePlayPhase</c> fan-out gate:
    /// in a duel room the flipped opponent is skipped (their setup and phase
    /// transitions happen inside the driver on the Enemy side — mirroring
    /// decomp CombatManager.cs:882-929); the local player keeps vanilla.
    /// </summary>
    public static bool ShouldSkipVanillaPlayerSetup(bool isDuelRoom, bool isLocalPlayer) =>
        isDuelRoom && !isLocalPlayer;

    /// <summary>
    /// End-turn phase one/two gate: vanilla loops over
    /// <c>CombatState.Players</c> (both duel players) when the local player
    /// ends their turn; in a duel the phases must process the local player
    /// only — the opponent's end turn is mirrored by the driver.
    /// </summary>
    public static bool ShouldRunMeOnlyEndTurn(bool isDuelRoom) => isDuelRoom;

    /// <summary>
    /// Duel rule for "all players ready to end turn": the LOCAL player's
    /// readiness alone ends the player side. The vanilla count rule would
    /// demand the opponent's readiness first — a deadlock, because the
    /// opponent can only end their turn during their driven turn on the Enemy
    /// side. <paramref name="isPlayerSideOrSingleplayer"/> mirrors the vanilla
    /// side guard (decomp CombatManager.cs:1067-1075).
    /// </summary>
    public static bool DuelAllPlayersReadyToEndTurn(bool localPlayerReady, bool isPlayerSideOrSingleplayer) =>
        localPlayerReady && isPlayerSideOrSingleplayer;

    /// <summary>
    /// Duel rule for <c>SetReadyToBeginEnemyTurn</c>'s fire condition: the
    /// local player's ready-to-begin alone fires the begin-enemy-turn signal
    /// (vanilla compares the ready count to <c>Players.Count</c>, which
    /// includes the flipped opponent and would never fire in a real MP duel).
    /// </summary>
    public static bool DuelReadyToBeginEnemyTurn(bool isPlayerSide, bool localPlayerReadyToBegin) =>
        isPlayerSide && localPlayerReadyToBegin;

    /// <summary>
    /// Combat-start side assignment: the second-hand machine starts on the
    /// Enemy side so its turn loop hosts turn one as the driven opponent turn —
    /// strict alternation from combat start (spec: 先手先行).
    /// </summary>
    public static bool ShouldStartOnEnemySide(bool isDuelRoom, bool amFirstHand) =>
        isDuelRoom && !amFirstHand;

    /// <summary>
    /// First-hand resolution. A declared first hand (act timer, spec §7 计时层)
    /// wins when non-zero; until that lands, both ends derive the same answer
    /// deterministically — the lower NetId goes first (spec §5.4 determinism,
    /// the seed-fallback variant of the timing tie rule).
    /// </summary>
    public static bool ResolveFirstHandIsMe(ulong? declaredFirstHandNetId, ulong myNetId, ulong opponentNetId) =>
        declaredFirstHandNetId is ulong id && id != 0
            ? id == myNetId
            : myNetId <= opponentNetId;

    /// <summary>
    /// Opponent end-turn wait loop: keep waiting while the opponent has not
    /// readied AND the combat is still alive (teardown/divergence must free
    /// the driver — it re-checks liveness and bails instead of ending a dead
    /// combat's turn).
    /// </summary>
    public static bool ShouldKeepWaitingForOpponentEndTurn(bool opponentReady, bool combatAlive) =>
        !opponentReady && combatAlive;
}
