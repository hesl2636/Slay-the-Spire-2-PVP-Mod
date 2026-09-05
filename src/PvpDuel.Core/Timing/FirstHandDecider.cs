namespace PvpDuel.Core.Timing;

/// <summary>The settled act first hand and how it was decided (diagnostics).</summary>
public readonly record struct FirstHandDecision(ulong FirstHandNetId, bool UsedRngFallback);

/// <summary>
/// First-hand determination (ticket #22, spec §2.1(5)/§7): the player with the
/// shorter act time wins the act's first hand; a difference below one second
/// (including exactly equal times — anti same-second race) falls back to the
/// run-seed-derived RNG. Pure and deterministic: both ends feed identical
/// inputs (host-clock elapsed values + the shared run seed) and derive the
/// identical winner locally — no RNG result is ever transmitted (spec §4.2).
/// </summary>
public static class FirstHandDecider
{
    /// <summary>Elapsed difference strictly below this window falls back to the seeded RNG (spec: 差 &lt; 1s).</summary>
    public const long TieWindowMs = 1_000;

    public static FirstHandDecision Decide(ActTimingPair pair, ulong derivedTiebreakSeed)
    {
        // Normalize by net id so confirmation order can never influence the
        // result — the host measures locally, the client receives messages, and
        // both must land on the same winner from the same inputs.
        var (lowId, lowMs, highId, highMs) = pair.FirstNetId <= pair.SecondNetId
            ? (pair.FirstNetId, pair.FirstElapsedMs, pair.SecondNetId, pair.SecondElapsedMs)
            : (pair.SecondNetId, pair.SecondElapsedMs, pair.FirstNetId, pair.FirstElapsedMs);

        var difference = lowMs > highMs ? lowMs - highMs : highMs - lowMs;
        if (difference >= TieWindowMs)
        {
            return new FirstHandDecision(lowMs < highMs ? lowId : highId, UsedRngFallback: false);
        }

        // Tie window: one deterministic bit mapped onto the duelists in net-id
        // order, so the mapping is stable regardless of who reported first.
        var rng = new FirstHandRng(derivedTiebreakSeed);
        return new FirstHandDecision((rng.NextULong() & 1UL) == 0UL ? lowId : highId, UsedRngFallback: true);
    }
}
