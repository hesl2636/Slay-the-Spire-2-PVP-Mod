using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Map;
using PvpDuel.Core.Maps;
using PvpDuel.Maps;
using Xunit;

namespace PvpDuel.Core.Tests.Maps;

/// <summary>
/// Game-surface mapping test (ticket #16 acceptance 3): the ActMap subclass is
/// constructible in a plain test process (no Godot runtime needed — MapPoint
/// and ActMap are plain C#). Verifies that the Core topology survives the
/// mapping into the real game types: grid dimensions, start/boss placement and
/// types, mirrored edge sets, and — critically — that Children connectivity
/// (what MapTravel reads) keeps the two sides isolated while both reach boss.
/// </summary>
public sealed class DuelSplitActMapMappingTests
{
    private static DuelSplitActMap Build(ulong seed = 42UL, int pathRows = DuelSplitTopologyGenerator.DefaultPathRows)
    {
        var topology = DuelSplitTopologyGenerator.Generate(seed, actIndex: 0, pathRows: pathRows);
        return new DuelSplitActMap(topology);
    }

    [Fact]
    public void Grid_HasVanillaDimensions()
    {
        var map = Build(pathRows: 15);
        Assert.Equal(7, map.GetColumnCount());
        Assert.Equal(16, map.GetRowCount());
    }

    [Fact]
    public void Start_IsSharedAncient_AtMiddleOfRow0()
    {
        var map = Build();
        var start = map.StartingMapPoint;
        Assert.Equal(3, start.coord.col);
        Assert.Equal(0, start.coord.row);
        Assert.Equal(MapPointType.Ancient, start.PointType);
        Assert.Same(start, map.GetPoint(3, 0));

        // Both sides hang off the shared start.
        Assert.Equal(6, start.Children.Count);
        Assert.Equal(
            new HashSet<int> { 0, 1, 2, 4, 5, 6 },
            start.Children.Select(c => c.coord.col).ToHashSet());
    }

    [Fact]
    public void Boss_IsShared_AtMiddlePastLastRoomRow()
    {
        var map = Build();
        var boss = map.BossMapPoint;
        Assert.Equal(3, boss.coord.col);
        Assert.Equal(map.GetRowCount(), boss.coord.row);
        Assert.Equal(MapPointType.Boss, boss.PointType);
        Assert.Same(boss, map.GetPoint(3, map.GetRowCount()));

        // Boss is a child of every last-row node on both sides.
        var lastRowParents = boss.parents.Select(p => p.coord.col).ToHashSet();
        Assert.Equal(new HashSet<int> { 0, 1, 2, 4, 5, 6 }, lastRowParents);
    }

    [Fact]
    public void Grid_MatchesTopology_Exactly()
    {
        var topology = DuelSplitTopologyGenerator.Generate(42UL, actIndex: 0);
        var map = new DuelSplitActMap(topology);
        var expected = topology.Nodes
            .Where(n => !topology.IsBoss(n.Col, n.Row)) // boss lives outside the grid
            .Select(n => (n.Col, n.Row))
            .ToHashSet();

        var actual = new HashSet<(int Col, int Row)>();
        for (int row = 0; row < map.GetRowCount(); row++)
        {
            for (int col = 0; col < map.GetColumnCount(); col++)
            {
                if (map.GetPoint(col, row) is not null)
                {
                    actual.Add((col, row));
                }
            }
        }
        Assert.Equal(expected, actual);

        // Middle column: only the shared start; outer lanes may be sparse.
        Assert.DoesNotContain(actual, c => c.Col == 3 && c.Row != 0);
    }

    [Fact]
    public void Children_Connectivity_MatchesCoreTopology_SidesIsolated_BossShared()
    {
        var map = Build();
        var start = map.StartingMapPoint;
        var boss = map.BossMapPoint;

        foreach (var sideStart in start.Children.ToList())
        {
            bool leftSide = sideStart.coord.col < 3;
            var visited = new HashSet<MapPoint> { sideStart };
            var queue = new Queue<MapPoint>();
            queue.Enqueue(sideStart);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var child in current.Children)
                {
                    if (visited.Add(child))
                    {
                        queue.Enqueue(child);
                    }
                }
            }

            // Same-side path only: no node of the opposite side is reachable
            // (the shared middle column — start row 0 and boss — is exempt)…
            Assert.DoesNotContain(visited, p => p.coord.col != 3 && (p.coord.col < 3) != leftSide);
            // …but the shared boss is reached.
            Assert.Contains(boss, visited);
        }
    }

    [Fact]
    public void GetAllMapPoints_HasNoOrphans()
    {
        var map = Build();
        var points = map.GetAllMapPoints().ToList();
        Assert.All(points, p =>
        {
            if (p.PointType is MapPointType.Boss or MapPointType.Ancient)
            {
                return;
            }
            Assert.NotEmpty(p.parents);
            Assert.NotEmpty(p.Children);
        });
    }

    [Fact]
    public void TwoClients_SameSeed_SeeSameMap()
    {
        var a = Build(seed: 987654321UL);
        var b = Build(seed: 987654321UL);
        Assert.Equal(
            a.GetAllMapPoints().Select(p => $"{p.coord.col},{p.coord.row}={p.PointType}|{string.Join(";", p.Children.Select(c => $"{c.coord.col},{c.coord.row}"))}")
                .OrderBy(s => s),
            b.GetAllMapPoints().Select(p => $"{p.coord.col},{p.coord.row}={p.PointType}|{string.Join(";", p.Children.Select(c => $"{c.coord.col},{c.coord.row}"))}")
                .OrderBy(s => s));
    }
}
