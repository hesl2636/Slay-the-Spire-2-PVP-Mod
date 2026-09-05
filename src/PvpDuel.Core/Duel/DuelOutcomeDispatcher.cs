namespace PvpDuel.Core.Duel;

/// <summary>
/// Post-settlement distribution (ticket #23, spec §6): routes a mirrored —
/// both-end agreed — duel result to the consumers that act on it. Acts 1/2
/// winners grant the Ancient first-pick right (<see cref="ActOutcomeSettled"/>,
/// consumed by the ancient-flow ticket); the act 3 result ends the whole run
/// (<see cref="FinalOutcomeSettled"/> — the terminal hook, intentionally without
/// in-repo subscribers until the endgame ticket lands). Pure Core: no game
/// types, dispatch is synchronous in raise order.
/// </summary>
public static class DuelOutcomeDispatcher
{
    /// <summary>Zero-based index of the final act (act 3 of 3 in v1).</summary>
    public const int FinalActIndex = 2;

    /// <summary>Raised when an act 1/2 duel settles (Ancient first-pick rights).</summary>
    public static event Action<DuelResult>? ActOutcomeSettled;

    /// <summary>Raised when the act 3 duel settles (whole-run end).</summary>
    public static event Action<DuelResult>? FinalOutcomeSettled;

    /// <summary>Routes one settled result to exactly one of the two events.</summary>
    public static void Dispatch(DuelResult settled)
    {
        ArgumentNullException.ThrowIfNull(settled);
        if (settled.ActIndex >= FinalActIndex)
        {
            FinalOutcomeSettled?.Invoke(settled);
        }
        else
        {
            ActOutcomeSettled?.Invoke(settled);
        }
    }

    /// <summary>Test seam: drops every subscriber (events are static).</summary>
    public static void Reset()
    {
        ActOutcomeSettled = null;
        FinalOutcomeSettled = null;
    }
}
