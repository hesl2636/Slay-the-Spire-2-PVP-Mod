namespace PvpDuel.Core.Disconnect;

/// <summary>Phase of the duel reconnect wait (ticket #25, spec §2.1(7)/§8).</summary>
public enum DisconnectWaitPhase
{
    /// <summary>No disconnect wait is active.</summary>
    Idle,

    /// <summary>The peer dropped inside a duel; the countdown is running.</summary>
    Waiting,

    /// <summary>The peer rejoined before the deadline; the duel resumes.</summary>
    Resumed,

    /// <summary>The countdown ran out — the disconnected peer forfeits.</summary>
    Expired,
}

/// <summary>
/// Pure reconnect-wait state machine behind the duel disconnect pause (ticket
/// #25, spec §2.1(7)/§8: 掉线 → 暂停等待重连，host 计时，超时判负). The game layer
/// feeds the three events — peer dropped (<see cref="Begin"/>), peer rejoined
/// (<see cref="NoteRejoined"/>), periodic clock poll (<see cref="Poll"/>) — and
/// consumes the phase: Waiting drives the localized countdown overlay, Resumed
/// restores the duel, Expired authorizes exactly one
/// <c>DuelResult(Reason=DisconnectTimeout)</c> forfeit through the settlement
/// pipeline. The clock is injected so both acceptance branches are unit-tested
/// without a game process; host authority means the host's clock is the only
/// one feeding <see cref="Poll"/> (the observing client is torn down by the
/// official path and never hosts a wait).
/// </summary>
public sealed class DisconnectWaitMachine
{
    private readonly TimeSpan _timeout;
    private readonly Func<DateTimeOffset> _clock;

    private DisconnectWaitPhase _phase = DisconnectWaitPhase.Idle;
    private DateTimeOffset _startedAt;
    private ulong _disconnectedNetId;

    /// <param name="timeout">Configured wait before the forfeit resolves (PvpConfig.DisconnectTimeoutSec).</param>
    /// <param name="clock">Injected time source; defaults to the wall clock.</param>
    public DisconnectWaitMachine(TimeSpan timeout, Func<DateTimeOffset>? clock = null)
    {
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _timeout = timeout;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public DisconnectWaitPhase Phase => _phase;

    /// <summary>NetId of the player the wait is for; 0 outside Waiting.</summary>
    public ulong DisconnectedNetId => _phase == DisconnectWaitPhase.Waiting ? _disconnectedNetId : 0;

    /// <summary>Whole seconds left on the countdown (ceiled); 0 outside Waiting.</summary>
    public int SecondsRemaining
    {
        get
        {
            if (_phase != DisconnectWaitPhase.Waiting)
            {
                return 0;
            }

            var remaining = _timeout - (Now() - _startedAt);
            return remaining <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(remaining.TotalSeconds);
        }
    }

    /// <summary>
    /// The peer dropped: opens (or re-opens) the wait. A repeat Begin for the
    /// same player keeps the original deadline (the drop event can echo); a
    /// different player replaces the wait with a fresh deadline.
    /// </summary>
    public void Begin(ulong disconnectedNetId)
    {
        if (_phase == DisconnectWaitPhase.Waiting && disconnectedNetId == _disconnectedNetId)
        {
            return;
        }

        _phase = DisconnectWaitPhase.Waiting;
        _startedAt = Now();
        _disconnectedNetId = disconnectedNetId;
    }

    /// <summary>
    /// The peer rejoined: only a player actually being waited for resumes the
    /// duel — an Expired wait is never undone (the forfeit already resolved).
    /// </summary>
    public void NoteRejoined(ulong netId)
    {
        if (_phase == DisconnectWaitPhase.Waiting && netId == _disconnectedNetId)
        {
            _phase = DisconnectWaitPhase.Resumed;
        }
    }

    /// <summary>
    /// Advances the machine against the injected clock: Waiting past the
    /// deadline becomes Expired (sticky until the next <see cref="Begin"/>).
    /// Every other phase is unaffected.
    /// </summary>
    public void Poll()
    {
        if (_phase == DisconnectWaitPhase.Waiting && Now() - _startedAt >= _timeout)
        {
            _phase = DisconnectWaitPhase.Expired;
        }
    }

    /// <summary>Back to <see cref="DisconnectWaitPhase.Idle"/> (duel room left / session ended).</summary>
    public void Reset()
    {
        _phase = DisconnectWaitPhase.Idle;
        _disconnectedNetId = 0;
    }

    private DateTimeOffset Now() => _clock();
}
