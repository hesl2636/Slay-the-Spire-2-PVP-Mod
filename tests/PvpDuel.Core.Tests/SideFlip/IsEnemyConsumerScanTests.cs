using System.Text.RegularExpressions;

using Xunit;

namespace PvpDuel.Core.Tests;

/// <summary>
/// Ticket #17 (T5) acceptance #3: the guard-list document must cover EVERY
/// IsEnemy / Side==CombatSide.Enemy consumption point found by grepping the
/// decompiled game (v0.111 decomp/ tree). Re-runs the same scan at test time
/// and asserts each file:line hit is annotated in docs/is-enemy-consumers.md.
/// Skips silently when the repo layout (decomp/ + docs/) is not present (e.g.
/// a CI checkout without the decomp tree).
/// </summary>
public class IsEnemyConsumerScanTests
{
    private const string ScanPattern = "IsEnemy|Side == CombatSide\\.Enemy|Side != CombatSide\\.Enemy";
    private static readonly Regex HitRegex = new(ScanPattern, RegexOptions.Compiled);

    private static readonly (string? Directory, string? Doc) RepoLayout = LocateRepo();
    private static bool RepoPresent => RepoLayout.Directory != null;

    [Fact]
    public void GuardListDocument_CoversEveryDecompIsEnemyHit()
    {
        if (!RepoPresent)
        {
            return; // decomp/docs not shipped with this checkout
        }

        var doc = File.ReadAllText(RepoLayout.Doc!);
        var misses = new List<string>();

        foreach (var (relativePath, line) in ScanDecomp(RepoLayout.Directory!))
        {
            var token = $"{relativePath}:{line}";
            if (!doc.Contains(token, StringComparison.Ordinal))
            {
                misses.Add(token);
            }
        }

        Assert.True(misses.Count == 0,
            "docs/is-enemy-consumers.md is missing annotations for: " + string.Join(", ", misses));
    }

    [Fact]
    public void GuardListDocument_ExistsWithExpectedSections()
    {
        if (!RepoPresent)
        {
            return;
        }
        var doc = File.ReadAllText(RepoLayout.Doc!);
        Assert.Contains("已 guard", doc);
        Assert.Contains("无需 guard", doc);
        Assert.Contains("attended", doc);
        Assert.Contains("manual items", doc);
    }

    private static IEnumerable<(string RelativePath, int Line)> ScanDecomp(string decompDir)
    {
        foreach (var file in Directory.EnumerateFiles(decompDir, "*.cs", SearchOption.AllDirectories).Order())
        {
            var relativePath = Path.GetRelativePath(decompDir, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (HitRegex.IsMatch(lines[i]))
                {
                    yield return (relativePath, i + 1);
                }
            }
        }
    }

    private static (string?, string?) LocateRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var decomp = Path.Combine(dir.FullName, "decomp");
            var doc = Path.Combine(dir.FullName, "docs", "is-enemy-consumers.md");
            if (Directory.Exists(decomp) && File.Exists(doc))
            {
                return (decomp, doc);
            }
            dir = dir.Parent;
        }
        return (null, null);
    }
}
