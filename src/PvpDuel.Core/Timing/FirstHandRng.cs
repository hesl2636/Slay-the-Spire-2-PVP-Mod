using System.Numerics;

namespace PvpDuel.Core.Timing;

/// <summary>
/// Bit-exact mirror of the game's MegaRandom — Xoshiro256** with SplitMix64
/// seeding (decomp MegaRandom.cs:87-93/99-105/168-187) — so the first-hand
/// tiebreak consumes exactly the stream a vanilla <c>Rng(derivedSeed)</c>
/// produces. The mod layer derives the seed exactly like the named vanilla
/// constructor does, <c>seed + StringHelper.GetDeterministicHashCode(name)</c>
/// (decomp Rng.cs:55-58), keeping the fallback equivalent to
/// <c>new Rng(runSeed, "pvp_firsthand_actN")</c> (spec §4.2: gameplay randomness
/// always derives from the run seed; the result never travels the wire).
/// Pinned to the live sts2.dll stream by FirstHandRngSurfaceTests.
/// </summary>
public sealed class FirstHandRng
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    public FirstHandRng(ulong derivedSeed)
    {
        _s0 = SplitMix64(ref derivedSeed);
        _s1 = SplitMix64(ref derivedSeed);
        _s2 = SplitMix64(ref derivedSeed);
        _s3 = SplitMix64(ref derivedSeed);
    }

    /// <summary>Next 64 random bits (vanilla MegaRandom.NextULong).</summary>
    public ulong NextULong()
    {
        var s0 = _s0;
        var s1 = _s1;
        var s2 = _s2;
        var s3 = _s3;

        var result = BitOperations.RotateLeft(s1 * 5, 7) * 9;
        var shifted = s1 << 17;
        s2 ^= s0;
        s3 ^= s1;
        s1 ^= s2;
        s0 ^= s3;
        s2 ^= shifted;
        s3 = BitOperations.RotateLeft(s3, 45);

        _s0 = s0;
        _s1 = s1;
        _s2 = s2;
        _s3 = s3;
        return result;
    }

    /// <summary>Vanilla MegaRandom.Splitmix64 (constants from decomp line 89-92).</summary>
    private static ulong SplitMix64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
