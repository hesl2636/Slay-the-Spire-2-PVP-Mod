using PvpDuel.Core.Fatigue;
using Xunit;

namespace PvpDuel.Core.Tests;

public class FatigueRuleTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(11, true)]
    [InlineData(12, false)]
    [InlineData(13, false)]
    public void DefaultCap_Boundaries(int playedThisTurn, bool expected)
    {
        var rule = new FatigueRule();
        Assert.Equal(12, rule.Cap);
        Assert.Equal(expected, rule.AllowsPlay(playedThisTurn));
    }

    [Fact]
    public void CapTwelve_ExactBoundary_IsLastAllowedPlay()
    {
        var rule = new FatigueRule();
        // 11 played → 12th play still allowed; 12 played → 13th refused.
        Assert.True(rule.AllowsPlay(11));
        Assert.False(rule.AllowsPlay(12));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public void CustomCap_IsRespected(int cap)
    {
        var rule = new FatigueRule(cap);
        Assert.True(rule.AllowsPlay(cap - 1));
        Assert.False(rule.AllowsPlay(cap));
    }

    [Fact]
    public void FatigueDisabled_IsRepresentedByNoRuleInvocation_NotByRuleChange()
    {
        // The toggle lives in PvpConfig.FatigueEnabled; the rule itself stays
        // strict so enabling/disabling never half-applies.
        var rule = new FatigueRule();
        Assert.False(rule.AllowsPlay(rule.Cap));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidArguments_Throw(int bad)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FatigueRule(bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FatigueRule().AllowsPlay(-1));
    }
}
