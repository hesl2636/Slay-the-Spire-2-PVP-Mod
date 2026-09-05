using PvpDuel.Core.Fatigue;

namespace PvpDuel.Duel;

/// <summary>
/// Scope gate: the single authority deciding whether the current combat is a
/// duel. Every combat/settlement patch checks this first and passes through to
/// the original method for anything else. STUB (ticket #13): always false, so
/// no gameplay behavior is changed yet — later tickets implement the real
/// <c>room.Encounter is PvpDuelEncounter</c> check.
/// </summary>
public static class DuelScope
{
    /// <summary>True only inside a duel boss room. STUB: constant false.</summary>
    public static bool IsDuelRoom => false;

    /// <summary>Context of the running duel; null outside duels. STUB: constant null.</summary>
    public static DuelContext? Current => null;
}

/// <summary>
/// Combat-scoped duel state shared by the combat patches: opponent, first-hand
/// side, act index, per-turn play counts and the fatigue rule. Created when a
/// duel room opens, cleared by <c>EndCombatInternal</c>.
/// STUB (ticket #13): signatures only — bodies throw until the combat ticket.
/// </summary>
public sealed class DuelContext
{
    public int ActIndex { get; init; }

    /// <summary>NetId of the player with first-hand (shorter act timer).</summary>
    public ulong FirstHandNetId { get; init; }

    public FatigueRule Fatigue { get; init; } = new(FatigueRule.DefaultCap);

    /// <summary>Cards already played this turn, keyed by player net id.</summary>
    public int PlaysThisTurn(ulong playerNetId) =>
        throw new NotImplementedException("Filled in by the combat ticket.");

    public void CountPlay(ulong playerNetId) =>
        throw new NotImplementedException("Filled in by the combat ticket.");

    public void ResetTurnCounters() =>
        throw new NotImplementedException("Filled in by the combat ticket.");
}
