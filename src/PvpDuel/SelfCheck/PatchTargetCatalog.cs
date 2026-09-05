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

        return manifest;
    }

    /// <summary>Patch-set ids declared by the catalog (single source for ModEntry gating and tests).</summary>
    public static class PatchSetIds
    {
        public const string DuelEntry = RoomSetBossPatch.PatchSetId;
        public const string SideFlip = "SideFlip";
        public const string DuelHarness = "DuelHarness";
        public const string Branching = "Branching";
    }
}
