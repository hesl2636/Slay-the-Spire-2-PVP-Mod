using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

using PvpDuel.Save;

using Xunit;

namespace PvpDuel.Core.Tests.Save;

/// <summary>
/// Ticket #21 (T9): the side-channel run key is the run's string seed, read
/// from the serialized RNG set that the official save round-trips
/// (decomp RunRngSet.ToSerializable → SerializableRunRngSet.Seed).
/// </summary>
public class DuelRunKeyTests
{
    [Fact]
    public void Of_SerializableRun_ReadsTheStringSeed()
    {
        var save = new SerializableRun
        {
            SerializableRng = new SerializableRunRngSet { Seed = "SEED-XYZ" },
        };

        Assert.Equal("SEED-XYZ", DuelRunKey.Of(save));
    }

    [Fact]
    public void Of_SerializableRunWithoutRng_IsEmpty()
    {
        Assert.Equal(string.Empty, DuelRunKey.Of(new SerializableRun()));
    }
}
