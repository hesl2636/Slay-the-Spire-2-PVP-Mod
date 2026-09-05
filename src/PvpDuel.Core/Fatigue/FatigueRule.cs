namespace PvpDuel.Core.Fatigue;

/// <summary>
/// Per-turn play-count cap ("fatigue") that bounds infinite-combo decks.
/// Pure rule: given the number of cards this player has already played this turn,
/// decide whether another play is allowed. Configurable cap, deterministic, unit-tested.
/// </summary>
public sealed class FatigueRule
{
    /// <summary>Spec default: 12 plays per turn per player.</summary>
    public const int DefaultCap = 12;

    public FatigueRule(int cap = DefaultCap)
    {
        if (cap < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(cap), cap, "Play cap must be at least 1.");
        }

        Cap = cap;
    }

    /// <summary>Maximum number of card plays allowed in one turn.</summary>
    public int Cap { get; }

    /// <summary>
    /// Whether a play is allowed after <paramref name="playedThisTurn"/> plays
    /// have already been made this turn. Rejected plays must not enqueue any
    /// network action (the refusal happens before the action enters the queue).
    /// </summary>
    public bool AllowsPlay(int playedThisTurn)
    {
        if (playedThisTurn < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playedThisTurn), playedThisTurn, "Play count cannot be negative.");
        }

        return playedThisTurn < Cap;
    }
}
