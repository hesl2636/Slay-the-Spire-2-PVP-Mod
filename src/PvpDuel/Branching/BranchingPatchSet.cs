using System.Reflection;

using HarmonyLib;

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Singleton;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Checksums;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

using PvpDuel.Logging;

namespace PvpDuel.Branching;

/// <summary>
/// Ticket #20 branch patch set (spec §7, Forked Road mode). Every target is
/// declared in <see cref="PvpDuel.SelfCheck.PatchTargetCatalog"/> under the
/// "Branching" set; the set is applied manually (never attribute-patched) so a
/// startup self-check failure disables it instead of crashing.
///
/// Gates: every entry checks <see cref="BranchSync.Active"/> /
/// <see cref="BranchSync.ValidationExcluded"/> first; anything else passes
/// straight to vanilla — co-op main line and singleplayer behavior are
/// untouched (spec §5.1 red line).
/// </summary>
internal static class BranchingPatchSet
{
    public const string PatchSetId = "Branching";

    private static MethodInfo? _enterMapCoordInternal;

    /// <summary>Applies the whole set; throws when a target is gone (caller disables the set).</summary>
    public static void Apply(Harmony harmony)
    {
        (MethodBase, string, string)[] prefixes =
        [
            (MapSelectionPatch(), nameof(MoveToMapCoordPrefix), "MapSelectionSynchronizer.MoveToMapCoord"),
            (EnterMapCoordPatch(), nameof(EnterMapCoordPrefix), "RunManager.EnterMapCoord"),
            (LoadIntoLatestMapCoordPatch(), nameof(LoadIntoLatestMapCoordPrefix), "RunManager.LoadIntoLatestMapCoord"),
            (RoomScopePatch(typeof(EventRoom)), nameof(RoomEnterInternalPrefix), "EventRoom.EnterInternal"),
            (RoomScopePatch(typeof(MerchantRoom)), nameof(RoomEnterInternalPrefix), "MerchantRoom.EnterInternal"),
            (RoomScopePatch(typeof(TreasureRoom)), nameof(RoomEnterInternalPrefix), "TreasureRoom.EnterInternal"),
            (RoomScopePatch(typeof(RestSiteRoom)), nameof(RoomEnterInternalPrefix), "RestSiteRoom.EnterInternal"),
            (ChecksumGeneratePatch(), nameof(GenerateChecksumPrefix), "ChecksumTracker.GenerateChecksum"),
            (ChecksumReceivePatch(), nameof(OnReceivedChecksumDataPrefix), "ChecksumTracker.OnReceivedChecksumDataMessage"),
        ];
        foreach (var (target, prefixName, label) in prefixes)
        {
            harmony.Patch(target, prefix: HarmonyMethodOf(prefixName));
            PvpDuelLog.Info($"patched: {label}.");
        }

        (MethodBase, string, string)[] postfixes =
        [
            (SetActInternalPatch(), nameof(SetActInternalPostfix), "RunManager.SetActInternal"),
            (ScalingPatch(), nameof(ModifyBlockMultiplicativePostfix), "MultiplayerScalingModel.ModifyBlockMultiplicative"),
        ];
        foreach (var (target, postfixName, label) in postfixes)
        {
            harmony.Patch(target, postfix: HarmonyMethodOf(postfixName));
            PvpDuelLog.Info($"patched: {label}.");
        }

        PvpDuelLog.Info("Branching patch set applied (11 targets).");
    }

    private static HarmonyMethod HarmonyMethodOf(string name) => new(
        typeof(BranchingPatchSet).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"BranchingPatchSet.{name} missing."));

    private static MethodBase MapSelectionPatch() =>
        AccessTools.Method(typeof(MapSelectionSynchronizer), "MoveToMapCoord")
        ?? throw new MissingMethodException(nameof(MapSelectionSynchronizer), "MoveToMapCoord");

    private static MethodBase EnterMapCoordPatch() =>
        AccessTools.Method(typeof(RunManager), nameof(RunManager.EnterMapCoord), [typeof(MapCoord)])
        ?? throw new MissingMethodException(nameof(RunManager), "EnterMapCoord");

    private static MethodBase LoadIntoLatestMapCoordPatch() =>
        AccessTools.Method(typeof(RunManager), nameof(RunManager.LoadIntoLatestMapCoord))
        ?? throw new MissingMethodException(nameof(RunManager), "LoadIntoLatestMapCoord");

    private static MethodBase SetActInternalPatch() =>
        AccessTools.Method(typeof(RunManager), nameof(RunManager.SetActInternal))
        ?? throw new MissingMethodException(nameof(RunManager), "SetActInternal");

    private static MethodBase RoomScopePatch(Type roomType) =>
        AccessTools.Method(roomType, "EnterInternal")
        ?? throw new MissingMethodException(roomType.FullName, "EnterInternal");

    private static MethodBase ChecksumGeneratePatch() =>
        AccessTools.Method(typeof(ChecksumTracker), nameof(ChecksumTracker.GenerateChecksum))
        ?? throw new MissingMethodException(nameof(ChecksumTracker), "GenerateChecksum");

    private static MethodBase ChecksumReceivePatch() =>
        AccessTools.Method(typeof(ChecksumTracker), "OnReceivedChecksumDataMessage")
        ?? throw new MissingMethodException(nameof(ChecksumTracker), "OnReceivedChecksumDataMessage");

    private static MethodBase ScalingPatch() =>
        AccessTools.Method(
            typeof(MultiplayerScalingModel),
            nameof(MultiplayerScalingModel.ModifyBlockMultiplicative),
            [typeof(Creature), typeof(decimal), typeof(ValueProp), typeof(CardModel), typeof(CardPlay)])
        ?? throw new MissingMethodException(nameof(MultiplayerScalingModel), "ModifyBlockMultiplicative");

    // ---- Prefix/postfix bodies (all fail-safe: gate first, never throw) ----

    /// <summary>
    /// Forked Road vote convergence replacement: skip vanilla's "one shared
    /// coordinate" move; group the votes into branches, publish host-authoritative
    /// branch states, and enqueue the local (host) player's own branch travel.
    /// Falling back to vanilla on unexpected failure keeps the session alive.
    /// </summary>
    private static bool MoveToMapCoordPrefix()
    {
        BranchSync.EnsureSubscribed();
        if (!BranchSync.Active)
        {
            return true;
        }

        try
        {
            if (!BranchSync.ResolveFromVotes())
            {
                return true;
            }

            BranchSync.EnqueueLocalTravel();
            return false;
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"branch resolve failed; falling back to vanilla convergence: {ex}");
            return true;
        }
    }

    /// <summary>
    /// Per-end branch retargeting: while branches are diverged, every travel
    /// (including the shared MoveToMapCoordAction fan-out) lands on this end's
    /// own branch coordinate. Boss-wait is marked after redirection.
    private static void EnterMapCoordPrefix(ref MapCoord coord)
    {
        BranchSync.EnsureSubscribed();
        try
        {
            if (BranchSync.Redirect(coord) is { } target)
            {
                PvpDuelLog.Info($"branch travel: {coord} -> own branch ({target.col},{target.row}).");
                coord = target;
            }

            BranchSync.OnEnteredCoord(coord);
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"EnterMapCoord branch prefix failed: {ex}");
        }
    }

    /// <summary>
    /// Room-stack restore (load path): re-enter through the private
    /// <c>EnterMapCoordInternal</c> with this end's own branch coordinate,
    /// preserving the vanilla <c>saveGame:false</c> semantics.
    /// </summary>
    private static bool LoadIntoLatestMapCoordPrefix(AbstractRoom? preFinishedRoom, ref Task __result)
    {
        try
        {
            if (BranchSync.RedirectLatest() is not { } target)
            {
                return true;
            }

            _enterMapCoordInternal ??= AccessTools.Method(typeof(RunManager), "EnterMapCoordInternal")
                ?? throw new MissingMethodException(nameof(RunManager), "EnterMapCoordInternal");
            PvpDuelLog.Info($"branch restore: entering own branch ({target.col},{target.row}).");
            __result = (Task)_enterMapCoordInternal.Invoke(
                RunManager.Instance, [target, preFinishedRoom, false])!;
            return false;
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"LoadIntoLatestMapCoord branch prefix failed: {ex}");
            return true;
        }
    }

    private static void SetActInternalPostfix(int actIndex)
    {
        try
        {
            BranchSync.OnActChanged(actIndex);
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"SetActInternal branch postfix failed: {ex}");
        }
    }

    /// <summary>Room-scope replacement: branch rooms see a branch-scoped run state view.</summary>
    private static void RoomEnterInternalPrefix(ref IRunState? runState)
    {
        try
        {
            runState = BranchSync.ScopeRoom(runState);
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"room scope prefix failed: {ex}");
        }
    }

    /// <summary>
    /// Checksum exclusion (spec §5.5): while branches are diverged neither end
    /// generates checksums — the mirrored table makes both ends skip the exact
    /// same call sites, so checksum ids stay aligned for reconvergence.
    /// </summary>
    private static bool GenerateChecksumPrefix(ref NetChecksumData __result)
    {
        if (!BranchSync.ValidationExcluded)
        {
            return true;
        }

        __result = default;
        return false;
    }

    /// <summary>Safety net on the host: never compare a checksum from a diverged sender.</summary>
    private static bool OnReceivedChecksumDataPrefix(ChecksumDataMessage message, ulong senderId)
    {
        if (!BranchSync.ValidationExcluded)
        {
            return true;
        }

        PvpDuelLog.Info($"checksum: dropping message from diverged sender {senderId}.");
        return false;
    }

    /// <summary>
    /// Branch combat scaling (spec §12.2): while diverged only the branch's
    /// single player fights this combat — block scaling must not double for the
    /// absent peer (vanilla multiplies by <c>Players.Count</c>, rule
    /// <see cref="PvpDuel.Core.Branching.BranchExclusion.CombatParticipantCount"/>).
    /// </summary>

    private static void ModifyBlockMultiplicativePostfix(ref decimal __result)
    {
        try
        {
            if (BranchSync.ValidationExcluded)
            {
                __result = 1m;
            }
        }
        catch (Exception ex)
        {
            PvpDuelLog.Error($"scaling postfix failed: {ex}");
        }
    }
}
