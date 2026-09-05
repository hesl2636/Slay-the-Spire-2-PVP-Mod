using PvpDuel.Core.Ancients;
using PvpDuel.Core.Duel;
using PvpDuel.Core.Save;

using Xunit;

namespace PvpDuel.Core.Tests.Ancients;

/// <summary>
/// Ticket #24 (spec §7 结算层, §4.2): the pure execution-layer mutex for the
/// Ancient body pick. The winner of the settled act duel picks first; the
/// loser picks second and cannot repeat; anything outside a settled duel for
/// the current act is not a pick at all.
/// </summary>
public class AncientMutexPolicyTests
{
    private const int Act = 0;
    private const ulong Winner = 10;
    private const ulong Loser = 20;
    private const string Orobas = "OROBAS";
    private const string Pael = "PAEL";

    private static DuelResult Settled() => new(Act, Winner, Loser, DuelEndReason.Kill);

    private static AncientPickVerdict Evaluate(
        DuelResult? settled, IReadOnlyList<AncientPickDto> picks, ulong chooser, string body) =>
        AncientMutexPolicy.Evaluate(settled, picks, Act, chooser, body);

    [Fact]
    public void NoSettledDuel_IsNotADuelParticipant()
    {
        // 非对决状态: no settled result for the act — the flow never opened.
        Assert.Equal(
            AncientPickVerdict.NotADuelParticipant,
            Evaluate(null, [], chooser: Winner, body: Orobas));
    }

    [Fact]
    public void SettledForAnotherAct_IsNotADuelParticipant()
    {
        var otherAct = new DuelResult(1, Winner, Loser, DuelEndReason.Kill);
        Assert.Equal(
            AncientPickVerdict.NotADuelParticipant,
            Evaluate(otherAct, [], chooser: Winner, body: Orobas));
    }

    [Fact]
    public void NonParticipant_IsRejected()
    {
        Assert.Equal(
            AncientPickVerdict.NotADuelParticipant,
            Evaluate(Settled(), [], chooser: 99, body: Orobas));
    }

    [Fact]
    public void WinnerFirstPick_IsAllowed()
    {
        Assert.Equal(
            AncientPickVerdict.Allowed,
            Evaluate(Settled(), [], chooser: Winner, body: Orobas));
    }

    [Fact]
    public void LoserPickBeforeWinner_IsAllowed()
    {
        // The mutex-lock period is a UI courtesy (the waiting lock); the
        // execution layer admits the loser whenever no winner body exists yet —
        // the AncientPickMessage lands within milliseconds of the winner's click.
        Assert.Equal(
            AncientPickVerdict.Allowed,
            Evaluate(Settled(), [], chooser: Loser, body: Orobas));
    }

    [Fact]
    public void LoserCannotRepeatWinnerBody_IsTaken()
    {
        var picks = new[] { new AncientPickDto(Act, Winner, Orobas) };
        Assert.Equal(
            AncientPickVerdict.TakenByOpponent,
            Evaluate(Settled(), picks, chooser: Loser, body: Orobas));
    }

    [Fact]
    public void LoserDifferentBody_IsAllowed()
    {
        var picks = new[] { new AncientPickDto(Act, Winner, Orobas) };
        Assert.Equal(
            AncientPickVerdict.Allowed,
            Evaluate(Settled(), picks, chooser: Loser, body: Pael));
    }

    [Fact]
    public void WinnerCannotTakeLoserBody_IsTaken()
    {
        // Pathological order (loser clicked first): the first-pick right still
        // protects the loser's claim once recorded.
        var picks = new[] { new AncientPickDto(Act, Loser, Pael) };
        Assert.Equal(
            AncientPickVerdict.TakenByOpponent,
            Evaluate(Settled(), picks, chooser: Winner, body: Pael));
    }


    [Fact]
    public void SecondDifferentBody_IsAlreadyPicked()
    {
        var picks = new[]
        {
            new AncientPickDto(Act, Winner, Orobas),
        };
        Assert.Equal(
            AncientPickVerdict.AlreadyPicked,
            Evaluate(Settled(), picks, chooser: Winner, body: Pael));
    }

    [Fact]
    public void PicksFromOtherActs_DontLeak()
    {
        var picks = new[]
        {
            new AncientPickDto(1, Winner, Orobas),
            new AncientPickDto(1, Loser, Orobas),
        };
        Assert.Equal(
            AncientPickVerdict.Allowed,
            Evaluate(Settled(), picks, chooser: Loser, body: Orobas));
    }

    [Fact]
    public void NullBody_IsNeverAllowed()
    {
        Assert.Equal(
            AncientPickVerdict.NotADuelParticipant,
            Evaluate(Settled(), [], chooser: Winner, body: ""));
    }
}
