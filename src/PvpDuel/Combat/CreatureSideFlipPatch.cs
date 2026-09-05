using System.Reflection;

using HarmonyLib;

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;

using PvpDuel.Duel;
using PvpDuel.Logging;

namespace PvpDuel.Combat;

/// <summary>
/// Ticket #17 (T5) patch set "SideFlip" — spec §7 #2 side flip plus the
/// Monster-deref guards from the docs/is-enemy-consumers.md scan.
///
/// Not attribute-patched: ModEntry applies the set manually, only after the
/// startup self-check confirmed every target (same pattern as RoomSetBossPatch).
/// Every guard is gated on <see cref="DuelScope.IsDuelRoom"/> so non-duel combat
/// is untouched (acceptance #2: vanilla Creature behavior zero change).
/// </summary>
internal static class CreatureSideFlipPatch
{
    public const string PatchSetId = "SideFlip";

    /// <summary>Applies the set; throws when a target is gone (caller disables the set).</summary>
    public static void Apply(Harmony harmony)
    {
        var ctor = AccessTools.Constructor(typeof(Creature), [typeof(Player), typeof(int), typeof(int)])
            ?? throw new MissingMethodException(typeof(Creature).FullName, ".ctor(Player, int, int)");
        harmony.Patch(ctor, postfix: Patch(nameof(PlayerCtorPostfix)));

        harmony.Patch(
            RequireCreatureMethod(nameof(Creature.AfterAddedToRoom)),
            prefix: Patch(nameof(AfterAddedToRoomPrefix)));

        harmony.Patch(
            RequireCreatureMethod(nameof(Creature.PrepareForNextTurn)),
            prefix: Patch(nameof(PrepareForNextTurnPrefix)));

        harmony.Patch(
            RequireCreatureMethod(nameof(Creature.TakeTurn)),
            prefix: Patch(nameof(TakeTurnPrefix)));

        // Private turn-state-relative overload AfterCreatureAdded(Creature, CombatState);
        // the parameter list separates it from the public instance AfterCreatureAdded(Creature).
        var afterAdded = AccessTools.Method(
                typeof(CombatManager), "AfterCreatureAdded",
                [typeof(Creature), typeof(CombatState)])
            ?? throw new MissingMethodException(typeof(CombatManager).FullName, "AfterCreatureAdded(Creature, CombatState)");
        harmony.Patch(afterAdded, prefix: Patch(nameof(AfterCreatureAddedPrefix)));

        PvpDuelLog.Info("SideFlip patch set applied (ctor postfix + 3 creature guards + roll-move guard).");
    }

    private static HarmonyMethod Patch(string name)
    {
        var method = typeof(CreatureSideFlipPatch).GetMethod(
                name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(CreatureSideFlipPatch).FullName, name);
        return new HarmonyMethod(method);
    }

    private static MethodInfo RequireCreatureMethod(string name) =>
        AccessTools.Method(typeof(Creature), name)
            ?? throw new MissingMethodException(typeof(Creature).FullName, name);

    /// <summary>
    /// Creature(Player, currentHp, maxHp) postfix: the duel opponent's creature is
    /// constructed with Side=Player like any other; inside a duel room every
    /// player creature that is not "me" is the opponent — rewrite the backing
    /// field to Enemy so AddCreature buckets it into the Enemies side
    /// (decomp CombatState.cs:718-731) and every IsEnemy consumer treats it as
    /// the opponent.
    /// </summary>
    private static void PlayerCtorPostfix(Creature __instance, Player? player)
    {
        try
        {
            if (player == null || !DuelScope.IsDuelRoom || LocalContext.IsMe(player))
            {
                return;
            }
            if (CreatureSideFlip.FlipToEnemy(__instance))
            {
                PvpDuelLog.Info($"side flip: {__instance.LogName} (netId {player.NetId}) -> Enemy (duel opponent).");
            }
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"player-creature side flip failed: {ex}");
        }
    }

    /// <summary>Task-returning prefix: CompletedTask skips the original (no NRE on Monster), null runs vanilla.</summary>

    private static Task? AfterAddedToRoomPrefix(Creature __instance)
    {
        if (!SideFlipPolicy.ShouldSkipAfterAddedToRoom(DuelScope.IsDuelRoom, __instance.IsPlayer, __instance.Side))
        {
            return null;
        }
        return Task.CompletedTask;
    }

    /// <summary>Void original: bool false skips the Monster.MoveStateMachine read.</summary>
    private static bool PrepareForNextTurnPrefix(Creature __instance)
    {
        return !SideFlipPolicy.ShouldSkipPrepareForNextTurn(DuelScope.IsDuelRoom, __instance.IsPlayer, __instance.Side);
    }

    /// <summary>Vanilla TakeTurn throws for non-monster enemies; skip the flipped opponent's automated turn.</summary>
    private static Task? TakeTurnPrefix(Creature __instance)
    {
        if (!SideFlipPolicy.ShouldSkipTakeTurn(DuelScope.IsDuelRoom, __instance.IsPlayer, __instance.Side))
        {
            return null;
        }
        return Task.CompletedTask;
    }

    /// <summary>Skips the creature.Monster.RollMove call for the flipped opponent; the AfterAddedToRoom line is a guarded no-op.</summary>
    private static Task? AfterCreatureAddedPrefix(Creature creature, CombatState state)
    {
        if (!SideFlipPolicy.ShouldSkipMonsterRollMove(DuelScope.IsDuelRoom, creature.IsPlayer, creature.Side))
        {
            return null;
        }
        return Task.CompletedTask;
    }
}
