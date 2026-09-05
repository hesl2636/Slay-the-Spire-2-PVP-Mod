using System.Reflection;

using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

using HarmonyLib;

using PvpDuel.Config;
using PvpDuel.Core.Duel;
using PvpDuel.Core.Save;
using PvpDuel.Logging;

namespace PvpDuel.Save;

/// <summary>Outcome of a lenient side-channel read (ticket #21, spec §8).</summary>
public enum DuelSideChannelReadStatus
{
    Ok,

    /// <summary>No side-channel file for this profile/mode — nothing mod-side was ever saved.</summary>
    Absent,

    /// <summary>File exists but the payload is unreadable/invalid — dropped, official save unaffected.</summary>
    Corrupt,

    /// <summary>Payload was written by a different schema version — dropped (spec §4.3).</summary>
    VersionMismatch,

    /// <summary>Payload belongs to a different run — ignored, the current run's data is not touched.</summary>
    RunKeyMismatch,
}

/// <summary>
/// File IO and game-facing capture/restore for the mod save side channel
/// (ticket #21, spec §4.3/§5.7/§8). The mod writes its own schema-versioned
/// JSON blob (<see cref="DuelSavePayload"/>) beside the official run save,
/// keyed by run identity (<see cref="DuelRunKey"/>). Every failure on either
/// path degrades to Warn + dropped mod data — never a crash, never a touch of
/// the official <c>SerializableRun</c>.
/// </summary>
public static class DuelRunSaveStore
{
    // The branch table lives in the branch layer (BranchSync owns the single
    // instance); read it reflectively instead of widening that file's surface.
    private static FieldInfo? _branchTableField;

    // ---- File IO (path-explicit, unit-testable) ----

    /// <summary>
    /// Writes the side-channel JSON atomically (tmp file + replace). Any
    /// failure Warns and returns false — mod data is not persisted, nothing else
    /// is affected.
    /// </summary>
    public static bool TryWrite(string path, string json)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            PvpDuelLog.Warn($"save side channel write failed at {path}: {ex.Message}; mod-side data not persisted (official save unaffected).");
            return false;
        }
    }

    /// <summary>
    /// Lenient side-channel read: decodes, validates and run-key-checks the
    /// payload. Every failure returns a status with a null payload — mod data
    /// is dropped (Warn, spec §4.3/§8), nothing throws, the official save is
    /// never touched.
    /// </summary>
    public static (DuelSavePayload? Payload, DuelSideChannelReadStatus Status) TryReadForRunKey(string path, string runKey)
    {
        if (!File.Exists(path))
        {
            return (null, DuelSideChannelReadStatus.Absent);
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PvpDuelLog.Warn($"save side channel unreadable at {path}: {ex.Message}; dropping mod-side data (official save unaffected).");
            return (null, DuelSideChannelReadStatus.Corrupt);
        }

        var (payload, status) = DuelSaveCodec.TryRead(json);
        switch (status)
        {
            case DuelSaveReadStatus.Ok:
                if (!string.Equals(payload!.RunKey, runKey, StringComparison.Ordinal))
                {
                    return (null, DuelSideChannelReadStatus.RunKeyMismatch);
                }

                return (payload, DuelSideChannelReadStatus.Ok);
            case DuelSaveReadStatus.Corrupt:
                PvpDuelLog.Warn($"save side channel corrupt at {path}; dropping mod-side data (official save unaffected).");
                return (null, DuelSideChannelReadStatus.Corrupt);
            case DuelSaveReadStatus.VersionMismatch:
                PvpDuelLog.Warn($"save side channel schema mismatch at {path} (expected {SaveSchema.CurrentVersion}); dropping mod-side data (spec §4.3).");
                return (null, DuelSideChannelReadStatus.VersionMismatch);
            default:
                return (null, DuelSideChannelReadStatus.Corrupt);
        }
    }

    // ---- Game-facing capture / restore (Harmony postfix bodies; fail-safe) ----

    /// <summary>
    /// SaveRun postfix: snapshot the mod state beside the official run save.
    /// Host/singleplayer only (clients never own a save file, mirroring the
    /// vanilla SaveRun gate), and skipped entirely when there is no mod-side
    /// content so vanilla co-op / singleplayer runs stay file-clean.
    /// </summary>
    public static void WriteForCurrentRun()
    {
        try
        {
            var state = RunManager.Instance.DebugOnlyGetState();
            var netType = RunManager.Instance.NetService?.Type ?? NetGameType.None;
            if (state == null || netType is not (NetGameType.Singleplayer or NetGameType.Host))
            {
                return;
            }

            var runKey = DuelRunKey.Of(state);
            if (runKey.Length == 0)
            {
                return;
            }

            var payload = DuelSaveMapper.Capture(
                runKey, PvpConfigStore.LoadOrDefault().Hash, BranchTableRef(),
                DuelSaveState.History, DuelSaveState.AncientPicks);
            if (DuelSaveMapper.IsEmpty(payload))
            {
                return;
            }

            var path = DuelSavePaths.SideChannelPath(SaveManager.Instance.CurrentProfileId, multiplayer: netType == NetGameType.Host);
            if (TryWrite(path, DuelSaveCodec.Serialize(payload)))
            {
                PvpDuelLog.Info($"save side channel written for run '{runKey}' ({payload.Branches.Count} branches, {payload.DuelResults.Count} results, {payload.AncientPicks.Count} picks).");
            }
        }
        catch (Exception ex)
        {
            PvpDuelLog.Warn($"save side channel capture failed: {ex.Message} (official save unaffected).");
        }
    }

    /// <summary>
    /// LoadAndCanonicalizeMultiplayerRunSave postfix: early diagnostic read at
    /// menu/host time. Surfacing corrupt / mismatched side channels here makes
    /// the degrade visible in the log before the run even continues; the actual
    /// restore happens when the saved run materializes.
    /// </summary>
    public static void ProbeMultiplayerRunSave(ReadSaveResult<SerializableRun> result)
    {
        try
        {
            var save = result?.SaveData;
            if (save == null)
            {
                return;
            }

            var runKey = DuelRunKey.Of(save);
            if (runKey.Length == 0)
            {
                return;
            }

            var path = DuelSavePaths.SideChannelPath(SaveManager.Instance.CurrentProfileId, multiplayer: true);
            var (payload, status) = TryReadForRunKey(path, runKey);
            if (status == DuelSideChannelReadStatus.Ok)
            {
                PvpDuelLog.Info($"save side channel present for run '{runKey}' ({payload!.Branches.Count} branches, {payload.DuelResults.Count} results, {payload.AncientPicks.Count} picks).");
            }
        }
        catch (Exception ex)
        {
            PvpDuelLog.Warn($"save side channel probe failed: {ex.Message} (official load unaffected).");
        }
    }

    /// <summary>
    /// SetUpSaved{Singleplayer,Multiplayer} postfix: restore the mod state
    /// exactly when a saved run materializes. The runtime state is reset first,
    /// then the payload (if any) becomes the whole truth — a run without a
    /// side channel starts with clean mod state instead of a previous run's
    /// leftovers. Clients read their (nonexistent) local side channel and
    /// silently stay empty; their state arrives over the network.
    /// </summary>
    public static void RestoreForLoadedRun(RunState state, bool multiplayer)
    {
        try
        {
            DuelSaveState.Clear();
            var runKey = DuelRunKey.Of(state);
            if (runKey.Length == 0)
            {
                return;
            }

            var path = DuelSavePaths.SideChannelPath(SaveManager.Instance.CurrentProfileId, multiplayer);
            var (payload, status) = TryReadForRunKey(path, runKey);
            if (status != DuelSideChannelReadStatus.Ok)
            {
                if (status == DuelSideChannelReadStatus.RunKeyMismatch)
                {
                    PvpDuelLog.Info($"save side channel at {path} belongs to a different run; starting clean.");
                }

                return; // Absent stays silent; Corrupt/VersionMismatch already Warned.
            }

            DuelSaveMapper.Restore(payload!, BranchTableRef(), DuelSaveState.History,
                DuelSaveState.MutableAncientPicks, GameLogSink.Instance);
            DuelSaveState.MarkRestored(payload!.ConfigHash);
            PvpDuelLog.Info($"save side channel restored for run '{runKey}' ({payload.Branches.Count} branches, {payload.DuelResults.Count} results, {payload.AncientPicks.Count} picks).");
        }
        catch (Exception ex)
        {
            PvpDuelLog.Warn($"save side channel restore failed: {ex.Message} (official load unaffected).");
        }
    }


    private static BranchTable? BranchTableRef()
    {
        _branchTableField ??= AccessTools.Field(typeof(Branching.BranchSync), "Table");
        return _branchTableField?.GetValue(null) as BranchTable;
    }
}
