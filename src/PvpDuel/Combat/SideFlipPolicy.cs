using MegaCrit.Sts2.Core.Combat;

namespace PvpDuel.Combat;

/// <summary>
/// Pure decision logic for ticket #17 (T5): side flip + Monster-deref guards.
/// Deliberately free of game singletons so every decision is unit-testable
/// headless (the game Logger static ctor 0xC0000005 rule).
///
/// A "flipped opponent" is a player creature whose backing <c>Side</c> field
/// was rewritten to <see cref="CombatSide.Enemy"/> by the duel (spec §7 #2).
/// It has <c>Monster == null</c>, so every vanilla consumer that assumes
/// "Side == Enemy ⇒ Monster != null" must be guarded — see
/// docs/is-enemy-consumers.md for the full scanned consumption list.
/// </summary>
public static class SideFlipPolicy
{
    /// <summary>Is this creature the flipped duel opponent (player creature on the Enemy side)?</summary>
    public static bool IsFlippedOpponent(bool isDuelRoom, bool isPlayerCreature, CombatSide side) =>
        isDuelRoom && isPlayerCreature && side == CombatSide.Enemy;

    /// <summary>
    /// Creature(Player, currentHp, maxHp) ctor postfix: a player creature constructed
    /// while inside a duel room belongs to the opponent unless it is "me"
    /// (<see cref="MegaCrit.Sts2.Core.Context.LocalContext.IsMe"/>) — flip its Side.
    /// </summary>
    public static bool ShouldFlipConstructedSide(bool isDuelRoom, bool isMe) =>
        isDuelRoom && !isMe;

    /// <summary>
    /// Creature.AfterAddedToRoom guard: vanilla dereferences <c>Monster</c> on the
    /// Enemy side (decomp Creature.cs:415-421); a flipped opponent has none.
    /// </summary>
    public static bool ShouldSkipAfterAddedToRoom(bool isDuelRoom, bool isPlayerCreature, CombatSide side) =>
        IsFlippedOpponent(isDuelRoom, isPlayerCreature, side);

    /// <summary>
    /// Creature.PrepareForNextTurn guard: vanilla reads
    /// <c>Monster.MoveStateMachine</c> unconditionally (decomp Creature.cs:547-553)
    /// and is also called for every enemy at the player-side turn start
    /// (decomp CombatManager.cs:743-746).
    /// </summary>
    public static bool ShouldSkipPrepareForNextTurn(bool isDuelRoom, bool isPlayerCreature, CombatSide side) =>
        IsFlippedOpponent(isDuelRoom, isPlayerCreature, side);

    /// <summary>
    /// Creature.TakeTurn guard: vanilla throws when the creature is not a monster
    /// (decomp Creature.cs:716-721) and the enemy turn loop calls it for every
    /// entry of the Enemies bucket (decomp CombatManager.cs:1419-1428). A flipped
    /// opponent would crash the enemy turn — skip it instead (it is a player: its
    /// "turn" is the human opponent's, which lands with the net-combat ticket).
    /// </summary>
    public static bool ShouldSkipTakeTurn(bool isDuelRoom, bool isPlayerCreature, CombatSide side) =>
        IsFlippedOpponent(isDuelRoom, isPlayerCreature, side);

    /// <summary>
    /// CombatManager.AfterCreatureAdded(Creature, CombatState) guard: vanilla calls
    /// <c>creature.Monster.RollMove(...)</c> for every enemy when the player side
    /// starts (decomp CombatManager.cs:1130-1137). Skip for the flipped opponent;
    /// the body's first line (AfterAddedToRoom) is a guarded no-op for it anyway.
    /// </summary>
    public static bool ShouldSkipMonsterRollMove(bool isDuelRoom, bool isPlayerCreature, CombatSide side) =>
        IsFlippedOpponent(isDuelRoom, isPlayerCreature, side);
}
