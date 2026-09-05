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
    /// </summary>
    public static PatchManifest Build()
    {
        var manifest = new PatchManifest();
        manifest.Add(new PatchTargetSpec(
            RoomSetBossPatch.PatchSetId,
            "MegaCrit.Sts2.Core.Rooms.RoomSet",
            "Boss"));
        return manifest;
    }
}
