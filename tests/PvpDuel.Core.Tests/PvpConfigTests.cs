using System.Text.Json;
using PvpDuel.Core.Config;
using Xunit;

namespace PvpDuel.Core.Tests;

public class PvpConfigTests
{
    [Fact]
    public void Defaults_MatchSpec()
    {
        var config = PvpConfig.Default;
        Assert.Equal(12, config.PlayCap);
        Assert.Equal(120, config.DisconnectTimeoutSec);
        Assert.True(config.FatigueEnabled);
    }

    [Fact]
    public void Hash_IsDeterministic_AcrossInstances()
    {
        var a = ConfigHash.Compute(new PvpConfig());
        var b = ConfigHash.Compute(new PvpConfig { PlayCap = 12, DisconnectTimeoutSec = 120, FatigueEnabled = true });
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length); // SHA-256 hex
    }

    [Fact]
    public void Hash_Changes_WhenAnyFieldChanges()
    {
        var baseline = ConfigHash.Compute(PvpConfig.Default);
        Assert.NotEqual(baseline, ConfigHash.Compute(PvpConfig.Default with { PlayCap = 11 }));
        Assert.NotEqual(baseline, ConfigHash.Compute(PvpConfig.Default with { DisconnectTimeoutSec = 60 }));
        Assert.NotEqual(baseline, ConfigHash.Compute(PvpConfig.Default with { FatigueEnabled = false }));
    }

    [Fact]
    public void Config_RoundTrips_ThroughCamelCaseJson()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var config = PvpConfig.Default with { PlayCap = 15, DisconnectTimeoutSec = 90, FatigueEnabled = false };

        var json = JsonSerializer.Serialize(config, options);
        Assert.Contains("\"playCap\":15", json);
        Assert.Contains("\"disconnectTimeoutSec\":90", json);
        Assert.Contains("\"fatigueEnabled\":false", json);

        var parsed = JsonSerializer.Deserialize<PvpConfig>(json, options);
        Assert.Equal(config, parsed);
        Assert.Equal(ConfigHash.Compute(config), ConfigHash.Compute(parsed!));
    }
}
