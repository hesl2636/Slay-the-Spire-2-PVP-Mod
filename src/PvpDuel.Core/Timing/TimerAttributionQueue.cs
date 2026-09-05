namespace PvpDuel.Core.Timing;

/// <summary>
/// Client-side attribution of host-broadcast act timers (ticket #22). The
/// DuelTimerMessage payload carries no player id (spec §4.2: serializable
/// primitives only), so the protocol pins attribution by order: the host sends
/// each player's timer immediately after that player's boss-wait confirmation
/// broadcast, and the reliable channel preserves per-connection FIFO — the same
/// ordering invariant vanilla co-op lockstep already trusts. The queue replays
/// the confirmation order observed on the mirrored branch table: confirmations
/// enqueue, arriving timers dequeue the head.
/// </summary>
public sealed class TimerAttributionQueue
{
    private readonly Dictionary<int, Queue<ulong>> _confirmationOrder = [];

    /// <summary>Records a boss-wait confirmation (false→true transition on the mirrored table).</summary>
    public void Confirmed(int actIndex, ulong playerNetId)
    {
        if (playerNetId == 0)
        {
            return;
        }

        if (!_confirmationOrder.TryGetValue(actIndex, out var queue))
        {
            queue = new Queue<ulong>();
            _confirmationOrder[actIndex] = queue;
        }

        if (!queue.Contains(playerNetId))
        {
            queue.Enqueue(playerNetId);
        }
    }

    /// <summary>Dequeues the confirmation-order head for one arriving timer; false when nothing is pending.</summary>
    public bool TryAssign(int actIndex, out ulong playerNetId)
    {
        if (_confirmationOrder.TryGetValue(actIndex, out var queue) && queue.Count > 0)
        {
            playerNetId = queue.Dequeue();
            return true;
        }

        playerNetId = 0;
        return false;
    }

    public void ResetAct(int actIndex) => _confirmationOrder.Remove(actIndex);
}
