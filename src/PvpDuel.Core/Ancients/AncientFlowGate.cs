using PvpDuel.Core.Duel;

namespace PvpDuel.Core.Ancients;

/// <summary>
/// Ticket #24 (spec §7 结算层): session-scoped pending/open decisions between
/// the settled act outcome and the programmatically opened picker room.
///
/// Flow: a settled act 1/2 duel registers the pending act; the next room entry
/// that is NOT a combat room (the vanilla post-combat flow walked back to the
/// map) opens the <c>DuelAncientEventModel</c> room on this end, exactly once.
/// Act 3 settles straight into the terminal hook — no Ancient flow (spec
/// §2.1(4)/§7: "第 3 幕：胜利直接终局画面，无 Ancient 环节"). Pure decisions;
/// the mod-side flow class only executes them.
/// </summary>
public static class AncientFlowGate
{
    /// <summary>
    /// Registers a settled act as pending (returns the new pending act, or the
    /// unchanged current one for the final act — the dispatcher routes final
    /// acts to <see cref="DuelOutcomeDispatcher.FinalOutcomeSettled"/>; this is
    /// the belt-and-braces half of the same rule).
    /// </summary>
    public static int? RegisterSettledAct(int? currentPending, int settledActIndex) =>
        settledActIndex >= DuelOutcomeDispatcher.FinalActIndex ? currentPending : settledActIndex;

    /// <summary>Whether this room entry should open the picker room.</summary>
    public static bool ShouldOpenPicker(int? pendingAct, int enteredActIndex, bool enteredRoomIsCombat) =>
        pendingAct is { } act && !enteredRoomIsCombat && act == enteredActIndex;

    /// <summary>Whether a room entry retires a stale pending registration.</summary>
    public static bool ShouldDropPending(int? pendingAct, int enteredActIndex) =>
        pendingAct is { } act && act != enteredActIndex;
}
