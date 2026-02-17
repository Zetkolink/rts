using System.Collections.Generic;
using UnityEngine;

namespace RTS.Pathfinding
{
    /// <summary>
    /// Line-of-sight path smoothing using Bresenham ray on grid.
    /// Removes redundant waypoints where direct traversal is possible.
    /// Static, stateless, zero allocations beyond result array.
    /// </summary>
    public static class PathSmoother
    {
        /// <summary>
        /// Takes raw grid-space path from A*, returns reduced world-space waypoints.
        /// </summary>
        public static Vector3[] Smooth(
            Vector2Int[] rawPath,
            bool[] walkable,
            int gridWidth,
            GridManager grid)
        {
            if (rawPath == null || rawPath.Length == 0)
                return System.Array.Empty<Vector3>();

            if (rawPath.Length <= 2)
            {
                var direct = new Vector3[rawPath.Length];
                for (int i = 0; i < rawPath.Length; i++)
                    direct[i] = grid.GridToWorld(rawPath[i]);
                return direct;
            }

            var smoothed = new List<Vector3>(16);
            smoothed.Add(grid.GridToWorld(rawPath[0]));

            int current = 0;

            while (current < rawPath.Length - 1)
            {
                int farthestVisible = current + 1;

                // Scan forward: find farthest cell reachable in straight line
                for (int test = current + 2; test < rawPath.Length; test++)
                {
                    if (GridLineOfSight(
                        rawPath[current].x, rawPath[current].y,
                        rawPath[test].x, rawPath[test].y,
                        walkable, gridWidth))
                    {
                        farthestVisible = test;
                    }
                }

                smoothed.Add(grid.GridToWorld(rawPath[farthestVisible]));
                current = farthestVisible;
            }

            return smoothed.ToArray();
        }

        /// <summary>
        /// Bresenham line walk. Returns true if ALL cells on the line are walkable.
        /// Pure integer arithmetic, no allocations.
        /// </summary>
        private static bool GridLineOfSight(
            int x0, int z0, int x1, int z1,
            bool[] walkable, int gridWidth)
        {
            int dx = Mathf.Abs(x1 - x0);
            int dz = Mathf.Abs(z1 - z0);
            int sx = x0 < x1 ? 1 : -1;
            int sz = z0 < z1 ? 1 : -1;
            int err = dx - dz;

            while (true)
            {
                if (!walkable[z0 * gridWidth + x0])
                    return false;

                if (x0 == x1 && z0 == z1)
                    return true;

                int e2 = 2 * err;
                if (e2 > -dz) { err -= dz; x0 += sx; }
                if (e2 < dx) { err += dx; z0 += sz; }
            }
        }
    }
}