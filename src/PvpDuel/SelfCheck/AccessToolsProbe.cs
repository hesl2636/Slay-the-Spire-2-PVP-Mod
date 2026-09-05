using System.Reflection;
using HarmonyLib;

using PvpDuel.Core.SelfCheck;
namespace PvpDuel.SelfCheck;

/// <summary>
/// Real probe used in the live game: Harmony AccessTools/Traverse semantics,
/// tolerant of assembly-qualified and namespace-short type names.
/// </summary>
public sealed class AccessToolsProbe : IPatchTargetProbe
{
    public static readonly AccessToolsProbe Instance = new();

    private AccessToolsProbe()
    {
    }

    public Type? FindType(string fullTypeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullTypeName);
        return AccessTools.TypeByName(fullTypeName);
    }

    public bool MemberExists(Type type, string memberName, string[] parameterTypeNames)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        if (memberName == ".ctor")
        {
            return FindConstructor(type, parameterTypeNames) != null;
        }

        // Methods (incl. overloads), then properties/fields by exact name.
        var methods = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        foreach (var method in methods.OfType<MethodInfo>())
        {
            if (method.Name != memberName)
            {
                continue;
            }

            if (parameterTypeNames.Length == 0 || MatchesParameters(method.GetParameters(), parameterTypeNames))
            {
                return true;
            }
        }

        return methods.Any(m =>
            (m.MemberType == MemberTypes.Property || m.MemberType == MemberTypes.Field)
            && m.Name == memberName);
    }

    private static ConstructorInfo? FindConstructor(Type type, string[] parameterTypeNames) =>
        type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(ctor =>
                parameterTypeNames.Length == 0
                || MatchesParameters(ctor.GetParameters(), parameterTypeNames));

    private static bool MatchesParameters(ParameterInfo[] parameters, string[] parameterTypeNames)
    {
        if (parameters.Length != parameterTypeNames.Length)
        {
            return false;
        }

        return parameters.Zip(parameterTypeNames, (parameter, expected) =>
                NamesMatch(parameter.ParameterType, expected))
            .All(match => match);
    }

    private static bool NamesMatch(Type actual, string expected)
    {
        if (actual.Name == expected || actual.FullName == expected)
        {
            return true;
        }

        // Tolerate generic arity suffixes ("List`1" declared as "List").
        var arityIndex = actual.Name.IndexOf('`');
        return arityIndex > 0 && actual.Name[..arityIndex] == expected;
    }
}
