namespace PvpDuel.Core.SelfCheck;

/// <summary>Self-check outcome for one patch set.</summary>
public sealed record PatchSetStatus(string PatchSetId, bool Enabled, IReadOnlyList<string> MissingTargets);

/// <summary>Full result of one startup self-check run.</summary>
public sealed record SelfCheckResult(IReadOnlyList<PatchSetStatus> PatchSets)
{
    public bool AllTargetsExist => PatchSets.All(s => s.Enabled);

    public IEnumerable<string> DisabledPatchSets =>
        PatchSets.Where(s => !s.Enabled).Select(s => s.PatchSetId);

    public bool IsPatchSetEnabled(string patchSetId) =>
        PatchSets.FirstOrDefault(s => s.PatchSetId == patchSetId) is { Enabled: true };

    /// <summary>Human-readable warning lines for the banner + log.</summary>
    public IReadOnlyList<string> Warnings()
    {
        var lines = new List<string>();
        foreach (var set in PatchSets.Where(s => !s.Enabled))
        {
            lines.Add($"[pvpduel] patch set '{set.PatchSetId}' DISABLED — missing target(s): {string.Join("; ", set.MissingTargets)}");
        }

        return lines;
    }
}
