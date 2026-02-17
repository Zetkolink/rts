using System.Collections.Generic;
using UnityEngine;

namespace RTS.Pathfinding
{
    /// <summary>
    /// Stateless A* solver. Pure C#, no MonoBehaviour.
    /// All data structures pre-allocated and reused between Solve() calls.
    /// </summary>
    public sealed class AStarSolver
    {
        private readonly float[] _gCost;
        private readonly float[] _fCost;
        private readonly bool[] _inClosed;
        private readonly int[] _parentOf;
        private readonly int _gridWidth;
        private readonly int _gridHeight;

        // Open list as flat index list.
        // For grids up to 256x256 linear scan is acceptable.
        // Replace with BinaryHeap when profiler shows bottleneck.
        private readonly List<int> _openList;

        private const float StraightCost = 1f;
        private const float DiagonalCost = 1.41421356f;

        // 8-directional: N, NE, E, SE, S, SW, W, NW
        private static readonly int[] DX = { 0, 1, 1, 1, 0, -1, -1, -1 };
        private static readonly int[] DZ = { 1, 1, 0, -1, -1, -1, 0, 1 };
        private static readonly float[] DCost =
        {
            StraightCost, DiagonalCost, StraightCost, DiagonalCost,
            StraightCost, DiagonalCost, StraightCost, DiagonalCost
        };

        public AStarSolver(int gridWidth, int gridHeight)
        {
            _gridWidth = gridWidth;
            _gridHeight = gridHeight;
            int total = gridWidth * gridHeight;

            _gCost = new float[total];
            _fCost = new float[total];
            _inClosed = new bool[total];
            _parentOf = new int[total];
            _openList = new List<int>(1024);
        }

        /// <summary>
        /// Find path on the walkability grid.
        /// maxIterations caps computation for frame budget control.
        /// </summary>
        public PathResult Solve(
            bool[] walkable,
            Vector2Int start,
            Vector2Int end,
            int maxIterations = 10000)
        {
            var result = new PathResult();

            // ── Validate ──
            if (!InBounds(start.x, start.y) || !InBounds(end.x, end.y))
            {
                result.Status = PathStatus.Invalid;
                return result;
            }

            int startIdx = start.y * _gridWidth + start.x;
            int endIdx = end.y * _gridWidth + end.x;

            if (!walkable[startIdx])
            {
                start = FindNearestWalkable(walkable, start, 15);
                startIdx = start.y * _gridWidth + start.x;
                if (!walkable[startIdx])
                {
                    result.Status = PathStatus.Invalid;
                    return result;
                }
            }

            // If end is blocked, find nearest walkable cell
            if (!walkable[endIdx])
            {
                end = FindNearestWalkable(walkable, end, 15);
                endIdx = end.y * _gridWidth + end.x;
                if (!walkable[endIdx])
                {
                    result.Status = PathStatus.NotFound;
                    return result;
                }
            }

            // ── Reset ──
            int total = _gridWidth * _gridHeight;
            System.Array.Clear(_inClosed, 0, total);
            for (int i = 0; i < total; i++)
            {
                _gCost[i] = float.MaxValue;
                _fCost[i] = float.MaxValue;
                _parentOf[i] = -1;
            }
            _openList.Clear();

            // ── Seed start ──
            _gCost[startIdx] = 0f;
            _fCost[startIdx] = Heuristic(start.x, start.y, end.x, end.y);
            _openList.Add(startIdx);

            int iterations = 0;
            int bestClosestIdx = startIdx;
            float bestClosestH = _fCost[startIdx];

            // ── Main loop ──
            while (_openList.Count > 0 && iterations < maxIterations)
            {
                iterations++;

                // Extract min F from open list (linear scan)
                int bestOpenPos = 0;
                float bestF = _fCost[_openList[0]];
                for (int i = 1; i < _openList.Count; i++)
                {
                    float f = _fCost[_openList[i]];
                    if (f < bestF)
                    {
                        bestF = f;
                        bestOpenPos = i;
                    }
                }

                int currentIdx = _openList[bestOpenPos];

                // Swap-remove: O(1)
                _openList[bestOpenPos] = _openList[_openList.Count - 1];
                _openList.RemoveAt(_openList.Count - 1);

                // ── Goal reached ──
                if (currentIdx == endIdx)
                {
                    result.Status = PathStatus.Found;
                    result.RawCells = ReconstructPath(startIdx, endIdx);
                    result.IterationsUsed = iterations;
                    Debug.Log($"[AStar] Path found in {iterations} iterations, " +
                              $"{result.RawCells.Length} cells");
                    return result;
                }

                _inClosed[currentIdx] = true;

                int cx = currentIdx % _gridWidth;
                int cz = currentIdx / _gridWidth;

                // Track closest to goal for partial path
                float currentH = Heuristic(cx, cz, end.x, end.y);
                if (currentH < bestClosestH)
                {
                    bestClosestH = currentH;
                    bestClosestIdx = currentIdx;
                }

                // ── Expand 8 neighbors ──
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + DX[d];
                    int nz = cz + DZ[d];

                    if (!InBounds(nx, nz)) continue;

                    int nIdx = nz * _gridWidth + nx;

                    if (_inClosed[nIdx]) continue;
                    if (!walkable[nIdx]) continue;

                    // Corner cutting prevention:
                    // Diagonal move allowed only if both adjacent cardinals are walkable
                    if (d % 2 == 1) // odd = diagonal
                    {
                        int adjHorizontal = cz * _gridWidth + nx;
                        int adjVertical = nz * _gridWidth + cx;
                        if (!walkable[adjHorizontal] || !walkable[adjVertical])
                            continue;
                    }

                    float tentativeG = _gCost[currentIdx] + DCost[d];

                    if (tentativeG < _gCost[nIdx])
                    {
                        _gCost[nIdx] = tentativeG;
                        _fCost[nIdx] = tentativeG + Heuristic(nx, nz, end.x, end.y);
                        _parentOf[nIdx] = currentIdx;

                        if (!_openList.Contains(nIdx))
                            _openList.Add(nIdx);
                    }
                }
            }

            // ── Budget exhausted or no path ──
            if (bestClosestIdx != startIdx)
            {
                result.Status = PathStatus.Partial;
                result.RawCells = ReconstructPath(startIdx, bestClosestIdx);
                Debug.Log($"[AStar] Partial path, {iterations} iterations, " +
                          $"{result.RawCells.Length} cells");
            }
            else
            {
                result.Status = PathStatus.NotFound;
                Debug.Log($"[AStar] No path found after {iterations} iterations");
            }

            result.IterationsUsed = iterations;
            return result;
        }

        // ───────────── Heuristic ─────────────

        /// <summary>
        /// Octile distance: exact heuristic for 8-directional uniform grid.
        /// Admissible and consistent → A* is optimal.
        /// </summary>
        private float Heuristic(int ax, int az, int bx, int bz)
        {
            int dx = Mathf.Abs(ax - bx);
            int dz = Mathf.Abs(az - bz);
            return StraightCost * (dx + dz)
                 + (DiagonalCost - 2f * StraightCost) * Mathf.Min(dx, dz);
        }

        // ───────────── Utilities ─────────────

        private bool InBounds(int x, int z) =>
            x >= 0 && x < _gridWidth && z >= 0 && z < _gridHeight;

        private Vector2Int[] ReconstructPath(int startIdx, int endIdx)
        {
            var path = new List<Vector2Int>(64);
            int current = endIdx;
            int safety = _gridWidth * _gridHeight; // prevent infinite loop

            while (current != -1 && safety-- > 0)
            {
                int x = current % _gridWidth;
                int z = current / _gridWidth;
                path.Add(new Vector2Int(x, z));

                if (current == startIdx) break;
                current = _parentOf[current];
            }

            path.Reverse();
            return path.ToArray();
        }

        private Vector2Int FindNearestWalkable(bool[] walkable, Vector2Int center, int maxRadius)
        {
            for (int r = 1; r <= maxRadius; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        // Perimeter only — skip inner cells (already checked)
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue;

                        int nx = center.x + dx;
                        int nz = center.y + dz;
                        if (!InBounds(nx, nz)) continue;

                        if (walkable[nz * _gridWidth + nx])
                            return new Vector2Int(nx, nz);
                    }
                }
            }
            return center;
        }

        // ───────────── Debug API ─────────────

        /// <summary>
        /// Returns open/closed sets after last Solve() call.
        /// Use only in editor/debug builds — allocates.
        /// </summary>
        public void GetDebugState(out Vector2Int[] openSet, out Vector2Int[] closedSet)
        {
            var open = new List<Vector2Int>(_openList.Count);
            foreach (int idx in _openList)
                open.Add(new Vector2Int(idx % _gridWidth, idx / _gridWidth));

            var closed = new List<Vector2Int>(256);
            int total = _gridWidth * _gridHeight;
            for (int i = 0; i < total; i++)
            {
                if (_inClosed[i])
                    closed.Add(new Vector2Int(i % _gridWidth, i / _gridWidth));
            }

            openSet = open.ToArray();
            closedSet = closed.ToArray();
        }
    }
}