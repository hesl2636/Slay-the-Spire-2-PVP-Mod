namespace PvpDuel.Core.SelfCheck;

/// <summary>
/// Reflection seam so the self-check runner is testable without the game.
/// The game project implements this with Harmony's AccessTools/Traverse.
/// </summary>
public interface IPatchTargetProbe
{
    /// <summary>Finds a type by full name; null when missing (signature drift / update).</summary>
    Type? FindType(string fullTypeName);

    /// <summary>
    /// Whether <paramref name="type"/> declares a member with the given name and
    /// (when <paramref name="parameterTypeNames"/> is non-empty) a matching
    /// parameter list. Constructors are matched by declaring type.
    /// </summary>
    bool MemberExists(Type type, string memberName, string[] parameterTypeNames);
}
