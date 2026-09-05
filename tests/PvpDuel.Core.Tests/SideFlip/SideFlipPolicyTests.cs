using MegaCrit.Sts2.Core.Combat;

using PvpDuel.Combat;

using Xunit;

namespace PvpDuel.Core.Tests;

/// <summary>
/// Ticket #17 (T5): pure decision logic for the side flip and the
/// Monster-deref guards. No game singletons — the decisions take the gate and
/// creature facts as parameters (game Logger static ctor must not run here).
/// </summary>
public class SideFlipPolicyTests
{
    private static (bool isDuelRoom, bool isPlayer, CombatSide side) Flipped => (true, true, CombatSide.Enemy);
    private static (bool isDuelRoom, bool isPlayer, CombatSide side) NormalPlayer => (true, true, CombatSide.Player);
    private static (bool isDuelRoom, bool isPlayer, CombatSide side) NormalMonster => (true, false, CombatSide.Enemy);

    [Fact]
    public void FlippedOpponent_IsPlayerCreatureOnEnemySideInsideDuelRoom()
    {
        Assert.True(SideFlipPolicy.IsFlippedOpponent(Flipped.isDuelRoom, Flipped.isPlayer, Flipped.side));
    }

    [Theory]
    [InlineData(false, true, CombatSide.Player)]  // not a duel room: vanilla player creature
    [InlineData(true, true, CombatSide.Player)]   // duel room, own creature (assigned Player)
    [InlineData(true, false, CombatSide.Enemy)]   // real monster
    [InlineData(true, false, CombatSide.Player)]  // impossible combo, must stay inert
    public void Guards_NeverTriggerOutsideTheFlippedOpponentShape(bool isDuelRoom, bool isPlayer, CombatSide side)
    {
        Assert.False(SideFlipPolicy.IsFlippedOpponent(isDuelRoom, isPlayer, side));
        Assert.False(SideFlipPolicy.ShouldSkipAfterAddedToRoom(isDuelRoom, isPlayer, side));
        Assert.False(SideFlipPolicy.ShouldSkipPrepareForNextTurn(isDuelRoom, isPlayer, side));
        Assert.False(SideFlipPolicy.ShouldSkipTakeTurn(isDuelRoom, isPlayer, side));
        Assert.False(SideFlipPolicy.ShouldSkipMonsterRollMove(isDuelRoom, isPlayer, side));
    }

    [Fact]
    public void AllGuards_TriggerExactlyForTheFlippedOpponent()
    {
        var (isDuelRoom, isPlayer, side) = Flipped;
        Assert.True(SideFlipPolicy.ShouldSkipAfterAddedToRoom(isDuelRoom, isPlayer, side));
        Assert.True(SideFlipPolicy.ShouldSkipPrepareForNextTurn(isDuelRoom, isPlayer, side));
        Assert.True(SideFlipPolicy.ShouldSkipTakeTurn(isDuelRoom, isPlayer, side));
        Assert.True(SideFlipPolicy.ShouldSkipMonsterRollMove(isDuelRoom, isPlayer, side));
    }

    [Theory]
    [InlineData(true, false)]   // duel room + not me → flip
    [InlineData(false, false)]  // outside duel room → vanilla Player side
    [InlineData(true, true)]    // my own creature → never flipped
    [InlineData(false, true)]   // outside duel room, me → never flipped
    public void CtorPostfix_FlipDecision_MatchesGateAndMe(bool isDuelRoom, bool isMe)
    {
        Assert.Equal(isDuelRoom && !isMe, SideFlipPolicy.ShouldFlipConstructedSide(isDuelRoom, isMe));
    }
}
