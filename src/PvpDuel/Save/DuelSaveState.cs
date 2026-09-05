using PvpDuel.Core.Duel;
using PvpDuel.Core.Save;

namespace PvpDuel.Save;

/// <summary>
/// Run-period holder of the mod state the save side channel persists (ticket
/// #21, spec §4.3/§6): the settled <see cref="DuelHistory"/>, the per-act
/// Ancient body picks and the config hash seen at restore time. The combat /
/// settlement tickets record into <see cref="History"/> and
/// <see cref="RecordAncientPick"/>; the save patches capture and restore this
/// state around the official run save. Branch positions live in the branch
/// layer's table and are mirrored here through the payload only.
/// </summary>
public static class DuelSaveState
{
    private static readonly List<AncientPickDto> Picks = [];

    /// <summary>Settled duel results, in act order (last write wins per act).</summary>
    public static DuelHistory History { get; } = new();

    /// <summary>Recorded Ancient body picks (act + player + model id).</summary>
    public static IReadOnlyList<AncientPickDto> AncientPicks => Picks;

    /// <summary>Config hash restored from the side channel; null before the first restore (diagnostic, spec §4.4).</summary>
    public static string? RestoredConfigHash { get; private set; }

    /// <summary>Restore seam: the live list — <see cref="DuelSaveMapper.Restore"/> clears and refills it.</summary>
    public static List<AncientPickDto> MutableAncientPicks => Picks;

    /// <summary>
    /// Records one Ancient body pick. Re-picking for the same (act, player)
    /// replaces the previous entry, mirroring <see cref="DuelHistory.Record"/>.
    /// </summary>
    public static void RecordAncientPick(AncientPickDto pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        Picks.RemoveAll(p => p.ActIndex == pick.ActIndex && p.PlayerNetId == pick.PlayerNetId);
        Picks.Add(pick);
    }

    /// <summary>Replaces the whole picks list (restore semantics: payload is the truth).</summary>
    public static void SetAncientPicks(IEnumerable<AncientPickDto> picks)
    {
        Picks.Clear();
        Picks.AddRange(picks);
    }

    /// <summary>Diagnostic marker set by the save-side restore.</summary>
    public static void MarkRestored(string? configHash) => RestoredConfigHash = configHash;

    /// <summary>Clears all run-scoped mod state (new run / nothing restoreable).</summary>
    public static void Clear()
    {
        History.Clear();
        Picks.Clear();
        RestoredConfigHash = null;
    }
}
