using PvpDuel.Core.Timing;

using Xunit;

namespace PvpDuel.Core.Tests.Timing;

/// <summary>
/// Ticket #22: client-side attribution of host-broadcast act timers. The
/// DuelTimerMessage payload carries no player id (spec §4.2, primitives only),
/// so the protocol pins attribution by order: the host sends each player's
/// timer immediately after that player's boss-wait confirmation broadcast, and
/// the reliable channel preserves FIFO. The queue replays that order from the
/// mirrored branch-state confirmations.
/// </summary>
public class TimerAttributionQueueTests
{
    private const ulong HostPlayer = 11;
    private const ulong ClientPlayer = 22;

    [Fact]
    public void Timers_AreAssignedInConfirmationOrder()
    {
        var queue = new TimerAttributionQueue();
        queue.Confirmed(0, ClientPlayer); // client confirmed first on the mirror
        queue.Confirmed(0, HostPlayer);

        Assert.True(queue.TryAssign(0, out var first));
        Assert.Equal(ClientPlayer, first);
        Assert.True(queue.TryAssign(0, out var second));
        Assert.Equal(HostPlayer, second);
        Assert.False(queue.TryAssign(0, out _));
    }

    [Fact]
    public void RepeatConfirmation_DoesNotDoubleEnqueue()
    {
        var queue = new TimerAttributionQueue();
        queue.Confirmed(0, HostPlayer);
        queue.Confirmed(0, HostPlayer);

        Assert.True(queue.TryAssign(0, out _));
        Assert.False(queue.TryAssign(0, out _));
    }

    [Fact]
    public void UnsetNetId_IsIgnored()
    {
        var queue = new TimerAttributionQueue();
        queue.Confirmed(0, 0);
        Assert.False(queue.TryAssign(0, out _));
    }

    [Fact]
    public void Acts_AreIndependentlyScoped()
    {
        var queue = new TimerAttributionQueue();
        queue.Confirmed(0, HostPlayer);
        queue.Confirmed(1, ClientPlayer);

        Assert.True(queue.TryAssign(0, out var act0));
        Assert.Equal(HostPlayer, act0);
        Assert.True(queue.TryAssign(1, out var act1));
        Assert.Equal(ClientPlayer, act1);
    }

    [Fact]
    public void ResetAct_ClearsTheActQueue()
    {
        var queue = new TimerAttributionQueue();
        queue.Confirmed(0, HostPlayer);
        queue.ResetAct(0);

        Assert.False(queue.TryAssign(0, out _));
    }
}
