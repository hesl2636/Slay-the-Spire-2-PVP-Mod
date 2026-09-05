using System.Reflection;
using System.Text.Json;

using PvpDuel.Logging;
using PvpDuel.Core.Config;

namespace PvpDuel.Config;

/// <summary>
/// Loads/saves the mod config JSON that lives beside the mod manifest
/// (pvpduel_config.json). Missing or corrupt file falls back to defaults
/// (Warn, never blocks startup). The derived hash is what both ends compare.
/// </summary>
public static class PvpConfigStore
{
    public const string FileName = "pvpduel_config.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static string ConfigDirectory
    {
        get
        {
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var dir = Path.GetDirectoryName(assemblyLocation);
            return string.IsNullOrEmpty(dir) ? AppContext.BaseDirectory : dir;
        }
    }

    public static string ConfigPath => Path.Combine(ConfigDirectory, FileName);

    /// <summary>Loads config, falling back to defaults; returns (config, hash).</summary>
    public static (PvpConfig Config, string Hash) LoadOrDefault()
    {
        PvpConfig config;
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                config = JsonSerializer.Deserialize<PvpConfig>(json, Options) ?? PvpConfig.Default;
                if (!IsSane(config))
                {
                    PvpDuelLog.Warn($"config at {ConfigPath} has out-of-range values; using defaults.");
                    config = PvpConfig.Default;
                }
            }
            else
            {
                config = PvpConfig.Default;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            PvpDuelLog.Warn($"failed reading config {ConfigPath}: {ex.Message}; using defaults.");
            config = PvpConfig.Default;
        }

        return (config, ConfigHash.Compute(config));
    }

    /// <summary>Persists a config (used by future settings UI); returns its hash.</summary>
    public static string Save(PvpConfig config)
    {
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, Options));
        return ConfigHash.Compute(config);
    }

    private static bool IsSane(PvpConfig config) =>
        config.PlayCap is >= 1 and <= 1000
        && config.DisconnectTimeoutSec is >= 0 and <= 3600;
}
