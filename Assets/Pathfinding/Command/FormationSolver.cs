using UnityEngine;

namespace RTS.Pathfinding
{
    /// <summary>
    /// Pure C# formation calculator. No MonoBehaviour, no allocations beyond result array.
    /// Computes local-space offsets for N units in a given formation shape.
    /// Offsets are relative to formation center, facing +Z (forward).
    /// Caller rotates offsets to match desired formation heading.
    /// </summary>
    public static class FormationSolver
    {
        /// <summary>
        /// Compute formation offsets for a given unit count and formation type.
        /// Returns array of Vector3 offsets in local space (Y=0).
        /// </summary>
        public static Vector3[] GetOffsets(int count, FormationType type, float spacing)
        {
            if (count <= 0) return System.Array.Empty<Vector3>();
            if (count == 1) return new[] { Vector3.zero };

            switch (type)
            {
                case FormationType.Box:    return BuildBox(count, spacing);
                case FormationType.Line:   return BuildLine(count, spacing);
                case FormationType.Wedge:  return BuildWedge(count, spacing);
                case FormationType.Column: return BuildColumn(count, spacing);
                default:                   return BuildBox(count, spacing);
            }
        }

        /// <summary>
        /// Determine the best formation type for a given group and passage width.
        /// If the passage is too narrow for a box, auto-switch to column.
        /// </summary>
        public static FormationType AutoSelectFormation(
            int unitCount,
            float spacing,
            float passageWidthCells,
            float cellSize)
        {
            float passageWidth = passageWidthCells * cellSize;

            // How many columns fit in the passage?
            int columnsFit = Mathf.Max(1, Mathf.FloorToInt(passageWidth / spacing));

            if (columnsFit >= 3 && unitCount >= 4)
                return FormationType.Box;

            if (columnsFit >= 2)
                return FormationType.Column;

            return FormationType.Column;
        }

        // ───────────── Formation Builders ─────────────

        /// <summary>
        /// Box/grid formation. Units arranged in rows, centered on origin.
        /// Front row at Z=0, subsequent rows behind (-Z).
        /// </summary>
        private static Vector3[] BuildBox(int count, float spacing)
        {
            int columns = Mathf.CeilToInt(Mathf.Sqrt(count));
            int rows = Mathf.CeilToInt((float)count / columns);

            var offsets = new Vector3[count];
            float halfWidth = (columns - 1) * spacing * 0.5f;
            float halfDepth = (rows - 1) * spacing * 0.5f;

            for (int i = 0; i < count; i++)
            {
                int col = i % columns;
                int row = i / columns;

                offsets[i] = new Vector3(
                    col * spacing - halfWidth,
                    0f,
                    -(row * spacing - halfDepth)
                );
            }

            return offsets;
        }

        /// <summary>
        /// Line formation. Single row, centered on origin.
        /// </summary>
        private static Vector3[] BuildLine(int count, float spacing)
        {
            var offsets = new Vector3[count];
            float halfWidth = (count - 1) * spacing * 0.5f;

            for (int i = 0; i < count; i++)
            {
                offsets[i] = new Vector3(
                    i * spacing - halfWidth,
                    0f,
                    0f
                );
            }

            return offsets;
        }

        /// <summary>
        /// Wedge/V formation. Point at front (Z=0), wings spread behind.
        /// </summary>
        private static Vector3[] BuildWedge(int count, float spacing)
        {
            var offsets = new Vector3[count];

            // First unit is the point
            offsets[0] = Vector3.zero;

            // Alternating left-right, each row one step back
            for (int i = 1; i < count; i++)
            {
                int pair = (i + 1) / 2; // 1,1,2,2,3,3,...
                bool isRight = (i % 2 == 1);

                float x = pair * spacing * (isRight ? 1f : -1f);
                float z = -pair * spacing;

                offsets[i] = new Vector3(x, 0f, z);
            }

            return offsets;
        }

        /// <summary>
        /// Column formation. Single file, centered on origin.
        /// Front at Z=0, subsequent units behind (-Z).
        /// Ideal for narrow passages.
        /// </summary>
        private static Vector3[] BuildColumn(int count, float spacing)
        {
            var offsets = new Vector3[count];
            float halfDepth = (count - 1) * spacing * 0.5f;

            for (int i = 0; i < count; i++)
            {
                offsets[i] = new Vector3(
                    0f,
                    0f,
                    -(i * spacing - halfDepth)
                );
            }

            return offsets;
        }

        /// <summary>
        /// Validate formation positions against the nav grid.
        /// Returns the number of positions that are walkable.
        /// </summary>
        public static int CountWalkablePositions(
            Vector3[] worldPositions,
            GridManager grid,
            ClearanceClass clearance)
        {
            int walkable = 0;
            for (int i = 0; i < worldPositions.Length; i++)
            {
                var cell = grid.WorldToGrid(worldPositions[i]);
                if (grid.IsNavWalkable(cell, clearance))
                    walkable++;
            }
            return walkable;
        }
    }
}
