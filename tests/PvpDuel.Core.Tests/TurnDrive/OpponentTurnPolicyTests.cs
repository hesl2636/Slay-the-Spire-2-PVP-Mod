using PvpDuel.Combat;

using Xunit;

namespace PvpDuel.Core.Tests.TurnDrive;

/// <summary>
/// Ticket #18 (T6): pure decisions for the opponent-turn driver (spec §7 #3,
/// §12.3 verdict). Headless — no game singletons, no game DLL needed (the game
/// Logger static ctor 0xC0000005 rule).
///
/// The driver replaces <c>CombatManager.ExecuteEnemyTurn</c> inside duel rooms
/// and mirrors <c>SetupPlayerTurn</c> for the player creature sitting on the
/// current (Enemy) side — the flipped duel opponent. Vanilla fan-out points
/// that iterate <c>CombatState.Players</c> (which still contains the flipped
/// opponent — docs/is-enemy-consumers.md #63) are gated to the local player
/// only, so turns strictly alternate: local player turn (Player side) ↔
/// opponent turn (Enemy side, driven).
/// </summary>
public class OpponentTurnPolicyTests
{
    // --- ExecuteEnemyTurn replacement gate ---------------------------------

    [Theory]
    [InlineData(false, true, true, false)] // non-duel room: vanilla enemy turn (monsters)
    [InlineData(true, false, true, false)] // no turn state: nothing to drive
    [InlineData(true, true, false, false)] // combat torn down / already ended
    [InlineData(true, true, true, true)]   // duel + live combat: drive the opponent
    public void ShouldDriveOpponentTurn_GatesOnDuelRoomAndLiveTurnState(
        bool isDuelRoom,
        bool hasTurnState,
        bool isInProgress,
        bool expected)
    {
        Assert.Equal(expected, OpponentTurnPolicy.ShouldDriveOpponentTurn(isDuelRoom, hasTurnState, isInProgress));
    }

    // --- vanilla player-setup fan-out gate (SetupPlayerTurn / RunAutoPrePlayPhase) ---

    [Theory]
    [InlineData(false, false, false)] // non-duel: vanilla sets up everyone
    [InlineData(false, true, false)]  // non-duel: vanilla even for me
    [InlineData(true, true, false)]   // duel: my setup stays vanilla
    [InlineData(true, false, true)]   // duel: the flipped opponent's setup belongs to the driver
    public void ShouldSkipVanillaPlayerSetup_SkipsOnlyDuelOpponent(
        bool isDuelRoom,
        bool isLocalPlayer,
        bool expected)
    {
        Assert.Equal(expected, OpponentTurnPolicy.ShouldSkipVanillaPlayerSetup(isDuelRoom, isLocalPlayer));
    }

    // --- end-turn phase one/two: local player only --------------------------

    [Fact]
    public void ShouldRunMeOnlyEndTurn_OnlyInDuelRooms()
    {
        Assert.False(OpponentTurnPolicy.ShouldRunMeOnlyEndTurn(isDuelRoom: false));
        Assert.True(OpponentTurnPolicy.ShouldRunMeOnlyEndTurn(isDuelRoom: true));
    }

    // --- ready predicates (strict alternation) ------------------------------
    // With Players = {me, opponent} the vanilla "all ready" rule would demand
    // the opponent's readiness before switching sides — a deadlock, because the
    // opponent can only end their turn during their driven turn on the Enemy
    // side. The duel rule: the local player's readiness alone ends the player
    // side.

    [Theory]
    [InlineData(false, true, false)] // I have not readied: keep the player side
    [InlineData(true, false, false)] // readied but the side already moved on (stale MP delivery)
    [InlineData(true, true, true)]   // I readied during the player side: switch
    public void DuelAllPlayersReadyToEndTurn_LocalReadinessEndsPlayerSide(
        bool localPlayerReady,
        bool isPlayerSideOrSingleplayer,
        bool expected)
    {
        Assert.Equal(
            expected,
            OpponentTurnPolicy.DuelAllPlayersReadyToEndTurn(localPlayerReady, isPlayerSideOrSingleplayer));
    }

    [Theory]
    [InlineData(false, true, false)] // not on the player side anymore: never fire
    [InlineData(true, false, false)] // the local player has not readied to begin
    [InlineData(true, true, true)]   // fire: begin the (driven) enemy turn
    public void DuelReadyToBeginEnemyTurn_LocalReadinessBeginsEnemyTurn(
        bool isPlayerSide,
        bool localPlayerReadyToBegin,
        bool expected)
    {
        Assert.Equal(
            expected,
            OpponentTurnPolicy.DuelReadyToBeginEnemyTurn(isPlayerSide, localPlayerReadyToBegin));
    }

    // --- combat-start side assignment (spec: 先手先行, strict alternation) ---

    [Theory]
    [InlineData(false, false, false)] // non-duel: vanilla always starts on the player side
    [InlineData(true, true, false)]   // first hand: player side (vanilla default)
    [InlineData(true, false, true)]   // second hand: enemy side, so the driver hosts turn 1
    public void ShouldStartOnEnemySide_SecondHandMachineHostsTurnOne(bool isDuelRoom, bool amFirstHand, bool expected)
    {
        Assert.Equal(expected, OpponentTurnPolicy.ShouldStartOnEnemySide(isDuelRoom, amFirstHand));
    }

    // --- first-hand resolution: declared (act timer) wins, deterministic fallback ---

    [Fact]
    public void ResolveFirstHandIsMe_DeclaredFirstHandWins()
    {
        Assert.True(OpponentTurnPolicy.ResolveFirstHandIsMe(2, myNetId: 2, opponentNetId: 1));
        Assert.False(OpponentTurnPolicy.ResolveFirstHandIsMe(1, myNetId: 2, opponentNetId: 1));
    }

    [Fact]
    public void ResolveFirstHandIsMe_UnsetFallsBackToLowerNetId_BothEndsAgree()
    {
        // Deterministic tie-break (spec §5.4): with no declared first hand yet,
        // both machines must derive the same answer — the lower NetId goes first.
        Assert.True(OpponentTurnPolicy.ResolveFirstHandIsMe(null, myNetId: 1, opponentNetId: 2));
        Assert.False(OpponentTurnPolicy.ResolveFirstHandIsMe(null, myNetId: 2, opponentNetId: 1));
        Assert.True(OpponentTurnPolicy.ResolveFirstHandIsMe(0, myNetId: 1, opponentNetId: 2)); // 0 = unset sentinel
        Assert.False(OpponentTurnPolicy.ResolveFirstHandIsMe(0, myNetId: 2, opponentNetId: 1));
    }

    // --- opponent end-turn wait ---------------------------------------------

    [Theory]
    [InlineData(false, true, true)]  // not readied + combat alive: keep waiting
    [InlineData(true, true, false)]  // opponent readied: proceed to the mirrored end turn
    [InlineData(false, false, false)] // combat torn down (divergence/forfeit): bail out
    public void ShouldKeepWaitingForOpponentEndTurn_StopsOnReadinessOrTeardown(
        bool opponentReady,
        bool combatAlive,
        bool expected)
    {
        Assert.Equal(
            expected,
            OpponentTurnPolicy.ShouldKeepWaitingForOpponentEndTurn(opponentReady, combatAlive));
    }
}
