using PvpDuel.Logging;
using PvpDuel.Core.SelfCheck;

namespace PvpDuel.SelfCheck;

/// <summary>
/// Startup self-check state: the latest result and the warnings pending for the
/// main-menu banner (the banner can only render once the menu node exists, later
/// than mod init).
/// </summary>
public static class SelfCheckState
{
    private static readonly object Gate = new();
    private static IReadOnlyList<string>? _pendingWarnings;

    public static SelfCheckResult? Latest { get; private set; }

    public static void Report(SelfCheckResult result)
    {
        Latest = result ?? throw new ArgumentNullException(nameof(result));
        var warnings = result.Warnings();
        lock (Gate)
        {
            _pendingWarnings = warnings.Count == 0 ? null : warnings;
        }

        foreach (var warning in warnings)
        {
            PvpDuelLog.Error(warning);
        }
    }

    /// <summary>
    /// Takes the pending warnings for banner display (null when nothing to show).
    /// Called by the main-menu banner hook; consumes the pending list.
    /// </summary>
    public static IReadOnlyList<string>? TakePendingWarnings()
    {
        lock (Gate)
        {
            var warnings = _pendingWarnings;
            _pendingWarnings = null;
            return warnings;
        }
    }

    public static bool IsPatchSetEnabled(string patchSetId) =>
        Latest?.IsPatchSetEnabled(patchSetId) ?? true;
}
