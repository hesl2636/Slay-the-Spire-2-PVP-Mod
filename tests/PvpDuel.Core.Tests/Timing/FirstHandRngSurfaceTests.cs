using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Random;

using PvpDuel.Core.Timing;

using Xunit;

namespace PvpDuel.Core.Tests.Timing;

/// <summary>
/// Ticket #22: the tiebreak RNG must be an exact equivalent of the vanilla
/// derivation <c>new Rng(runSeed, "pvp_firsthand_actN")</c> (spec §4.2: all
/// gameplay randomness derives from the run seed). <see cref="Rng"/> wraps
/// MegaRandom (Xoshiro256**, SplitMix64 seeding, decomp MegaRandom.cs:99-187)
/// and the named ctor derives <c>seed + StringHelper hash</c> (decomp
/// Rng.cs:55-58) — this suite pins the Core mirror to the live sts2.dll
/// stream, so a game-side algorithm change fails here instead of diverging
/// the two ends mid-duel.
/// </summary>
public class FirstHandRngSurfaceTests
{
    public static TheoryData<ulong> DerivedSeeds => new()
    {
        0ul,
        1ul,
        0x9E3779B97F4A7C15ul,
        ulong.MaxValue,
        0xDEADBEEFCAFEBABEul,
    };

    [Theory]
    [MemberData(nameof(DerivedSeeds))]
    public void FirstHandRng_MatchesVanillaRngStream(ulong derivedSeed)
    {
        var mirror = new FirstHandRng(derivedSeed);
        var vanilla = new Rng(derivedSeed);

        for (var i = 0; i < 64; i++)
        {
            Assert.Equal(vanilla.NextUnsignedLong(), mirror.NextULong());
        }
    }

    [Theory]
    [InlineData("pvp_firsthand_act1")]
    [InlineData("pvp_firsthand_act2")]
    [InlineData("pvp_firsthand_act3")]
    public void FirstHandRng_MatchesVanillaNamedRngDerivation(string purposeName)
    {
        const ulong runSeed = 0x123456789ABCDEF;

        // Rng(ulong, string) derives seed + StringHelper hash (decomp Rng.cs:55-58).
        var vanilla = new Rng(runSeed, purposeName);
        var derived = runSeed + StringHelper.GetDeterministicHashCode(purposeName);
        var mirror = new FirstHandRng(derived);

        // NOTE: vanilla Rng.NextBool() is NOT a raw-stream MSB read (it draws
        // Next(2), decomp Rng.cs:73-77), so it cannot be cross-checked against a
        // single mirrored word — the raw-stream equivalence below is the pin
        // that matters (the tiebreak consumes one raw word per decision).
        Assert.Equal(vanilla.NextUnsignedLong(), mirror.NextULong());
        Assert.Equal(vanilla.NextUnsignedLong(), mirror.NextULong());
    }

    [Fact]
    public void SameDerivedSeed_ProducesIdenticalSequences()
    {
        var first = new FirstHandRng(0xC0FFEE);
        var second = new FirstHandRng(0xC0FFEE);
        for (var i = 0; i < 64; i++)
        {
            Assert.Equal(first.NextULong(), second.NextULong());
        }
    }

    [Fact]
    public void DifferentSeeds_StayIndependent()
    {
        var a = new FirstHandRng(1);
        var b = new FirstHandRng(2);
        var drifts = 0;
        for (var i = 0; i < 16; i++)
        {
            if (a.NextULong() != b.NextULong())
            {
                drifts++;
            }
        }

        Assert.True(drifts > 0, "distinct seeds produced identical streams");
    }
}
