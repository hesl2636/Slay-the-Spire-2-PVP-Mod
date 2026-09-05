namespace PvpDuel.Core.SelfCheck;

/// <summary>
/// One declarative patch-target declaration. Later tickets (T3-T12) add these to
/// the catalog; startup self-check verifies each one via reflection before any
/// Harmony patch is applied.
/// </summary>
/// <param name="PatchSetId">Feature slice that owns the patch (e.g. "DuelCombat").</param>
/// <param name="TypeName">Full name of the target type, assembly-agnostic prefix matching is allowed.</param>
/// <param name="MemberName">Method/property/field/constructor name, or null for type-only checks.</param>
/// <param name="ParameterTypeNames">Simple parameter type names for overload resolution; empty = ignore parameters.</param>
public sealed record PatchTargetSpec(
    string PatchSetId,
    string TypeName,
    string? MemberName = null,
    string[]? ParameterTypeNames = null)
{
    public string[] ParameterTypes { get; } = ParameterTypeNames ?? [];

    public override string ToString()
    {
        var member = MemberName is null ? string.Empty : $"::{MemberName}";
        var pars = ParameterTypes.Length == 0 ? string.Empty : $"({string.Join(", ", ParameterTypes)})";
        return $"{PatchSetId}: {TypeName}{member}{pars}";
    }
}
