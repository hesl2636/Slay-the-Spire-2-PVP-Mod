using PvpDuel.Core.SelfCheck;
using PvpDuel.Encounters;

namespace PvpDuel.SelfCheck;

/// <summary>
/// Declarative registry of every Harmony/hook target the mod patches, grouped by
/// owning patch set. Later tickets append their targets here; the startup
/// self-check walks the list and disables any set whose targets drifted away
/// (game update) instead of crashing.
/// </summary>
public static class PatchTargetCatalog
{
    /// <summary>
    /// Builds the manifest.
    /// "DuelEntry" (ticket #15): the <see cref="MegaCrit.Sts2.Core.Rooms.RoomSet.Boss"/>
    /// public setter — the duel entry patch (spec §7 #1). The prefix replacement
    /// is applied manually (not via Harmony attributes) and only when this set
    /// passes the startup self-check.
    /// "SideFlip" (ticket #17): the player-creature constructor side flip
    /// (spec §7 #2) plus the Monster-deref guards — all keyed to the duel scope
    /// and applied manually after the self-check.
    /// "DuelHarness" (ticket #17): the single-machine fake-opponent duel entry
    /// (boss setter second prefix), the no-monster duel generation and the
    /// combat-start injection (spec §9, test-only).
    /// "Branching" (ticket #20, spec §7): the Forked-Road split-path set — vote
    /// convergence replacement, per-end branch retargeting, room-scope
    /// replacement, per-act table reset, checksum branch exclusion and branch
    /// combat scaling. Applied manually via
    /// <see cref="Branching.BranchingPatchSet.Apply"/> when this set passes.
    /// "SaveSideChannel" (ticket #21, spec §4.3/§5.7): the run-save write, the
    /// early multiplayer load probe, the saved-run materialization restore and
    /// the new-run reset. Applied manually via
    /// <see cref="Save.DuelSavePatchSet.Apply"/> when this set passes.
    /// "TurnDrive" (ticket #18, spec §7 #3): the opponent-turn driver — the
    /// ExecuteEnemyTurn replacement plus the vanilla fan-out gates that keep
    /// the player-side turn flow local-player-only while the flipped duel
    /// opponent is driven on the Enemy side. Applied manually via
    /// <see cref="Combat.OpponentTurnDrivePatch.Apply"/> when this set passes.
    /// </summary>
    public static PatchManifest Build()
    {
        var manifest = new PatchManifest();
        manifest.Add(new PatchTargetSpec(
            RoomSetBossPatch.PatchSetId,
            "MegaCrit.Sts2.Core.Rooms.RoomSet",
            "Boss"));

        manifest.AddRange(
        [
            // Creature(Player, int, int) — the side flip postfix target.
            new(PatchSetIds.SideFlip, "MegaCrit.Sts2.Core.Entities.Creatures.Creature", ".ctor",
                ["Player", "Int32", "Int32"]),
            // Monster-deref guards (docs/is-enemy-consumers.md).
            new(PatchSetIds.SideFlip, "MegaCrit.Sts2.Core.Entities.Creatures.Creature", "AfterAddedToRoom"),
            new(PatchSetIds.SideFlip, "MegaCrit.Sts2.Core.Entities.Creatures.Creature", "PrepareForNextTurn",
                ["IEnumerable", "Boolean"]),
            new(PatchSetIds.SideFlip, "MegaCrit.Sts2.Core.Entities.Creatures.Creature", "TakeTurn"),
            new(PatchSetIds.SideFlip, "MegaCrit.Sts2.Core.Combat.CombatManager", "AfterCreatureAdded",
                ["Creature", "CombatState"]),
        ]);

        manifest.AddRange(
        [
            new(PatchSetIds.DuelHarness, "MegaCrit.Sts2.Core.Rooms.RoomSet", "Boss"),
            new(PatchSetIds.DuelHarness, "PvpDuel.Encounters.PvpDuelEncounter", "GenerateMonsters"),
            new(PatchSetIds.DuelHarness, "MegaCrit.Sts2.Core.Rooms.CombatRoom", "StartCombat", ["IRunState"]),
        ]);

        manifest.AddRange(
        [
            new(PatchSetIds.Branching, "MegaCrit.Sts2.Core.Multiplayer.Game.MapSelectionSynchronizer", "MoveToMapCoord"),
            new(PatchSetIds.Branching, "MegaCrit.Sts2.Core.Runs.RunManager", "EnterMapCoord"),
            new(PatchSetIds.Branching, "MegaCrit.Sts2.Core.Runs.RunManager", "LoadIntoLatestMapCoord"),
            new(PatchSetIds.Branching, "MegaCrit.Sts2.Core.Runs.RunManager", "SetActInternal"),
            new(PatchSetIds.Branching, "MegaCrit.Sts2.Core.Rooms.EventRoom", "EnterInternal"),
            new(PatchSetIds.Branching, "MegaCrit.Sts2.Core.Rooms.MerchantRoom", "EnterInternal"),
            new(PatchSetIds.Branching, "MegaCrit.Sts2.Core.Rooms.TreasureRoom", "EnterInternal"),
            new(PatchSetIds.Branching, "MegaCrit.Sts2.Core.Rooms.RestSiteRoom", "EnterInternal"),
            new(PatchSetIds.Branching, "MegaCrit.Sts2.Core.Multiplayer.Game.ChecksumTracker", "GenerateChecksum"),
            new(PatchSetIds.Branching, "MegaCrit.Sts2.Core.Multiplayer.Game.ChecksumTracker", "OnReceivedChecksumDataMessage"),
            new(PatchSetIds.Branching, "MegaCrit.Sts2.Core.Models.Singleton.MultiplayerScalingModel", "ModifyBlockMultiplicative"),
        ]);

        // Ticket #21 (T9): the save side-channel set — run-save write, early
        // multiplayer load probe, saved-run materialization restore (both
        // official paths) and new-run state reset (decomp evidence: SaveManager
        // SaveRun :621 / LoadAndCanonicalizeMultiplayerRunSave :1091;
        // RunManager SetUpSavedSingleplayer :356 / SetUpSavedMultiplayer :384,
        // InitializeNewRun :528 — private).
        manifest.AddRange(
        [
            new(PatchSetIds.SaveSideChannel, "MegaCrit.Sts2.Core.Saves.SaveManager", "SaveRun"),
            new(PatchSetIds.SaveSideChannel, "MegaCrit.Sts2.Core.Saves.SaveManager", "LoadAndCanonicalizeMultiplayerRunSave"),
            new(PatchSetIds.SaveSideChannel, "MegaCrit.Sts2.Core.Runs.RunManager", "SetUpSavedSingleplayer"),
            new(PatchSetIds.SaveSideChannel, "MegaCrit.Sts2.Core.Runs.RunManager", "SetUpSavedMultiplayer"),
            new(PatchSetIds.SaveSideChannel, "MegaCrit.Sts2.Core.Runs.RunManager", "InitializeNewRun"),
        ]);

        // Ticket #18 (T6): the opponent-turn driver set (spec §7 #3). The
        // ExecuteEnemyTurn replacement is the named target; the rest are the
        // minimal vanilla fan-out gates with archived reasons (§12.3 verdict):
        // SetupPlayerTurn/RunAutoPrePlayPhase skip the flipped opponent (its
        // setup is mirrored by the driver); EndPlayerTurnPhaseOne/TwoInternal
        // run local-player-only (the opponent's end turn is mirrored by the
        // driver, vanilla would flush their hand before their driven turn);
        // AllPlayersReadyToEndTurn/SetReadyToBeginEnemyTurn use the duel
        // ready rules (vanilla would deadlock waiting for the opponent's
        // readiness — the opponent is still in state.Players, see
        // docs/is-enemy-consumers.md #63); SetUpCombat assigns the
        // combat-start side so the second-hand machine hosts turn 1.
        manifest.AddRange(
        [
            new(PatchSetIds.TurnDrive, "MegaCrit.Sts2.Core.Combat.CombatManager", "ExecuteEnemyTurn",
                ["CombatTurnState", "Func"]),
            new(PatchSetIds.TurnDrive, "MegaCrit.Sts2.Core.Combat.CombatManager", "SetupPlayerTurn",
                ["CombatTurnState", "Player", "HookPlayerChoiceContext"]),
            new(PatchSetIds.TurnDrive, "MegaCrit.Sts2.Core.Combat.CombatManager", "RunAutoPrePlayPhase",
                ["CombatTurnState", "HookPlayerChoiceContext", "Task", "Player"]),
            new(PatchSetIds.TurnDrive, "MegaCrit.Sts2.Core.Combat.CombatManager", "EndPlayerTurnPhaseOneInternal",
                ["CombatTurnState"]),
            new(PatchSetIds.TurnDrive, "MegaCrit.Sts2.Core.Combat.CombatManager", "EndPlayerTurnPhaseTwoInternal",
                ["CombatTurnState"]),
            new(PatchSetIds.TurnDrive, "MegaCrit.Sts2.Core.Combat.CombatManager", "AllPlayersReadyToEndTurn",
                ["CombatTurnState"]),
            new(PatchSetIds.TurnDrive, "MegaCrit.Sts2.Core.Combat.CombatManager", "SetReadyToBeginEnemyTurn",
                ["Player", "Func"]),
            new(PatchSetIds.TurnDrive, "MegaCrit.Sts2.Core.Combat.CombatManager", "SetUpCombat",
                ["CombatState"]),
        ]);

        // Ticket #19 (T7): duel-loss interception (spec §7 #4) — the kill
        // batch observer/interceptor (decomp evidence: CreatureCmd.Kill
        // IReadOnlyCollection batch overload :461; the all-players-dead branch
        // :476-489 calls OnEnded(false)+ShowGameOverScreen directly, so the
        // batch must be intercepted before the all-dead evaluation) and the
        // LoseCombat backstop on the PendingLoss sole set-point
        // (CombatManager.cs:1267-1274).
        manifest.AddRange(
        [
            new(PatchSetIds.LossIntercept, "MegaCrit.Sts2.Core.Commands.CreatureCmd", "Kill",
                ["IReadOnlyCollection", "Boolean"]),
            new(PatchSetIds.LossIntercept, "MegaCrit.Sts2.Core.Combat.CombatManager", "LoseCombat"),
        ]);

        return manifest;
    }

    /// <summary>Patch-set ids declared by the catalog (single source for ModEntry gating and tests).</summary>
    public static class PatchSetIds
    {
        public const string DuelEntry = RoomSetBossPatch.PatchSetId;
        public const string SideFlip = "SideFlip";
        public const string DuelHarness = "DuelHarness";
        public const string Branching = "Branching";
        public const string SaveSideChannel = "SaveSideChannel";
        public const string LossIntercept = "LossIntercept";
        public const string TurnDrive = "TurnDrive";
    }
}
