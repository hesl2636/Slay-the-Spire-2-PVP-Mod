using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PvpDuel.Core.Maps;

/// <summary>
/// Game-independent mirror of the node types the split map uses
/// (subset of MegaCrit MapPointType; mapped 1:1 by DuelSplitActMap).
/// </summary>
public enum DuelNodeType
{
    Unknown,
    Shop,
    Treasure,
    RestSite,
    Monster,
    Elite,
    Boss,
    Ancient,
}

/// <summary>A grid cell in the split map topology (col 0-6, row 0 = shared start).</summary>
public sealed record DuelMapNode(int Col, int Row, DuelNodeType Type);

/// <summary>A parent→child edge (children only, matching MapPoint.AddChildPoint semantics).</summary>
public sealed record DuelMapEdge(int FromCol, int FromRow, int ToCol, int ToRow);

/// <summary>
/// Abstract topology of the symmetric split map: left path (cols 0-2) and right path
/// (cols 4-6) around a shared start (Ancient) and shared boss point in the middle
/// column. Both sides are structurally mirrored by construction; travel between
/// sides is impossible below the shared start/boss. Pure data — no game types —
/// so symmetry, determinism and reachability are unit-testable in Core.
/// </summary>
public sealed record DuelSplitTopology(
    int Columns,
    int MiddleCol,
    int PathRows,
    IReadOnlyList<DuelMapNode> Nodes,
    IReadOnlyList<DuelMapEdge> Edges)
{
    public const int StartRow = 0;

    /// <summary>Row of the shared boss point (one past the last room row, ActMap convention).</summary>
    public int BossRow => PathRows + 1;

    public bool IsStart(int col, int row) => col == MiddleCol && row == StartRow;

    public bool IsBoss(int col, int row) => col == MiddleCol && row == BossRow;

    public bool IsLeft(int col) => col < MiddleCol;

    public bool IsRight(int col) => col > MiddleCol;

    /// <summary>Mirror column across the middle column (7 columns → col' = 6 - col).</summary>
    public int MirrorCol(int col) => Columns - 1 - col;

    public DuelMapNode NodeAt(int col, int row) =>
        Nodes.First(n => n.Col == col && n.Row == row);

    public IEnumerable<DuelMapNode> NodesInRow(int row) =>
        Nodes.Where(n => n.Row == row).OrderBy(n => n.Col);

    /// <summary>
    /// Deterministic textual dump of nodes and edges — the canonical comparison
    /// artifact for the same-seed determinism and cross-client map-equality tests.
    /// </summary>
    public string Dump()
    {
        var sb = new StringBuilder();
        sb.Append("map columns=").Append(Columns)
          .Append(" middle=").Append(MiddleCol)
          .Append(" pathRows=").Append(PathRows)
          .AppendLine();
        foreach (var n in Nodes.OrderBy(n => n.Row).ThenBy(n => n.Col))
        {
            sb.Append("node ").Append(n.Col).Append(',').Append(n.Row)
              .Append('=').Append(n.Type).AppendLine();
        }
        foreach (var e in Edges.OrderBy(e => e.FromRow).ThenBy(e => e.FromCol)
                               .ThenBy(e => e.ToRow).ThenBy(e => e.ToCol))
        {
            sb.Append("edge ").Append(e.FromCol).Append(',').Append(e.FromRow)
              .Append("->").Append(e.ToCol).Append(',').Append(e.ToRow).AppendLine();
        }
        return sb.ToString();
    }
}
