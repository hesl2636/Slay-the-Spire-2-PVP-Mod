using System.Collections.Generic;
using System.Linq;
using PvpDuel.Core.Maps;
using Xunit;

namespace PvpDuel.Core.Tests.Maps;

/// <summary>
/// Core contract for the symmetric split map topology: same-seed determinism,
/// left/right structural symmetry, side isolation, and full connectivity to
/// the shared boss. These are the auto-verifiable acceptance criteria of
/// ticket #16; the in-game walk behavior builds on the same edge set.
/// </summary>
public sealed class DuelSplitTopologyTests
{
    private static DuelSplitTopology Generate(ulong seed, int actIndex = 0, int pathRows = DuelSplitTopologyGenerator.DefaultPathRows) =>
        DuelSplitTopologyGenerator.Generate(seed, actIndex, pathRows);

    public static IEnumerable<object[]> Seeds => new[]
    {
        new object[] { 0UL },
        new object[] { 1UL },
        new object[] { 0xDEADBEEFUL },
        new object[] { ulong.MaxValue },
        new object[] { 1234567890123456789UL },
    };

    [Theory]
    [MemberData(nameof(Seeds))]
    public void SameSeed_GeneratesIdenticalDump(ulong seed)
    {
        var first = Generate(seed);
        var second = Generate(seed);
        Assert.Equal(first.Dump(), second.Dump());
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void SameSeed_EveryRowCount_YieldsIdenticalTopology(ulong seed)
    {
        foreach (int pathRows in new[] { 5, 8, 15, 22 })
        {
            var first = Generate(seed, pathRows: pathRows);
            var second = Generate(seed, pathRows: pathRows);
            Assert.Equal(first.Nodes, second.Nodes);
            Assert.Equal(first.Edges, second.Edges);
        }
    }

    [Fact]
    public void DifferentActIndex_ChangesTopology()
    {
        var act0 = Generate(42UL, actIndex: 0);
        var act1 = Generate(42UL, actIndex: 1);
        var act2 = Generate(42UL, actIndex: 2);
        Assert.NotEqual(act0.Dump(), act1.Dump());
        Assert.NotEqual(act1.Dump(), act2.Dump());
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Topology_IsLeftRightMirrored(ulong seed)
    {
        var t = Generate(seed);
        var edgeSet = t.Edges.ToHashSet();

        foreach (var edge in t.Edges)
        {
            if (!t.IsLeft(edge.FromCol) && !t.IsRight(edge.FromCol))
            {
                continue; // start/boss edges are shared, not mirrored pairs
            }
            var mirror = new DuelMapEdge(t.MirrorCol(edge.FromCol), edge.FromRow, t.MirrorCol(edge.ToCol), edge.ToRow);
            Assert.Contains(mirror, edgeSet);
        }

        foreach (var node in t.Nodes)
        {
            if (node.Col == t.MiddleCol)
            {
                Assert.True(t.IsStart(node.Col, node.Row) || t.IsBoss(node.Col, node.Row),
                    $"middle column may only hold the shared start/boss, saw {node}");
                continue;
            }
            var mirrored = t.NodeAt(t.MirrorCol(node.Col), node.Row);
            Assert.Equal(node.Type, mirrored.Type);
        }

        int leftCount = t.Nodes.Count(n => t.IsLeft(n.Col));
        int rightCount = t.Nodes.Count(n => t.IsRight(n.Col));
        Assert.Equal(leftCount, rightCount);
        Assert.True(leftCount > 0);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Topology_SidesAreIsolated_ButBossReachesBoth(ulong seed)
    {
        var t = Generate(seed);
        var children = Children(t);
        var start = t.NodeAt(t.MiddleCol, DuelSplitTopology.StartRow);
        var boss = t.NodeAt(t.MiddleCol, t.BossRow);

        var leftNodes = t.Nodes.Where(n => t.IsLeft(n.Col)).ToList();
        var rightNodes = t.Nodes.Where(n => t.IsRight(n.Col)).ToList();
        Assert.NotEmpty(leftNodes);
        Assert.NotEmpty(rightNodes);

        foreach (var node in leftNodes)
        {
            var reachable = Reachable(node, children);
            // A left node can never step onto a right-side path node…
            Assert.DoesNotContain(reachable, rightNodes.Contains);
            // …but must always reach the shared boss.
            Assert.Contains(boss, reachable);
        }

        foreach (var node in rightNodes)
        {
            var reachable = Reachable(node, children);
            Assert.DoesNotContain(reachable, leftNodes.Contains);
            Assert.Contains(boss, reachable);
        }

        // The shared start reaches every node on both sides plus the boss.
        Assert.Equal(t.Nodes.ToHashSet(), Reachable(start, children));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Topology_HasNoOrphans_EveryNodeHasParentAndChild(ulong seed)
    {
        var t = Generate(seed);
        var edgeSet = t.Edges.ToHashSet();
        var parented = t.Edges.Select(e => (e.ToCol, e.ToRow)).ToHashSet();
        var childed = t.Edges.Select(e => (e.FromCol, e.FromRow)).ToHashSet();

        foreach (var node in t.Nodes)
        {
            var key = (node.Col, node.Row);
            if (t.IsStart(node.Col, node.Row))
            {
                Assert.DoesNotContain(key, parented);
            }
            else
            {
                Assert.Contains(key, parented);
            }
            if (t.IsBoss(node.Col, node.Row))
            {
                Assert.DoesNotContain(key, childed);
            }
            else
            {
                Assert.Contains(key, childed);
            }
        }

        // No duplicated edges.
        Assert.Equal(t.Edges.Count, edgeSet.Count);
    }

    [Fact]
    public void Topology_StructuralConstants_Hold()
    {
        var t = Generate(7UL);
        Assert.Equal(DuelSplitTopologyGenerator.Columns, t.Columns);
        Assert.Equal(DuelSplitTopologyGenerator.MiddleCol, t.MiddleCol);
        Assert.Equal(t.PathRows + 1, t.BossRow);

        // Entry row is all monsters; final room row is all rest sites; shared types.
        Assert.All(t.NodesInRow(1), n => Assert.Equal(DuelNodeType.Monster, n.Type));
        Assert.All(t.NodesInRow(t.PathRows), n => Assert.Equal(DuelNodeType.RestSite, n.Type));
        Assert.Equal(DuelNodeType.Ancient, t.NodeAt(t.MiddleCol, DuelSplitTopology.StartRow).Type);
        Assert.Equal(DuelNodeType.Boss, t.NodeAt(t.MiddleCol, t.BossRow).Type);

        // Treasure row sits 7 rows above the last room row (vanilla convention).
        var treasureRow = t.NodesInRow(t.PathRows - 7).ToList();
        Assert.NotEmpty(treasureRow);
        Assert.All(treasureRow, n => Assert.Equal(DuelNodeType.Treasure, n.Type));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    public void NonDefault_RowCounts_RemainValid(int pathRows)
    {
        var t = Generate(99UL, pathRows: pathRows);
        Assert.Equal(t.Nodes.Count, t.Nodes.Distinct().Count());
        var children = Children(t);
        var boss = t.NodeAt(t.MiddleCol, t.BossRow);
        foreach (var node in t.Nodes.Where(n => !t.IsBoss(n.Col, n.Row)))
        {
            Assert.Contains(boss, Reachable(node, children));
        }
    }

    [Fact]
    public void SmallPathRows_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Generate(1UL, pathRows: 4));
    }

    private static Dictionary<(int Col, int Row), List<DuelMapNode>> Children(DuelSplitTopology t)
    {
        var byCoord = t.Nodes.ToDictionary(n => (n.Col, n.Row));
        var children = new Dictionary<(int Col, int Row), List<DuelMapNode>>();
        foreach (var edge in t.Edges)
        {
            if (!children.TryGetValue((edge.FromCol, edge.FromRow), out var list))
            {
                list = [];
                children[(edge.FromCol, edge.FromRow)] = list;
            }
            list.Add(byCoord[(edge.ToCol, edge.ToRow)]);
        }
        return children;
    }

    private static HashSet<DuelMapNode> Reachable(DuelMapNode from, Dictionary<(int Col, int Row), List<DuelMapNode>> children)
    {
        var visited = new HashSet<DuelMapNode> { from };
        var queue = new Queue<DuelMapNode>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!children.TryGetValue((current.Col, current.Row), out var next))
            {
                continue;
            }
            foreach (var child in next)
            {
                if (visited.Add(child))
                {
                    queue.Enqueue(child);
                }
            }
        }
        return visited;
    }
}
