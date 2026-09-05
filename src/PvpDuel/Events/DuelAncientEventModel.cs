using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;

namespace PvpDuel.Events;

/// <summary>
/// Duel Ancient event: after acts 1/2 duels, the winner picks an Ancient body
/// first from that act's Ancient pool and the loser picks second without
/// repeats; blessings still roll from the chosen Ancient's pool as usual.
/// STUB (ticket #13): type + signatures only — bodies throw until the
/// settlement ticket wires <c>Hook.AfterCombatEnd → EnterRoom</c>.
/// </summary>
public class DuelAncientEventModel : AncientEventModel
{
    /// <summary>Winner's first-pick options (same act's Ancient pool). STUB.</summary>
    public override IEnumerable<EventOption> AllPossibleOptions =>
        throw new NotImplementedException("Filled in by the settlement ticket.");

    /// <summary>
    /// Dialogue set required by the base class. STUB: never called in this ticket.
    /// </summary>
    protected override AncientDialogueSet DefineDialogues() =>
        throw new NotImplementedException("Filled in by the settlement ticket.");

    /// <summary>
    /// Initial options: keeps each Ancient's own random pool draw, ordered as
    /// body-pick phase before blessing phase. STUB.
    /// </summary>
    protected override IReadOnlyList<EventOption> GenerateInitialOptions() =>
        throw new NotImplementedException("Filled in by the settlement ticket.");
}
