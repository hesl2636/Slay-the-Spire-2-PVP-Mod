using PvpDuel.Core.SelfCheck;

namespace PvpDuel.SelfCheck;

/// <summary>
/// Declarative registry of every Harmony/hook target the mod patches, grouped by
/// owning patch set. Later tickets append their targets here; the startup
/// self-check walks the list and disables any set whose targets drifted away
/// (game update) instead of crashing.
/// </summary>
public static class PatchTargetCatalog
{
    /// <summary>Builds the manifest. Empty until T3+ register their targets.</summary>
    public static PatchManifest Build()
    {
        var manifest = new PatchManifest();
        // T3+ append entries here, e.g.:
        // manifest.Add(new PatchTargetSpec("DuelCombat", "MegaCrit.Sts2.Core.Combat.CombatManager", "ExecuteEnemyTurn"));
        return manifest;
    }
}
