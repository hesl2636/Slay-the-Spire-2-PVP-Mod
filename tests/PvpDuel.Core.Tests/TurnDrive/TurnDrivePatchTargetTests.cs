using System.Reflection;

using PvpDuel.Core.SelfCheck;
using PvpDuel.SelfCheck;

using Xunit;

namespace PvpDuel.Core.Tests.TurnDrive;

/// <summary>
/// Ticket #18 (T6): the TurnDrive patch set is declared in the catalog and
/// every target actually exists in the running game build. Game-DLL asserts
/// use static reflection only (no game singletons).
/// </summary>
public class TurnDrivePatchTargetTests
{
    /// <summary>Probe mirroring AccessToolsProbe semantics with plain reflection.</summary>
    private sealed class ReflectionProbe : IPatchTargetProbe
    {
        public Type? FindType(string fullTypeName) =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => SafeGetType(a, fullTypeName))
                .FirstOrDefault(t => t != null);

        private static Type? SafeGetType(Assembly assembly, string fullTypeName)
        {
            try
            {
                return assembly.GetType(fullTypeName);
            }
            catch
            {
                return null;
            }
        }

        public bool MemberExists(Type type, string memberName, string[] parameterTypeNames)
        {
            if (memberName == ".ctor")
            {
                return type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Any(c => Matches(c.GetParameters(), parameterTypeNames));
            }

            var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (members.OfType<MethodInfo>().Any(m =>
                    m.Name == memberName
                    && (parameterTypeNames.Length == 0 || Matches(m.GetParameters(), parameterTypeNames))))
            {
                return true;
            }
            return members.Any(m =>
                m.MemberType is MemberTypes.Property or MemberTypes.Field && m.Name == memberName);
        }

        private static bool Matches(ParameterInfo[] parameters, string[] expected)
        {
            if (parameters.Length != expected.Length)
            {
                return false;
            }
            return parameters.Zip(expected, (p, e) => SimpleTypeName(p.ParameterType) == e)
                .All(match => match);
        }

        private static string SimpleTypeName(Type type)
        {
            var name = type.Name;
            var arity = name.IndexOf('`');
            return arity > 0 ? name[..arity] : name;
        }
    }

    [Fact]
    public void Catalog_DeclaresTurnDriveSetWithAllTargets()
    {
        var targets = PatchTargetCatalog.Build().Targets;
        var turnDrive = targets.Where(t => t.PatchSetId == PatchTargetCatalog.PatchSetIds.TurnDrive).ToList();

        Assert.Equal(8, turnDrive.Count);

        var cm = "MegaCrit.Sts2.Core.Combat.CombatManager";
        Assert.Contains(turnDrive, t => t.TypeName == cm && t.MemberName == "ExecuteEnemyTurn"
            && t.ParameterTypes.SequenceEqual(["CombatTurnState", "Func"]));
        Assert.Contains(turnDrive, t => t.TypeName == cm && t.MemberName == "SetupPlayerTurn"
            && t.ParameterTypes.SequenceEqual(["CombatTurnState", "Player", "HookPlayerChoiceContext"]));
        Assert.Contains(turnDrive, t => t.TypeName == cm && t.MemberName == "RunAutoPrePlayPhase"
            && t.ParameterTypes.SequenceEqual(["CombatTurnState", "HookPlayerChoiceContext", "Task", "Player"]));
        Assert.Contains(turnDrive, t => t.TypeName == cm && t.MemberName == "EndPlayerTurnPhaseOneInternal"
            && t.ParameterTypes.SequenceEqual(["CombatTurnState"]));
        Assert.Contains(turnDrive, t => t.TypeName == cm && t.MemberName == "EndPlayerTurnPhaseTwoInternal"
            && t.ParameterTypes.SequenceEqual(["CombatTurnState"]));
        Assert.Contains(turnDrive, t => t.TypeName == cm && t.MemberName == "AllPlayersReadyToEndTurn"
            && t.ParameterTypes.SequenceEqual(["CombatTurnState"]));
        Assert.Contains(turnDrive, t => t.TypeName == cm && t.MemberName == "SetReadyToBeginEnemyTurn"
            && t.ParameterTypes.SequenceEqual(["Player", "Func"]));
        Assert.Contains(turnDrive, t => t.TypeName == cm && t.MemberName == "SetUpCombat"
            && t.ParameterTypes.SequenceEqual(["CombatState"]));
    }

    [Fact]
    public void Catalog_TurnDriveTargetsPassSelfCheckAgainstLoadedGameAssembly()
    {
        var manifest = new PatchManifest();
        manifest.AddRange(PatchTargetCatalog.Build().Targets
            .Where(t => t.PatchSetId == PatchTargetCatalog.PatchSetIds.TurnDrive));

        var result = PatchSelfCheck.Run(manifest, new ReflectionProbe());
        Assert.True(result.AllTargetsExist, string.Join("\n", result.Warnings()));
        Assert.True(result.IsPatchSetEnabled(PatchTargetCatalog.PatchSetIds.TurnDrive));
    }
}
