using System.Reflection;

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;

using PvpDuel.Core.SelfCheck;
using PvpDuel.Logging;
using PvpDuel.SelfCheck;

using Xunit;

namespace PvpDuel.Core.Tests;

/// <summary>
/// Ticket #19 (T7): the LossIntercept patch targets are declared in the
/// catalog and actually exist in the running game build (spec §8: signature
/// drift disables the set instead of crashing). Game-DLL asserts use static
/// reflection only (no game singletons — the Logger static ctor 0xC0000005
/// rule).
/// </summary>
public class LossInterceptTargetTests
{
    public LossInterceptTargetTests()
    {
        // The game Logger static ctor crashes the test host (0xC0000005).
        PvpDuelLog.Enabled = false;
    }

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
    public void Catalog_DeclaresLossInterceptSetWithAllTargets()
    {
        // The catalog is shared with sibling patch-set tickets: assert only
        // this ticket's own set and targets here.
        var targets = PatchTargetCatalog.Build().Targets;

        var lossIntercept = targets.Where(t => t.PatchSetId == "LossIntercept").ToList();
        Assert.Equal(2, lossIntercept.Count);
        Assert.Contains(lossIntercept, t => t.TypeName.EndsWith("CreatureCmd") && t.MemberName == "Kill");
        Assert.Contains(lossIntercept, t => t.TypeName.EndsWith("CombatManager") && t.MemberName == "LoseCombat");

        var kill = lossIntercept.Single(t => t.MemberName == "Kill");
        Assert.Equal(["IReadOnlyCollection", "Boolean"], kill.ParameterTypes);
    }

    [Fact]
    public void Catalog_LossInterceptTargetsPassSelfCheckAgainstLoadedGameAssembly()
    {
        // Scope to this ticket's set: sibling sets are asserted by their own tests.
        var manifest = new PatchManifest();
        manifest.AddRange(PatchTargetCatalog.Build().Targets.Where(t => t.PatchSetId == "LossIntercept"));

        var result = PatchSelfCheck.Run(manifest, new ReflectionProbe());
        Assert.True(result.AllTargetsExist, string.Join("\n", result.Warnings()));
        Assert.True(result.IsPatchSetEnabled("LossIntercept"));
    }

    [Fact]
    public void GameDll_KillBatchOverload_ExistsAndIsTheListForm()
    {
        // The single-creature overload delegates to the batch overload
        // (decomp CreatureCmd.cs:446-449), so patching the batch form covers
        // every kill path.
        var batch = typeof(CreatureCmd).GetMethod(
            "Kill", BindingFlags.Public | BindingFlags.Static,
            [typeof(IReadOnlyCollection<Creature>), typeof(bool)]);
        Assert.NotNull(batch);

        var single = typeof(CreatureCmd).GetMethod(
            "Kill", BindingFlags.Public | BindingFlags.Static,
            [typeof(Creature), typeof(bool)]);
        Assert.NotNull(single);
    }

    [Fact]
    public void GameDll_LoseCombat_IsPublicVoidSolePendingLossSetter()
    {
        var loseCombat = typeof(CombatManager).GetMethod(
            "LoseCombat", BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        Assert.NotNull(loseCombat);
        Assert.True(loseCombat!.IsPublic);
        Assert.Equal(typeof(void), loseCombat.ReturnType);
        Assert.Empty(loseCombat.GetParameters());
    }
}
