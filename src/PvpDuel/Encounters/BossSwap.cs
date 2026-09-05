using MegaCrit.Sts2.Core.Models;

using PvpDuel.Logging;

namespace PvpDuel.Encounters;

/// <summary>
/// Pure boss-room replacement logic, split out of the Harmony postfix so it is
/// unit-testable against real game types. The postfix only forwards the backing
/// field and a gate flag; everything else lives here.
/// </summary>
public static class BossSwap
{
    /// <summary>Entry of the vanilla boss replaced by the last successful swap (diagnostics/tests).</summary>
    public static string? LastReplacedVanillaEntry { get; private set; }

    /// <summary>
    /// Replaces an act's rolled boss with the duel encounter. Called from the
    /// <c>RoomSet.Boss</c> setter postfix on every act generation; a no-op in
    /// single player / non-2-player sessions (duel gate closed).
    /// </summary>
    /// <param name="boss">The rolled boss (backing field, modified in place).</param>
    /// <param name="duelRunActive">True only in a 2-player co-op session with the mod active.</param>
    public static void ReplaceIfDuelRun(ref EncounterModel boss, bool duelRunActive)
    {
        if (!duelRunActive || boss is PvpDuelEncounter)
        {
            return;
        }

        var duel = ModelDb.GetByIdOrNull<EncounterModel>(ModelDb.GetId<PvpDuelEncounter>());
        if (duel == null)
        {
            PvpDuelLog.Warn("boss swap skipped: PvpDuelEncounter not registered in ModelDb; boss left vanilla.");
            return;
        }

        var originalEntry = boss.Id.Entry;
        boss = duel;
        LastReplacedVanillaEntry = originalEntry;
        PvpDuelLog.Info($"boss room swapped: {originalEntry} -> pvp_duel_encounter.");
    }
}
