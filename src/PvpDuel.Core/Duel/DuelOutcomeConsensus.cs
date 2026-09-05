namespace PvpDuel.Core.Duel;

/// <summary>Outcome of the both-end settlement (ticket #23, spec §4.2/§8).</summary>
public enum DuelOutcomeVerdict
{
    /// <summary>Waiting for the reports to be complete.</summary>
    Pending,

    /// <summary>Both ends reported the same <see cref="DuelResult"/> — the duel is settled.</summary>
    Settled,

    /// <summary>
    /// The reports disagree (or a report could not be decoded). Divergence
    /// semantics apply: the session must be aborted, never silently continued.
    /// </summary>
    Diverged,
}

/// <summary>
/// Both-end duel-outcome settlement state machine (ticket #23, spec §4.2
/// 「双方消息一致才落定」/ §8 divergence semantics). Each end feeds its local
/// result and the remote end's report; only full agreement produces a
/// settlement. Pure logic — no clock, no game types: the caller measures the
/// wait against <see cref="RemoteWaitTimeout"/> and asks
/// <see cref="WaitOverdue"/>. Verdicts are sticky: a settlement or divergence
/// can never be rewritten by later reports (echoes, rebroadcasts, skew).
/// </summary>
public sealed class DuelOutcomeConsensus
{
    /// <summary>
    /// Default wait for the remote report after the first one lands. Both ends
    /// run the same synced combat teardown, so a healthy session reports within
    /// seconds; anything past this window means a broken/opponent-dead session.
    /// </summary>
    public static readonly TimeSpan DefaultRemoteWaitTimeout = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _remoteWaitTimeout;

    private DuelResult? _local;
    private DuelResult? _remote;
    private DuelOutcomeVerdict _verdict = DuelOutcomeVerdict.Pending;
    private DuelResult? _settled;

    public DuelOutcomeConsensus(TimeSpan? remoteWaitTimeout = null)
    {
        _remoteWaitTimeout = remoteWaitTimeout ?? DefaultRemoteWaitTimeout;
    }

    public DuelOutcomeVerdict Verdict => _verdict;

    /// <summary>The agreed result — only set once <see cref="Verdict"/> is Settled.</summary>
    public DuelResult? Settled => _settled;

    public DuelResult? Local => _local;

    public DuelResult? Remote => _remote;

    /// <summary>True while the remote report is the missing half (watchdog window open).</summary>
    public bool WaitingForRemote => _verdict == DuelOutcomeVerdict.Pending && _local != null && _remote == null;

    public TimeSpan RemoteWaitTimeout => _remoteWaitTimeout;

    /// <summary>
    /// Reports the local end's resolved outcome (starts the wait window when no
    /// remote report is buffered yet). Idempotent for repeated local reports.
    /// </summary>
    public void ReportLocal(DuelResult local)
    {
        ArgumentNullException.ThrowIfNull(local);
        if (_verdict != DuelOutcomeVerdict.Pending || _local != null)
        {
            return;
        }

        _local = local;
        if (_remote != null)
        {
            Compare();
        }
    }

    /// <summary>
    /// Reports the remote end's message. May arrive before the local report
    /// (message ordering is not guaranteed) — it is buffered until then.
    /// Idempotent for repeated remote reports; the first sticks.
    /// </summary>
    public void ReportRemote(DuelResult remote)
    {
        ArgumentNullException.ThrowIfNull(remote);
        if (_verdict != DuelOutcomeVerdict.Pending || _remote != null)
        {
            return;
        }

        _remote = remote;
        if (_local != null)
        {
            Compare();
        }
    }

    /// <summary>
    /// Marks the remote report as received but undecodable (unknown reason
    /// byte — mod skew or corruption). Both ends can no longer agree on a
    /// well-formed result, so this is divergence, not silence.
    /// </summary>
    public void ReportUndecodableRemote()
    {
        if (_verdict == DuelOutcomeVerdict.Pending)
        {
            _verdict = DuelOutcomeVerdict.Diverged;
        }
    }

    /// <summary>
    /// True when the settlement is still pending after <paramref name="elapsed"/>
    /// — the wait is overdue and divergence semantics apply (spec §8: the
    /// session must be aborted, including the timeout-no-message path).
    /// </summary>
    public bool WaitOverdue(TimeSpan elapsed) =>
        _verdict == DuelOutcomeVerdict.Pending && elapsed >= _remoteWaitTimeout;

    private void Compare()
    {
        _verdict = _local!.Mirrors(_remote!)
            ? DuelOutcomeVerdict.Settled
            : DuelOutcomeVerdict.Diverged;
        if (_verdict == DuelOutcomeVerdict.Settled)
        {
            _settled = _local;
        }
    }
}
