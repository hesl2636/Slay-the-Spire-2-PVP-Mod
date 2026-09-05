using System.Reflection;

using HarmonyLib;

using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

using PvpDuel.Duel;
using PvpDuel.Logging;

namespace PvpDuel.Encounters;

/// <summary>
/// Spec §7 patch #1 — the duel entry point. Prefix on the public
/// <c>RoomSet.Boss</c> setter (RoomSet.cs:56-66): after the game rolls a vanilla
/// boss for an act, the assigned value is replaced with the duel encounter in
/// 2-player co-op. Without this patch nothing downstream (combat patches,
/// settlement) ever triggers, because they all gate on the duel encounter type.
///
/// Not attribute-patched: ModEntry applies it manually so the startup self-check
/// can disable the whole "DuelEntry" set when the target drifted (game update).
/// </summary>
internal static class RoomSetBossPatch
{
    public const string PatchSetId = "DuelEntry";

    private static readonly HarmonyMethod PrefixMethod = new(
        typeof(RoomSetBossPatch).GetMethod(
            nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("RoomSetBossPatch.Prefix missing."));

    /// <summary>Applies the patch; throws when the target is gone (caller disables the set).</summary>
    public static void Apply(Harmony harmony)
    {
        var setter = AccessTools.PropertySetter(typeof(RoomSet), nameof(RoomSet.Boss))
            ?? throw new MissingMethodException(typeof(RoomSet).FullName, "set_Boss");
        harmony.Patch(setter, prefix: PrefixMethod);
        PvpDuelLog.Info("RoomSet.Boss setter prefix applied (duel entry patch).");
    }

    /// <summary>
    /// Runs before the setter body; overwriting <c>value</c> (declared by ref)
    /// changes what the game stores in the backing field.
    /// </summary>
    private static void Prefix(ref EncounterModel value)
    {
        try
        {
            if (value == null)
            {
                return;
            }

            BossSwap.ReplaceIfDuelRun(ref value, DuelScope.DuelRunActive);
        }
        catch (Exception ex)
        {
            // Entry patch must never break act generation.
            PvpDuelLog.Error($"RoomSet.Boss prefix failed: {ex}");
        }
    }
}
