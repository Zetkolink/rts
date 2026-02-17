using System.Collections.Generic;
using UnityEngine;

namespace RTS.Pathfinding
{
    /// <summary>
    /// Orchestrates group movement with formation support.
    /// Computes per-unit destinations from a single click point,
    /// validates positions, and issues individual MoveTo commands.
    ///
    /// Usage: GroupMoveCommand.Execute(selectedUnits, clickPosition);
    /// </summary>
    public static class GroupMoveCommand
    {
        private const float DefaultSpacing = 2f;
        private const FormationType DefaultFormation = FormationType.Box;

        /// <summary>
        /// Execute a group move command with default Box formation.
        /// Each unit receives its own destination offset from the formation center.
        /// </summary>
        public static void Execute(
            IList<UnitPathFollower> units,
            Vector3 destination,
            FormationType formation = DefaultFormation)
        {
            if (units == null || units.Count == 0) return;

            // Single unit — direct move, no formation needed
            if (units.Count == 1)
            {
                var unit = units[0];
                if (unit == null) return;

                var profile = unit.Profile;
                ClearanceClass clearance = profile != null
                    ? profile.clearanceClass
                    : ClearanceClass.Small;

                unit.MoveTo(destination, clearance);
                return;
            }

            // Compute group center and heading
            Vector3 center = ComputeGroupCenter(units);
            Vector3 toTarget = destination - center;
            toTarget.y = 0f;

            Quaternion rotation;
            if (toTarget.sqrMagnitude > 0.01f)
                rotation = Quaternion.LookRotation(toTarget.normalized);
            else
                rotation = Quaternion.identity;

            // Determine spacing from units (use max formationSpacing in group)
            float spacing = GetGroupSpacing(units);

            // Determine max clearance class in group (for formation validation path)
            ClearanceClass maxClearance = GetMaxClearance(units);

            // Generate formation offsets
            Vector3[] offsets = FormationSolver.GetOffsets(units.Count, formation, spacing);

            // Compute world-space formation destinations
            var destinations = new Vector3[units.Count];
            for (int i = 0; i < units.Count; i++)
                destinations[i] = destination + rotation * offsets[i];

            // Validate: check how many positions are walkable
            GridManager grid = PathfindingAPI.Instance != null
                ? PathfindingAPI.Instance.Grid
                : null;

            if (grid != null)
            {
                int walkable = FormationSolver.CountWalkablePositions(
                    destinations, grid, maxClearance);

                float walkableRatio = (float)walkable / units.Count;

                // If less than 60% walkable, try Column formation
                if (walkableRatio < 0.6f && formation != FormationType.Column)
                {
                    offsets = FormationSolver.GetOffsets(units.Count, FormationType.Column, spacing);
                    for (int i = 0; i < units.Count; i++)
                        destinations[i] = destination + rotation * offsets[i];

                    int columnWalkable = FormationSolver.CountWalkablePositions(
                        destinations, grid, maxClearance);

                    // If column is worse, revert
                    if (columnWalkable < walkable)
                    {
                        offsets = FormationSolver.GetOffsets(units.Count, formation, spacing);
                        for (int i = 0; i < units.Count; i++)
                            destinations[i] = destination + rotation * offsets[i];
                    }
                }
            }

            // Assign units to slots: front units → front slots, left → left
            Vector3 movDir = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : Vector3.forward;
            int[] assignment = AssignUnitsToFormationSlots(units, offsets, movDir);

            // Issue individual move commands
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit == null) continue;

                int slot = assignment[i];
                Vector3 personalDest = destinations[slot];

                var profile = unit.Profile;
                ClearanceClass clearance = profile != null
                    ? profile.clearanceClass
                    : ClearanceClass.Small;

                unit.MoveTo(personalDest, clearance);
            }
        }

        /// <summary>
        /// Assign units to formation slots using row-column projection matching.
        /// Front units (closest to destination along movement axis) receive front
        /// formation slots; within each row, leftmost units receive leftmost slots.
        /// Eliminates path crossing on both forward and lateral axes.
        /// O(N log N) complexity.
        /// </summary>
        private static int[] AssignUnitsToFormationSlots(
            IList<UnitPathFollower> units,
            Vector3[] offsets,
            Vector3 movementDir)
        {
            int count = units.Count;
            var assignment = new int[count];

            // Compute lateral axis (perpendicular to movement in XZ plane)
            Vector3 perpDir = Vector3.Cross(Vector3.up, movementDir);
            if (perpDir.sqrMagnitude < 0.001f)
            {
                movementDir = Vector3.forward;
                perpDir = Vector3.right;
            }

            // ── Group formation slots into rows by local Z ──
            // Sort slot indices by Z descending (front first)
            var slotOrder = new int[count];
            for (int i = 0; i < count; i++) slotOrder[i] = i;
            System.Array.Sort(slotOrder, (a, b) => offsets[b].z.CompareTo(offsets[a].z));

            // Walk sorted slots and split into rows at Z discontinuities
            var rows = new List<List<int>>(8);
            var currentRow = new List<int>(8) { slotOrder[0] };

            for (int i = 1; i < count; i++)
            {
                float zPrev = offsets[slotOrder[i - 1]].z;
                float zCurr = offsets[slotOrder[i]].z;

                if (zPrev - zCurr > 0.01f)
                {
                    rows.Add(currentRow);
                    currentRow = new List<int>(8);
                }
                currentRow.Add(slotOrder[i]);
            }
            rows.Add(currentRow);

            // ── Rank units by forward projection (frontmost first) ──
            var unitIndices = new int[count];
            var forwardProj = new float[count];
            var lateralProj = new float[count];

            for (int i = 0; i < count; i++)
            {
                unitIndices[i] = i;
                if (units[i] != null)
                {
                    Vector3 pos = units[i].transform.position;
                    forwardProj[i] = Vector3.Dot(pos, movementDir);
                    lateralProj[i] = Vector3.Dot(pos, perpDir);
                }
                else
                {
                    forwardProj[i] = float.MinValue;
                    lateralProj[i] = 0f;
                }
            }

            System.Array.Sort(unitIndices, (a, b) => forwardProj[b].CompareTo(forwardProj[a]));

            // ── Assign row by row ──
            int cursor = 0;
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                int rowSize = row.Count;

                // Sort slots in this row by local X ascending (leftmost first)
                row.Sort((a, b) => offsets[a].x.CompareTo(offsets[b].x));

                // Extract batch of unit indices for this row, sort by lateral projection
                var batch = new int[rowSize];
                for (int j = 0; j < rowSize; j++)
                    batch[j] = unitIndices[cursor + j];

                System.Array.Sort(batch, (a, b) => lateralProj[a].CompareTo(lateralProj[b]));

                // Zip: leftmost unit → leftmost slot
                for (int j = 0; j < rowSize; j++)
                    assignment[batch[j]] = row[j];

                cursor += rowSize;
            }

            return assignment;
        }

        private static Vector3 ComputeGroupCenter(IList<UnitPathFollower> units)
        {
            Vector3 sum = Vector3.zero;
            int validCount = 0;

            for (int i = 0; i < units.Count; i++)
            {
                if (units[i] == null) continue;
                sum += units[i].transform.position;
                validCount++;
            }

            return validCount > 0 ? sum / validCount : Vector3.zero;
        }

        private static float GetGroupSpacing(IList<UnitPathFollower> units)
        {
            float maxSpacing = DefaultSpacing;

            for (int i = 0; i < units.Count; i++)
            {
                if (units[i] == null) continue;
                var profile = units[i].Profile;
                if (profile != null && profile.formationSpacing > maxSpacing)
                    maxSpacing = profile.formationSpacing;
            }

            return maxSpacing;
        }

        private static ClearanceClass GetMaxClearance(IList<UnitPathFollower> units)
        {
            ClearanceClass max = ClearanceClass.Small;

            for (int i = 0; i < units.Count; i++)
            {
                if (units[i] == null) continue;
                var profile = units[i].Profile;
                if (profile != null && profile.clearanceClass > max)
                    max = profile.clearanceClass;
            }

            return max;
        }
    }
}
