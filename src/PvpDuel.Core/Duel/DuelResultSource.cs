namespace PvpDuel.Core.Duel;

/// <summary>
/// Per-duel-combat observation state for the local end: which duelists reached
/// lethal during this combat. One instance per duel room entry (the Harmony
/// layer arms/drops it via <see cref="DuelResultSource"/>).
/// </summary>
public sealed class DuelOutcomeTracker
{
    private bool _localDied;
    private bool _opponentDied;
    private DuelResult? _resolved;

    /// <summary>True once any death was observed in this combat (drives the LoseCombat backstop).</summary>
    public bool HasObservations => _localDied || _opponentDied;

    public bool LocalDied => _localDied;

    public bool OpponentDied => _opponentDied;

    /// <summary>The settled outcome of this combat, if any.</summary>
    public DuelResult? Resolved => _resolved;

    /// <summary>Raised once when this combat's outcome is resolved.</summary>
    public event Action<DuelResult>? SettledLocally;

    public void ObserveLocalDeath()
    {
        // A settled outcome sticks for the whole combat: later observations
        // cannot rewrite it (the combat ends right after settlement).
        if (_resolved == null)
        {
            _localDied = true;
        }
    }

    public void ObserveOpponentDeath()
    {
        if (_resolved == null)
        {
            _opponentDied = true;
        }
    }

    /// <summary>
    /// Resolves (once) and returns the locally settled outcome; null when
    /// nothing was observed. Idempotent: the first resolution sticks.
    /// </summary>
    public DuelResult? TryResolve(int actIndex, ulong localNetId, ulong opponentNetId, ulong firstHandNetId)
    {
        if (_resolved != null)
        {
            return _resolved;
        }

        var result = LossInterceptPolicy.ResolveOutcome(
            actIndex, localNetId, opponentNetId, _localDied, _opponentDied, firstHandNetId);
        if (result != null)
        {
            _resolved = result;
            SettledLocally?.Invoke(result);
        }

        return result;
    }
}

/// <summary>
/// Local duel-outcome event source (ticket #19, spec §6): death observations
/// land on the combat-scoped tracker; resolved outcomes are raised on
/// <see cref="OutcomeSettledLocally"/> and forwarded to <see cref="SettledSink"/>.
/// Pure Core state — no game types.
///
/// Since the settlement ticket (#23) the sink carries the result into the
/// both-end consensus (<c>Outcomes.DuelOutcomeSync</c>) instead of straight
/// into the run-scoped history: history records only after the peer's
/// <c>DuelOutcomeMessage</c> agrees (divergence semantics otherwise). Before
/// that ticket installs, a direct-to-history binding preserves the local-only
/// settlement.
/// </summary>
public static class DuelResultSource
{
    /// <summary>
    /// Run-scoped sink for settled results; since #23 the mod layer binds it to
    /// the both-end settlement pipeline (which records the history on
    /// agreement). Null keeps results event-only.
    /// </summary>
    public static Action<DuelResult>? SettledSink { get; set; }

    private static DuelOutcomeTracker? _current;

    /// <summary>Raised when this end settles a duel outcome locally (once per duel combat).</summary>
    public static event Action<DuelResult>? OutcomeSettledLocally;

    /// <summary>Tracker of the running duel combat; null outside duel rooms.</summary>
    public static DuelOutcomeTracker? Current => _current;

    /// <summary>
    /// Arms a fresh tracker for a duel combat (duel room entered). Any stale
    /// tracker is dropped first, so a re-entered act starts clean.
    /// </summary>
    public static void BeginDuelCombat()
    {
        EndDuelCombat();
        var tracker = new DuelOutcomeTracker();
        tracker.SettledLocally += OnTrackerSettled;
        _current = tracker;
    }

    /// <summary>Drops the current tracker (duel room exited).</summary>
    public static void EndDuelCombat()
    {
        if (_current != null)
        {
            _current.SettledLocally -= OnTrackerSettled;
            _current = null;
        }
    }

    private static void OnTrackerSettled(DuelResult result)
    {
        SettledSink?.Invoke(result);
        OutcomeSettledLocally?.Invoke(result);
    }
}
