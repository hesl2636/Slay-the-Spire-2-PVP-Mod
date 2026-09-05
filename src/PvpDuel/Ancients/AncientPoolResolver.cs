using System.Reflection;

using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

using PvpDuel.Logging;

namespace PvpDuel.Ancients;

/// <summary>
/// The act's Ancient pool, derived exactly like the vanilla ancient-room draw
/// (ActModel.cs:397): <c>GetUnlockedAncients(unlockState)</c> plus the act's
/// shared subset (RunManager:GenerateRooms 753-760 →
/// ActModel:SetSharedAncientSubset 311-316; the UnlockState epoch gating lives
/// inside both). Pool order is canonical data + lockstep-derived subset, so
/// both ends enumerate identical option indexes.
///
/// Native gap (acceptance #4): <c>_sharedAncientSubset</c> is private with no
/// getter and <c>ActModel:PullAncient</c> has no hook, so the subset is read
/// via reflection; if the field ever drifts the resolver degrades to the
/// unlocked act pool on both ends identically (mirrored act state), keeping
/// the option indexes aligned.
/// </summary>
internal static class AncientPoolResolver
{
    private static FieldInfo? _sharedSubsetField;

    public static IReadOnlyList<AncientEventModel> Resolve(IRunState state)
    {
        var pool = state.Act.GetUnlockedAncients(state.UnlockState).ToList();
        pool.AddRange(SharedSubset(state.Act));
        return pool;
    }

    private static IEnumerable<AncientEventModel> SharedSubset(ActModel act)
    {
        try
        {
            _sharedSubsetField ??= typeof(ActModel).GetField("_sharedAncientSubset", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingFieldException(typeof(ActModel).FullName, "_sharedAncientSubset");
            return _sharedSubsetField.GetValue(act) as List<AncientEventModel> ?? [];
        }
        catch (Exception ex)
        {
            PvpDuelLog.Warn($"ancient pool: shared subset unreadable ({ex.Message}) — using the unlocked act pool only.");
            return [];
        }
    }
}
