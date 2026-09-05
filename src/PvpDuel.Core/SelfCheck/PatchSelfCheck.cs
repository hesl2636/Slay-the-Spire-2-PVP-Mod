using PvpDuel.Core.Diagnostics;

namespace PvpDuel.Core.SelfCheck;

/// <summary>
/// Startup self-check: verifies every declared patch target still exists in the
/// current game build before any Harmony patch is applied. A missing target
/// disables the whole owning patch set (feature degrades, never crashes) and
/// produces banner/log warnings for the main menu.
/// </summary>
public static class PatchSelfCheck
{
    /// <summary>
    /// Runs the manifest against the probe. Targets are grouped by patch set;
    /// a set is enabled only when all of its targets resolve.
    /// </summary>
    public static SelfCheckResult Run(PatchManifest manifest, IPatchTargetProbe probe, IModLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(probe);

        var statuses = new List<PatchSetStatus>();
        foreach (var group in manifest.Targets.GroupBy(t => t.PatchSetId, StringComparer.Ordinal))
        {
            var missing = new List<string>();
            foreach (var target in group)
            {
                if (!TargetExists(target, probe, log))
                {
                    missing.Add(target.ToString());
                }
            }

            statuses.Add(new PatchSetStatus(group.Key, Enabled: missing.Count == 0, missing));
        }

        var result = new SelfCheckResult(statuses);
        if (result.AllTargetsExist)
        {
            log?.Info($"[pvpduel] patch self-check passed ({manifest.Targets.Count} target(s) in {statuses.Count} set(s)).");
        }
        else
        {
            foreach (var warning in result.Warnings())
            {
                log?.Error(warning);
            }
        }

        return result;
    }

    private static bool TargetExists(PatchTargetSpec target, IPatchTargetProbe probe, IModLog? log)
    {
        var type = probe.FindType(target.TypeName);
        if (type == null)
        {
            log?.Warn($"[pvpduel] self-check: type not found: {target.TypeName}");
            return false;
        }

        if (target.MemberName == null)
        {
            return true;
        }

        if (probe.MemberExists(type, target.MemberName, target.ParameterTypes))
        {
            return true;
        }

        log?.Warn($"[pvpduel] self-check: member not found: {target.TypeName}::{target.MemberName}({string.Join(", ", target.ParameterTypes)})");
        return false;
    }
}
