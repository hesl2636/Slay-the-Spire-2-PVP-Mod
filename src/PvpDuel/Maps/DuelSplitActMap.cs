using System.Collections.Generic;
using MegaCrit.Sts2.Core.Map;
using PvpDuel.Core.Maps;

namespace PvpDuel.Maps;

/// <summary>
/// Symmetric split-path act map (ticket #16): the left path (cols 0-2) and the
/// right path (cols 4-6) around a shared Ancient start and shared boss point in
/// the middle column, exactly like ActMap's own conventions (StandardActMap /
/// GoldenPathActMap precedents):
/// - Grid holds rows 0..PathRows; row 0 holds the StartingMapPoint (Ancient),
///   the boss point lives at (middle, PathRows+1), outside the grid;
/// - edges are built exclusively with MapPoint.AddChildPoint, so travel
///   (MapTravel.GetTravelablePointsFrom reads Children) is restricted to a
///   player's own side naturally — no MapTravel changes;
/// - boss is a child of every last-row node on both sides, so both paths converge.
/// </summary>
public sealed class DuelSplitActMap : ActMap
{
    private readonly MapPoint?[,] _grid;

    public override MapPoint BossMapPoint { get; }

    public override MapPoint StartingMapPoint { get; }

    protected override MapPoint?[,] Grid => _grid;

    public DuelSplitActMap(DuelSplitTopology topology)
    {
        // Rows 0..PathRows (count PathRows+1); the boss coordinate sits at
        // GetRowCount(), one row past the grid — same as StandardActMap.
        _grid = new MapPoint?[topology.Columns, topology.PathRows + 1];

        StartingMapPoint = new MapPoint(topology.MiddleCol, 0)
        {
            PointType = MapPointType.Ancient,
        };
        _grid[topology.MiddleCol, 0] = StartingMapPoint;

        BossMapPoint = new MapPoint(topology.MiddleCol, topology.BossRow)
        {
            PointType = MapPointType.Boss,
        };

        var byCoord = new Dictionary<(int Col, int Row), MapPoint>();
        foreach (var node in topology.Nodes)
        {
            if (topology.IsStart(node.Col, node.Row) || topology.IsBoss(node.Col, node.Row))
            {
                byCoord[(node.Col, node.Row)] = topology.IsStart(node.Col, node.Row)
                    ? StartingMapPoint
                    : BossMapPoint;
                continue;
            }
            var point = new MapPoint(node.Col, node.Row)
            {
                PointType = ToGameType(node.Type),
            };
            _grid[node.Col, node.Row] = point;
            byCoord[(node.Col, node.Row)] = point;
        }

        foreach (var edge in topology.Edges)
        {
            byCoord[(edge.FromCol, edge.FromRow)].AddChildPoint(byCoord[(edge.ToCol, edge.ToRow)]);
        }

        // ActMap convention: row-1 nodes are the walkable starts off the shared start.
        foreach (var node in topology.NodesInRow(1))
        {
            startMapPoints.Add(byCoord[(node.Col, node.Row)]);
        }
    }

    private static MapPointType ToGameType(DuelNodeType type) => type switch
    {
        DuelNodeType.Unknown => MapPointType.Unknown,
        DuelNodeType.Shop => MapPointType.Shop,
        DuelNodeType.Treasure => MapPointType.Treasure,
        DuelNodeType.RestSite => MapPointType.RestSite,
        DuelNodeType.Monster => MapPointType.Monster,
        DuelNodeType.Elite => MapPointType.Elite,
        DuelNodeType.Boss => MapPointType.Boss,
        DuelNodeType.Ancient => MapPointType.Ancient,
        _ => MapPointType.Monster,
    };
}
