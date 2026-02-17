using UnityEngine;

namespace RTS.Pathfinding
{
    /// <summary>
    /// Resolves arrival positions when a unit's destination is occupied.
    /// Searches ring of positions around the target, finds nearest free spot.
    /// Used as fallback when formation slots are occupied or for single-unit commands.
    /// </summary>
    public static class ArrivalManager
    {
        private const int RingPositions = 8;
        private const int MaxRings = 3;

        /// <summary>
        /// Find a free arrival position near the desired destination.
        /// Checks ring positions around the target at increasing radii.
        /// Returns the destination itself if it's free, or the nearest free alternative.
        /// </summary>
        public static Vector3 FindFreeArrivalPosition(
            Vector3 destination,
            float unitRadius,
            GridManager grid,
            ClearanceClass clearance,
            UnitPathFollower excludeUnit = null)
        {
            // Check if destination itself is free
            if (IsPositionFree(destination, unitRadius, grid, clearance, excludeUnit))
                return destination;

            // Search expanding rings
            float ringRadius = unitRadius * 2.5f;

            for (int ring = 0; ring < MaxRings; ring++)
            {
                float currentRadius = ringRadius * (ring + 1);
                float bestDistSq = float.MaxValue;
                Vector3 bestPos = destination;
                bool found = false;

                for (int i = 0; i < RingPositions; i++)
                {
                    float angle = (2f * Mathf.PI / RingPositions) * i;
                    Vector3 candidate = destination + new Vector3(
                        Mathf.Cos(angle) * currentRadius,
                        0f,
                        Mathf.Sin(angle) * currentRadius
                    );

                    if (!IsPositionFree(candidate, unitRadius, grid, clearance, excludeUnit))
                        continue;

                    float distSq = (candidate - destination).sqrMagnitude;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestPos = candidate;
                        found = true;
                    }
                }

                if (found)
                    return bestPos;
            }

            // All rings full — return original destination and let RVO handle it
            return destination;
        }

        /// <summary>
        /// Check if a position is free: nav-walkable AND no other unit occupying it.
        /// </summary>
        private static bool IsPositionFree(
            Vector3 position,
            float unitRadius,
            GridManager grid,
            ClearanceClass clearance,
            UnitPathFollower excludeUnit)
        {
            var cell = grid.WorldToGrid(position);
            if (!grid.IsNavWalkable(cell, clearance))
                return false;

            // Check for other units via OverlapSphere (uses physics layer)
            float checkRadius = unitRadius * 0.9f;
            var colliders = Physics.OverlapSphere(position, checkRadius);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (excludeUnit != null && colliders[i].gameObject == excludeUnit.gameObject)
                    continue;

                // Any collider with a UnitPathFollower means it's occupied by a unit
                if (colliders[i].GetComponentInParent<UnitPathFollower>() != null)
                    return false;
            }

            return true;
        }
    }
}
