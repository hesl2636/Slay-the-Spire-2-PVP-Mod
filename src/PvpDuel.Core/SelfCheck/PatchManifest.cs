namespace PvpDuel.Core.SelfCheck;

/// <summary>
/// Declarative list of everything the mod intends to patch. Ships empty for the
/// skeleton ticket; later tickets append targets and the startup self-check
/// verifies all of them exist in the running game version.
/// </summary>
public sealed class PatchManifest
{
    private readonly List<PatchTargetSpec> _targets = [];

    public IReadOnlyList<PatchTargetSpec> Targets => _targets;

    public IEnumerable<string> PatchSetIds => _targets.Select(t => t.PatchSetId).Distinct();

    public void Add(PatchTargetSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (string.IsNullOrWhiteSpace(spec.PatchSetId))
        {
            throw new ArgumentException("PatchSetId must be non-empty.", nameof(spec));
        }

        if (string.IsNullOrWhiteSpace(spec.TypeName))
        {
            throw new ArgumentException("TypeName must be non-empty.", nameof(spec));
        }

        _targets.Add(spec);
    }

    public void AddRange(IEnumerable<PatchTargetSpec> specs)
    {
        foreach (var spec in specs)
        {
            Add(spec);
        }
    }
}
