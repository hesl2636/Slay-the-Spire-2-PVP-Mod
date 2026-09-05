using PvpDuel.Core.Timing;

using Xunit;

namespace PvpDuel.Core.Tests.Timing;

/// <summary>
/// Ticket #22: first-hand rule (spec §2.1(5)/§7) — shorter act time wins;
/// a difference below one second (including exactly equal times) falls back to
/// the run-seed-derived RNG. Both ends feed identical inputs and must derive
/// the identical winner, so the decision must be argument-order independent.
/// </summary>
public class FirstHandDeciderTests
{
    private const ulong HostPlayer = 11;
    private const ulong ClientPlayer = 22;

    private static ActTimingPair Pair(ulong firstNetId, long firstMs, ulong secondNetId, long secondMs) =>
        new(0, firstNetId, firstMs, secondNetId, secondMs);

    [Fact]
    public void ShorterElapsedWins_RegardlessOfArgumentOrder()
    {
        var decision = FirstHandDecider.Decide(Pair(HostPlayer, 10_000, ClientPlayer, 12_000), derivedTiebreakSeed: 1234);
        Assert.Equal(HostPlayer, decision.FirstHandNetId);
        Assert.False(decision.UsedRngFallback);

        var flipped = FirstHandDecider.Decide(Pair(ClientPlayer, 12_000, HostPlayer, 10_000), derivedTiebreakSeed: 1234);
        Assert.Equal(HostPlayer, flipped.FirstHandNetId);
        Assert.False(flipped.UsedRngFallback);
    }

    [Fact]
    public void DifferenceOfExactlyOneSecond_IsNotATie_ShorterWins()
    {
        var decision = FirstHandDecider.Decide(Pair(ClientPlayer, 60_000, HostPlayer, 61_000), derivedTiebreakSeed: 7);
        Assert.Equal(ClientPlayer, decision.FirstHandNetId);
        Assert.False(decision.UsedRngFallback);
    }

    [Fact]
    public void DifferenceBelowOneSecond_FallsBackToSeededRng()
    {
        var decision = FirstHandDecider.Decide(Pair(HostPlayer, 60_000, ClientPlayer, 60_999), derivedTiebreakSeed: 7);
        Assert.True(decision.UsedRngFallback);
        Assert.InRange(decision.FirstHandNetId, 0UL, ulong.MaxValue);
    }

    [Fact]
    public void ExactlyEqualTimes_FallBackToSeededRng()
    {
        var decision = FirstHandDecider.Decide(Pair(HostPlayer, 60_000, ClientPlayer, 60_000), derivedTiebreakSeed: 7);
        Assert.True(decision.UsedRngFallback);
    }

    [Fact]
    public void RngFallback_IsDeterministic_AndConsumesOneOrderedBit()
    {
        // The RNG picks between the two duelists ordered by net id; the same
        // seed must always yield the same winner (spec §5.4 determinism).
        const ulong seed = 0xABCDEF0123456789;
        var lowId = HostPlayer;  // 11
        var highId = ClientPlayer; // 22
        var expectedBit = new FirstHandRng(seed).NextULong() & 1UL;
        var expected = expectedBit == 0UL ? lowId : highId;

        for (var i = 0; i < 5; i++)
        {
            var decision = FirstHandDecider.Decide(Pair(HostPlayer, 100, ClientPlayer, 150), seed);
            Assert.Equal(expected, decision.FirstHandNetId);
        }
    }

    [Fact]
    public void RngFallback_MapsStablyToNetIdOrder_AcrossEnds()
    {
        // Same inputs with the pair fields swapped (as can happen between the
        // host's local order and the client's message order) → same winner.
        const ulong seed = 42;
        var a = FirstHandDecider.Decide(Pair(HostPlayer, 100, ClientPlayer, 100), seed);
        var b = FirstHandDecider.Decide(Pair(ClientPlayer, 100, HostPlayer, 100), seed);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Winner_IsAlwaysOneOfTheDuelists()
    {
        for (ulong seed = 0; seed < 32; seed++)
        {
            var decision = FirstHandDecider.Decide(Pair(HostPlayer, 100, ClientPlayer, 100), seed);
            Assert.True(
                decision.FirstHandNetId == HostPlayer || decision.FirstHandNetId == ClientPlayer,
                $"seed {seed} produced a non-duelist winner");
        }
    }

    [Fact]
    public void TieWindow_IsOneSecond()
    {
        Assert.Equal(1_000, FirstHandDecider.TieWindowMs);
    }
}
