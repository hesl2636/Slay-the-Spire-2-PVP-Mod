using PvpDuel.Core.Disconnect;

using Xunit;

namespace PvpDuel.Core.Tests.Disconnect;

/// <summary>
/// Ticket #25 (spec §2.1(7)/§8): the reconnect-wait state machine behind the
/// duel disconnect pause. Pure logic with an injected clock — the game layer
/// (DisconnectWatch) only feeds events and consumes phases. Both acceptance
/// branches are covered here: reconnect → resume and timeout → forfeit.
/// </summary>
public class DisconnectWaitMachineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly List<DateTimeOffset> _now = [T0];

    private DisconnectWaitMachine Machine(double timeoutSeconds) => new(
        TimeSpan.FromSeconds(timeoutSeconds),
        () => _now[^1]);

    private void Advance(double seconds) => _now.Add(_now[^1].AddSeconds(seconds));

    [Theory]
    [InlineData(120)]
    [InlineData(15)]
    public void Begin_FromIdle_EntersWaitingWithFullCountdown(double timeoutSeconds)
    {
        var machine = Machine(timeoutSeconds);
        machine.Begin(7);

        Assert.Equal(DisconnectWaitPhase.Waiting, machine.Phase);
        Assert.Equal(7ul, machine.DisconnectedNetId);
        Assert.Equal(timeoutSeconds, machine.SecondsRemaining);
    }

    [Fact]
    public void Reconnect_BeforeTimeout_ResumesAndNeverExpires()
    {
        var machine = Machine(120);
        machine.Begin(7);
        Advance(30);

        machine.NoteRejoined(7);

        Assert.Equal(DisconnectWaitPhase.Resumed, machine.Phase);
        Advance(500);
        machine.Poll();
        Assert.Equal(DisconnectWaitPhase.Resumed, machine.Phase);
    }

    [Fact]
    public void Reconnect_OfDifferentPlayer_IsIgnored()
    {
        var machine = Machine(120);
        machine.Begin(7);
        Advance(10);

        machine.NoteRejoined(8);

        Assert.Equal(DisconnectWaitPhase.Waiting, machine.Phase);
        Assert.Equal(7ul, machine.DisconnectedNetId);
    }

    [Fact]
    public void Reconnect_WhileNotWaiting_IsIgnored()
    {
        var machine = Machine(120);

        machine.NoteRejoined(7);

        Assert.Equal(DisconnectWaitPhase.Idle, machine.Phase);
    }

    [Fact]
    public void Timeout_ExpiresAndSticksExpired()
    {
        var machine = Machine(120);
        machine.Begin(7);
        Advance(120);

        machine.Poll();

        Assert.Equal(DisconnectWaitPhase.Expired, machine.Phase);
        Advance(500);
        machine.Poll();
        Assert.Equal(DisconnectWaitPhase.Expired, machine.Phase);
    }

    [Fact]
    public void Timeout_BoundaryIsInclusive()
    {
        var machine = Machine(120);
        machine.Begin(7);

        Advance(119);
        machine.Poll();
        Assert.Equal(DisconnectWaitPhase.Waiting, machine.Phase);

        Advance(1);
        machine.Poll();
        Assert.Equal(DisconnectWaitPhase.Expired, machine.Phase);
    }

    [Fact]
    public void Reconnect_AfterExpiry_CannotUndoForfeit()
    {
        var machine = Machine(120);
        machine.Begin(7);
        Advance(120);
        machine.Poll();
        Assert.Equal(DisconnectWaitPhase.Expired, machine.Phase);

        machine.NoteRejoined(7);

        Assert.Equal(DisconnectWaitPhase.Expired, machine.Phase);
    }

    [Fact]
    public void SecondDisconnect_ReplacesWaitWithFreshCountdown()
    {
        var machine = Machine(120);
        machine.Begin(7);
        Advance(100);
        machine.Poll();
        Assert.Equal(DisconnectWaitPhase.Waiting, machine.Phase);

        machine.Begin(8);

        Assert.Equal(DisconnectWaitPhase.Waiting, machine.Phase);
        Assert.Equal(8ul, machine.DisconnectedNetId);
        Assert.Equal(120, machine.SecondsRemaining);
        Advance(119);
        machine.Poll();
        Assert.Equal(DisconnectWaitPhase.Waiting, machine.Phase);
        Advance(1);
        machine.Poll();
        Assert.Equal(DisconnectWaitPhase.Expired, machine.Phase);
    }

    [Fact]
    public void RepeatBegin_SamePlayer_KeepsOriginalDeadline()
    {
        var machine = Machine(120);
        machine.Begin(7);
        Advance(30);

        machine.Begin(7);

        Assert.Equal(DisconnectWaitPhase.Waiting, machine.Phase);
        Assert.Equal(90, machine.SecondsRemaining);
    }

    [Fact]
    public void Reset_ReturnsToIdleAndClearsThePlayer()
    {
        var machine = Machine(120);
        machine.Begin(7);
        machine.Poll();

        machine.Reset();

        Assert.Equal(DisconnectWaitPhase.Idle, machine.Phase);
        Assert.Equal(0ul, machine.DisconnectedNetId);
        Assert.Equal(0, machine.SecondsRemaining);
    }

    [Theory]
    [InlineData(0, 120)]
    [InlineData(30.5, 90)]
    [InlineData(119.9, 1)]
    public void Countdown_CeilsTheRemainingSeconds(double elapsed, int expectedRemaining)
    {
        var machine = Machine(120);
        machine.Begin(7);
        Advance(elapsed);

        Assert.Equal(expectedRemaining, machine.SecondsRemaining);
    }

    [Fact]
    public void Poll_OutsideWaiting_DoesNotExpire()
    {
        var machine = Machine(120);
        machine.Poll();
        Assert.Equal(DisconnectWaitPhase.Idle, machine.Phase);

        machine.Begin(7);
        machine.NoteRejoined(7);
        Advance(1000);
        machine.Poll();
        Assert.Equal(DisconnectWaitPhase.Resumed, machine.Phase);
    }

    [Fact]
    public void Resumed_NewDisconnect_WaitsAgain()
    {
        var machine = Machine(120);
        machine.Begin(7);
        machine.NoteRejoined(7);
        Assert.Equal(DisconnectWaitPhase.Resumed, machine.Phase);

        machine.Begin(7);
        Advance(120);
        machine.Poll();
        Assert.Equal(DisconnectWaitPhase.Expired, machine.Phase);
    }
}
