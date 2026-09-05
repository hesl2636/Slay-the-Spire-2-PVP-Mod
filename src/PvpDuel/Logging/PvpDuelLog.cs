using MegaCrit.Sts2.Core.Logging;

namespace PvpDuel.Logging;

/// <summary>
/// Unified mod log channel: every line carries the "[pvpduel]" prefix and can be
/// muted at runtime (Enabled = false). Backed by the game's Log so entries land
/// in the Godot log (%APPDATA%/Godot/app_userdata/SlayTheSpire2/logs or
/// %APPDATA%/SlayTheSpire2/logs depending on install).
/// </summary>
public static class PvpDuelLog
{
    public const string Prefix = "[pvpduel]";

    /// <summary>Master switch; flipped off, the mod logs nothing.</summary>
    public static bool Enabled { get; set; } = true;
    public static void Info(string message)
    {
        if (Enabled)
        {
            Log.Info(Prefixed(message));
        }
    }

    public static void Warn(string message)
    {
        if (Enabled)
        {
            Log.Warn(Prefixed(message));
        }
    }

    public static void Error(string message)
    {
        if (Enabled)
        {
            Log.Error(Prefixed(message));
        }
    }

    private static string Prefixed(string message) => $"{Prefix} {message}";
}

/// <summary>Adapts the game log to the Core logging seam.</summary>
public sealed class GameLogSink : PvpDuel.Core.Diagnostics.IModLog
{
    public static readonly GameLogSink Instance = new();

    private GameLogSink()
    {
    }

    public void Info(string message) => PvpDuelLog.Info(message);

    public void Warn(string message) => PvpDuelLog.Warn(message);

    public void Error(string message) => PvpDuelLog.Error(message);
}
