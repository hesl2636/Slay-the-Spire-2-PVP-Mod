using PvpDuel.Core.Duel;
using PvpDuel.Core.Diagnostics;

namespace PvpDuel.Core.Save;

/// <summary>
/// Maps the runtime duel state (branch table, settled results, Ancient picks)
/// to and from the save side-channel payload. Pure Core logic: callers supply
/// the state holders and own the file IO, so the mapping is unit-testable
/// without the game (ticket #21, spec §4.3).
/// </summary>
public static class DuelSaveMapper
{
    /// <summary>
    /// Snapshots the current mod state into a payload. Branches are ordered by
    /// player net id so the serialized form is deterministic.
    /// </summary>
    public static DuelSavePayload Capture(
        string runKey,
        string? configHash,
        BranchTable? table,
        DuelHistory history,
        IReadOnlyList<AncientPickDto> ancientPicks)
    {
        List<SavedBranchEntry> branches = table is null
            ? []
            : [.. table.Snapshot.OrderBy(kv => kv.Key).Select(kv => new SavedBranchEntry(kv.Key, BranchStateDto.From(kv.Value)))];

        return new DuelSavePayload(
            SaveSchema.CurrentVersion,
            runKey,
            branches,
            [.. history.Results.Select(DuelResultDto.From)],
            [.. ancientPicks],
            configHash);
    }

    /// <summary>True when the payload carries no mod-side content (nothing worth writing).</summary>
    public static bool IsEmpty(DuelSavePayload payload) =>
        payload.Branches.Count == 0
        && payload.DuelResults.Count == 0
        && payload.AncientPicks.Count == 0;

    /// <summary>
    /// Applies a payload to the runtime state holders, replacing the previous
    /// contents wholesale (restore = payload is the truth). Entry-level
    /// corruption drops individual entries with a Warn (via
    /// <see cref="DuelSaveCodec.Materialize"/>); the payload itself must already
    /// have passed <see cref="DuelSaveCodec.TryRead"/>. Branches restore the
    /// table exactly as saved — act index and per-player positions — so the
    /// branch patches retarget both ends onto their own paths after a load.
    /// </summary>
    public static void Restore(
        DuelSavePayload payload,
        BranchTable? table,
        DuelHistory history,
        IList<AncientPickDto> ancientPicks,
        IModLog? log = null)
    {
        history.Clear();
        ancientPicks.Clear();

        var (branches, results) = DuelSaveCodec.Materialize(payload, log);
        foreach (var result in results)
        {
            history.Record(result);
        }

        foreach (var pick in payload.AncientPicks)
        {
            ancientPicks.Add(pick);
        }

        if (table is not null && branches.Count > 0)
        {
            table.Reset(branches[0].State.ActIndex);
            foreach (var (netId, state) in branches)
            {
                table.Set(netId, state);
            }
        }
    }
}
