using System;
using System.Collections.Generic;

namespace PvpDuel.Core.Maps;

/// <summary>
/// Deterministic, game-independent generator for the symmetric split map.
/// The left topology (cols 0-2) is generated first from a seed-derived RNG and
/// then mirrored onto the right (cols 4-6), so both sides are structurally
/// identical by construction. All randomness flows from (runSeed, actIndex)
/// via SplitMix64 — no wall clock, no frame counters, no System.Random —
/// so two clients with the same run seed produce byte-identical dumps.
///
/// Shape invariants (enforced below, asserted in tests):
/// - room rows 1..PathRows; the middle lane (col 1 of a side) is present in
///   every row so every side node keeps at least one parent and one child;
/// - first row is a full 3-node row (mirrors StandardActMap row 1);
/// - last room row is a full 3-node row, every node links into the shared boss;
/// - row 1 = Monster, last row = RestSite, row PathRows-7 = Treasure
///   (vanilla StandardActMap.AssignPointTypes conventions).
/// </summary>
public static class DuelSplitTopologyGenerator
{
    public const int Columns = 7;
    public const int MiddleCol = 3;
    public const int LeftMaxCol = 2;
    public const int RightMinCol = 4;
    public const int StartRow = 0;
    public const int DefaultPathRows = 15;

    private const int MinPathRows = 5;

    private const int OuterLanePresencePercent = 80;
    private const int SecondChildPercent = 35;

    public static DuelSplitTopology Generate(ulong seed, int actIndex, int pathRows = DefaultPathRows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pathRows, MinPathRows);
        var rng = new SplitMix64(SplitMix64.Mix(seed, unchecked((ulong)actIndex)));

        bool[][] presence = BuildPresence(pathRows, rng);
        List<(int FromRow, int FromCol, int ToRow, int ToCol)> leftEdges = BuildLeftEdges(presence, pathRows, rng);
        DuelNodeType?[,] leftTypes = AssignLeftTypes(presence, pathRows, rng);

        int bossRow = pathRows + 1;
        var nodes = new List<DuelMapNode>
        {
            new(MiddleCol, StartRow, DuelNodeType.Ancient),
            new(MiddleCol, bossRow, DuelNodeType.Boss),
        };
        var edges = new List<DuelMapEdge>();

        // First row: the shared Ancient start feeds the left and right entry nodes.
        for (int col = 0; col <= LeftMaxCol; col++)
        {
            if (!presence[1][col])
            {
                continue;
            }
            nodes.Add(new DuelMapNode(col, 1, leftTypes[1, col]!.Value));
            nodes.Add(new DuelMapNode(Mirror(col), 1, leftTypes[1, col]!.Value));
            edges.Add(new DuelMapEdge(MiddleCol, StartRow, col, 1));
            edges.Add(new DuelMapEdge(MiddleCol, StartRow, Mirror(col), 1));
        }

        // Room rows 2..PathRows: mirrored node pairs.
        for (int row = 2; row <= pathRows; row++)
        {
            for (int col = 0; col <= LeftMaxCol; col++)
            {
                if (!presence[row][col])
                {
                    continue;
                }
                nodes.Add(new DuelMapNode(col, row, leftTypes[row, col]!.Value));
                nodes.Add(new DuelMapNode(Mirror(col), row, leftTypes[row, col]!.Value));
            }
        }

        // Left edges plus their mirror.
        foreach (var (fromRow, fromCol, toRow, toCol) in leftEdges)
        {
            edges.Add(new DuelMapEdge(fromCol, fromRow, toCol, toRow));
            edges.Add(new DuelMapEdge(Mirror(fromCol), fromRow, Mirror(toCol), toRow));
        }

        // Last room row: both sides converge on the shared boss point.
        for (int col = 0; col <= LeftMaxCol; col++)
        {
            if (!presence[pathRows][col])
            {
                continue;
            }
            edges.Add(new DuelMapEdge(col, pathRows, MiddleCol, bossRow));
            edges.Add(new DuelMapEdge(Mirror(col), pathRows, MiddleCol, bossRow));
        }

        return new DuelSplitTopology(Columns, MiddleCol, pathRows, nodes, edges);
    }

    private static int Mirror(int col) => Columns - 1 - col;

    /// <summary>
    /// Row-by-row lane presence on the left side (index 0..2 = cols 0..2).
    /// The middle lane is always present, keeping parent/child chains alive;
    /// the outer lanes drop in and out with the seeded rng.
    /// </summary>
    private static bool[][] BuildPresence(int pathRows, SplitMix64 rng)
    {
        var presence = new bool[pathRows + 1][];
        presence[1] = [true, true, true];
        for (int row = 2; row < pathRows; row++)
        {
            presence[row] =
            [
                rng.NextInt(100) < OuterLanePresencePercent,
                true,
                rng.NextInt(100) < OuterLanePresencePercent,
            ];
        }
        presence[pathRows] = [true, true, true];
        return presence;
    }

    /// <summary>
    /// Edges between consecutive room rows on the left side only (±1 column moves).
    /// Each present parent picks one primary child (plus a second with some
    /// probability); afterwards every present child that no parent chose is
    /// backfilled with a seeded parent edge, so no node is ever orphaned.
    /// </summary>
    private static List<(int FromRow, int FromCol, int ToRow, int ToCol)> BuildLeftEdges(
        bool[][] presence, int pathRows, SplitMix64 rng)
    {
        var edges = new List<(int, int, int, int)>();
        for (int row = 1; row < pathRows; row++)
        {
            for (int col = 0; col <= LeftMaxCol; col++)
            {
                if (!presence[row][col])
                {
                    continue;
                }
                foreach (int child in PickChildren(rng, col, presence[row + 1]))
                {
                    edges.Add((row, col, row + 1, child));
                }
            }

            // Backfill: every present child needs at least one parent.
            var parented = edges.Where(e => e.Item1 == row).Select(e => e.Item4).ToHashSet();
            for (int child = 0; child <= LeftMaxCol; child++)
            {
                if (!presence[row + 1][child] || parented.Contains(child))
                {
                    continue;
                }
                List<int> parents = [];
                for (int parent = Math.Max(0, child - 1); parent <= Math.Min(LeftMaxCol, child + 1); parent++)
                {
                    if (presence[row][parent])
                    {
                        parents.Add(parent);
                    }
                }
                // The always-present middle lane guarantees a non-empty parent set.
                edges.Add((row, parents[rng.NextInt(parents.Count)], row + 1, child));
            }
        }
        return edges;
    }

    /// <summary>Non-empty set of children (±1 column) a parent connects to in the next row.</summary>
    private static List<int> PickChildren(SplitMix64 rng, int col, bool[] nextRow)
    {
        List<int> candidates = [];
        for (int child = Math.Max(0, col - 1); child <= Math.Min(LeftMaxCol, col + 1); child++)
        {
            if (nextRow[child])
            {
                candidates.Add(child);
            }
        }

        int first = candidates[rng.NextInt(candidates.Count)];
        var children = new List<int> { first };
        foreach (int other in candidates)
        {
            if (other != first && rng.NextInt(100) < SecondChildPercent)
            {
                children.Add(other);
            }
        }
        return children;
    }
    /// <summary>
    /// Left-side node types. Vanilla-convention forced rows (entry monsters, rest
    /// sites before the boss, treasure 7 rows above the last room row), then a
    /// seeded distribution of Elite/Shop/Unknown over the rest; leftovers stay
    /// Monster. The right side inherits these types verbatim via mirroring.
    /// </summary>
    private static DuelNodeType?[,] AssignLeftTypes(bool[][] presence, int pathRows, SplitMix64 rng)
    {
        var types = new DuelNodeType?[pathRows + 1, 3];
        for (int col = 0; col <= LeftMaxCol; col++)
        {
            types[1, col] = DuelNodeType.Monster;
            types[pathRows, col] = DuelNodeType.RestSite;
        }

        int treasureRow = pathRows - 7;
        if (treasureRow >= 2)
        {
            for (int col = 0; col <= LeftMaxCol; col++)
            {
                types[treasureRow, col] = DuelNodeType.Treasure;
            }
        }

        var open = new List<(int Row, int Col)>();
        for (int row = 2; row < pathRows; row++)
        {
            if (row == treasureRow)
            {
                continue;
            }
            for (int col = 0; col <= LeftMaxCol; col++)
            {
                if (presence[row][col] && types[row, col] is null)
                {
                    open.Add((row, col));
                }
            }
        }

        for (int i = open.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (open[i], open[j]) = (open[j], open[i]);
        }

        var queue = new Queue<DuelNodeType>([DuelNodeType.Elite, DuelNodeType.Shop, DuelNodeType.Elite, DuelNodeType.Shop]);
        int unknowns = open.Count / 3;
        for (int i = 0; i < unknowns; i++)
        {
            queue.Enqueue(DuelNodeType.Unknown);
        }

        foreach (var (row, col) in open)
        {
            types[row, col] = TakeFitting(queue, row, pathRows);
        }
        return types;
    }

    /// <summary>Vanilla-style lane rules: elites never in the bottom 6 rows or the top 4; shops anywhere open.</summary>
    private static DuelNodeType TakeFitting(Queue<DuelNodeType> queue, int row, int pathRows)
    {
        int attempts = queue.Count;
        for (int i = 0; i < attempts; i++)
        {
            DuelNodeType type = queue.Dequeue();
            bool fits = type switch
            {
                DuelNodeType.Elite => row >= 6 && row <= pathRows - 4,
                DuelNodeType.Shop => row >= 2,
                _ => true,
            };
            if (fits)
            {
                return type;
            }
            queue.Enqueue(type);
        }
        return DuelNodeType.Monster;
    }
}

/// <summary>
/// SplitMix64 — small, fully deterministic 64-bit PRNG (Steele et al. 2014).
/// Only non-cryptographic stream quality is required here; the whole point is
/// that both clients derive identical streams from the shared run seed.
/// </summary>
internal sealed class SplitMix64
{
    private const ulong Gamma = 0x9E3779B97F4A7C15UL;

    private ulong _state;

    public SplitMix64(ulong state) => _state = state;

    /// <summary>Derives a generator seed from the run seed and an index (act number).</summary>
    public static ulong Mix(ulong seed, ulong index)
    {
        unchecked
        {
            return Finalize(seed ^ (index + Gamma));
        }
    }

    private static ulong Finalize(ulong z)
    {
        unchecked
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    public ulong Next()
    {
        unchecked
        {
            _state += Gamma;
            return Finalize(_state);
        }
    }

    /// <summary>Uniform-ish value in [0, maxExclusive); maxExclusive must be positive.</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        }
        return (int)(Next() % (uint)maxExclusive);
    }
}
