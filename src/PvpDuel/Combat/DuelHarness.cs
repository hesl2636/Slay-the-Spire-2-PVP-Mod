using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

using PvpDuel.Duel;
using PvpDuel.Logging;

namespace PvpDuel.Combat;

/// <summary>
/// Ticket #17 (T5) single-machine fake-opponent duel harness (spec §9 集成层,
/// test-only). Two parts:
///
/// 1. <see cref="DebugSinglePlayerDuel"/> — a runtime debug flag (deliberately
///    NOT part of PvpConfig, so it can never leak into the config hash). When
///    on, the DuelHarness patch set swaps the act boss for the duel encounter
///    in singleplayer too and spawns a fake opponent player creature instead of
///    placeholder monsters, giving a local two-player duel room. Console entry:
///    the <c>pvpduel</c> command (auto-registered by the game via
///    ReflectionHelper.GetSubtypesInMods&lt;AbstractConsoleCmd&gt;).
/// 2. <see cref="InjectFakeOpponent"/> — constructs the fake opponent (same
///    character as the local player, distinct NetId) and attaches its creature
///    to the duel room's CombatState BEFORE combat setup, so NCombatRoom's
///    CreateEnemyNodes gives it a real node (targetable, positioned on the
///    enemy side via the Side flip from <see cref="CreatureSideFlipPatch"/>).
///
/// Attended steps live in docs/is-enemy-consumers.md § "假玩家对决 attended 步骤".
/// Nothing here writes to save files or run state: the fake player is a combat
/// local, never added to RunState.Players.
/// </summary>
public static class DuelHarness
{
    /// <summary>Fake opponent NetId when the local player is not already using it.</summary>
    public const ulong DefaultFakeNetId = 2;

    /// <summary>
    /// Debug flag: when true, singleplayer runs treat the act boss room as a duel
    /// room with a fake opponent. Set via the <c>pvpduel on/off</c> console
    /// command; must be enabled before the run starts (act rooms generate at
    /// run creation).
    /// </summary>
    public static bool DebugSinglePlayerDuel { get; set; }

    /// <summary>
    /// Creates the fake opponent for the given duel combat state. No-op (null)
    /// unless the harness debug flag is on. The Side flip itself is performed by
    /// the SideFlip ctor postfix (the creature is constructed inside the duel
    /// room), which this method then asserts before adding it to the Enemies
    /// bucket.
    /// </summary>
    public static Creature? InjectFakeOpponent(CombatState combatState)
    {
        if (!DebugSinglePlayerDuel || combatState == null)
        {
            return null;
        }

        var runState = RunManager.Instance.DebugOnlyGetState();
        var me = runState == null ? null : LocalContext.GetMe(runState);
        if (me == null)
        {
            PvpDuelLog.Warn("harness: no local player; fake opponent skipped.");
            return null;
        }
        if (combatState.Creatures.Any(c => c.IsPlayer && !LocalContext.IsMe(c)))
        {
            PvpDuelLog.Info("harness: fake opponent already present; skipping.");
            return null;
        }

        var fake = Player.CreateForNewRun(me.Character, SaveManager.Instance.GenerateUnlockStateFromProgress(), FakeNetIdFor(me));
        fake.RunState = runState!;
        fake.ResetCombatState();

        var creature = fake.Creature;
        if (creature.Side != CombatSide.Enemy)
        {
            // The ctor postfix flips the fake creature inside the duel room; if it
            // did not fire, adding a Player-sided creature to the duel would break
            // the split — fail loudly instead.
            throw new InvalidOperationException(
                "harness: fake opponent creature was not flipped to Enemy (SideFlip postfix did not fire).");
        }
        creature.CombatState = combatState;
        creature.CombatId = NextCombatId(combatState);

        // CombatState.AddCreature buckets by Side (enemies); SetUpCombat then
        // subscribes it (CombatManager.AddCreature) and the turn-loop setup runs
        // the guarded AfterCreatureAdded. No CombatManager.AddCreature call here:
        // the prefix-injected creature is picked up by SetUpCombat itself.
        combatState.AddCreature(creature);

        PvpDuelLog.Info($"harness: fake opponent spawned ({fake.Character.Id.Entry}, netId {fake.NetId}, side {creature.Side}); enemies bucket: {combatState.Enemies.Count}.");
        return creature;
    }

    private static ulong FakeNetIdFor(Player me) =>
        me.NetId == DefaultFakeNetId ? DefaultFakeNetId + 1 : DefaultFakeNetId;

    private static uint NextCombatId(CombatState combatState) =>
        combatState.Creatures.Select(c => c.CombatId).Max(c => c ?? 0) + 1;
}

/// <summary>
/// Console wrapper for the harness (DebugOnly ⇒ modded console only, per
/// NDevConsole.cs:359 shouldAllowDebugCommands). Subcommands:
/// on | off | status | spawn.
/// </summary>
public sealed class DuelHarnessConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "pvpduel";

    public override string Args => "on|off|status|spawn";

    public override string Description =>
        "PvP Duel harness: 'on' enables the single-machine fake-opponent duel (set before starting a run); " +
        "'spawn' injects the fake opponent (debug; also happens automatically at duel combat start); " +
        "'status' prints the flag.";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var sub = args.Length == 0 ? "status" : args[0].ToLowerInvariant();
        switch (sub)
        {
            case "on":
                DuelHarness.DebugSinglePlayerDuel = true;
                return new CmdResult(true, "pvpduel harness ON — start a run; act boss rooms become duel rooms with a fake opponent.");
            case "off":
                DuelHarness.DebugSinglePlayerDuel = false;
                return new CmdResult(true, "pvpduel harness OFF.");
            case "status":
                return new CmdResult(true, $"pvpduel harness: {(DuelHarness.DebugSinglePlayerDuel ? "ON" : "off")}.");
            case "spawn":
                return Spawn();
            default:
                return new CmdResult(false, $"unknown subcommand '{args[0]}' (use on|off|status|spawn)");
        }
    }

    private static CmdResult Spawn()
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
        {
            return new CmdResult(false, "not in a run");
        }
        if (!DuelScope.IsDuelRoom)
        {
            return new CmdResult(false, "not in a duel room (enable 'pvpduel on' before starting the run, then enter the act boss room)");
        }
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        if (combatState == null)
        {
            return new CmdResult(false, "no live combat state");
        }
        var creature = DuelHarness.InjectFakeOpponent(combatState);
        return creature == null
            ? new CmdResult(false, "fake opponent not spawned (see log; likely already present)")
            : new CmdResult(true, $"fake opponent spawned: {creature.LogName} (side {creature.Side}, hp {creature.CurrentHp}/{creature.MaxHp})");
    }
}
