namespace PvpDuel.Core.Timing;

/// <summary>
/// Both duelists' act-arrival timings for one act, in confirmation order
/// (host clock). Argument order carries no meaning for decisions —
/// <see cref="FirstHandDecider"/> normalizes by net id.
/// </summary>
public readonly record struct ActTimingPair(
    int ActIndex,
    ulong FirstNetId,
    long FirstElapsedMs,
    ulong SecondNetId,
    long SecondElapsedMs);

/// <summary>
/// Pure per-act timing accumulation for the act timer (ticket #22, spec
/// §4.2/§7): t0 = the act map became operable, arrival = a player's boss-wait
/// confirmation. All times enter as caller-supplied timestamps — that is the
/// clock abstraction — so any monotonic source drives it and unit tests run on
/// a fake timeline. The host uses its own monotonic clock (dual-machine clocks
/// are untrusted, spec §4.2: host is the single source); the client reuses the
/// same accumulator on the host-reported elapsed axis with t0 = 0.
/// </summary>
public sealed class ActTimingTracker
{
    private readonly Dictionary<int, long> _actStartMs = [];
    private readonly Dictionary<int, List<(ulong NetId, long ElapsedMs)>> _arrivals = [];

    /// <summary>
    /// (Re)arms an act: records t0 and clears prior arrivals (a re-fired act
    /// start, e.g. a save/load re-entry, restarts the measurement).
    /// </summary>
    public void ActStarted(int actIndex, long nowMs)
    {
        _actStartMs[actIndex] = nowMs;
        _arrivals.Remove(actIndex);
    }

    /// <summary>
    /// Stamps a player's boss-wait confirmation. Returns false (no-op) when the
    /// act has no t0 yet, the net id is the unset sentinel, or the player
    /// already arrived (a re-broadcast must never skew the measurement).
    /// </summary>
    public bool RecordArrival(int actIndex, ulong playerNetId, long nowMs)
    {
        if (playerNetId == 0 || !_actStartMs.TryGetValue(actIndex, out var startMs))
        {
            return false;
        }

        if (!_arrivals.TryGetValue(actIndex, out var arrivals))
        {
            arrivals = [];
            _arrivals[actIndex] = arrivals;
        }

        if (arrivals.Count(a => a.NetId == playerNetId) > 0)
        {
            return false;
        }

        arrivals.Add((playerNetId, nowMs - startMs));
        return true;
    }

    public bool TryGetElapsedMs(int actIndex, ulong playerNetId, out long elapsedMs)
    {
        if (_arrivals.TryGetValue(actIndex, out var arrivals))
        {
            foreach (var arrival in arrivals)
            {
                if (arrival.NetId == playerNetId)
                {
                    elapsedMs = arrival.ElapsedMs;
                    return true;
                }
            }
        }

        elapsedMs = 0;
        return false;
    }

    /// <summary>Both duelists confirmed: the rendezvous pair in confirmation order.</summary>
    public bool TryGetRendezvousPair(int actIndex, out ActTimingPair pair)
    {
        if (_arrivals.TryGetValue(actIndex, out var arrivals) && arrivals.Count >= 2)
        {
            pair = new ActTimingPair(
                actIndex,
                arrivals[0].NetId, arrivals[0].ElapsedMs,
                arrivals[1].NetId, arrivals[1].ElapsedMs);
            return true;
        }

        pair = default;
        return false;
    }
}
