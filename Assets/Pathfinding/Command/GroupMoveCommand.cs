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

            // Assign destinations: sort units by distance to their slot for optimal assignment
            int[] assignment = AssignUnitsToSlots(units, destinations);

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
        /// Simple greedy assignment: each unit goes to the nearest unoccupied slot.
        /// Prevents units from crossing paths unnecessarily.
        /// </summary>
        private static int[] AssignUnitsToSlots(
            IList<UnitPathFollower> units,
            Vector3[] destinations)
        {
            int count = units.Count;
            var assignment = new int[count];
            var slotTaken = new bool[count];

            // Build distance pairs and sort by distance
            var pairs = new List<(int unitIdx, int slotIdx, float distSq)>(count * count);
            for (int u = 0; u < count; u++)
            {
                if (units[u] == null) continue;
                Vector3 unitPos = units[u].transform.position;
                for (int s = 0; s < count; s++)
                {
                    float distSq = (unitPos - destinations[s]).sqrMagnitude;
                    pairs.Add((u, s, distSq));
                }
            }

            pairs.Sort((a, b) => a.distSq.CompareTo(b.distSq));

            var unitAssigned = new bool[count];

            for (int p = 0; p < pairs.Count; p++)
            {
                var (u, s, _) = pairs[p];
                if (unitAssigned[u] || slotTaken[s]) continue;

                assignment[u] = s;
                unitAssigned[u] = true;
                slotTaken[s] = true;
            }

            // Fallback: any unassigned units get their own index
            for (int i = 0; i < count; i++)
            {
                if (!unitAssigned[i])
                    assignment[i] = i;
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
