using UnityEngine;

namespace RTS.Pathfinding
{
    /// <summary>
    /// Per-unit-type configuration. Defines physical size, movement parameters,
    /// and clearance class for multi-clearance pathfinding.
    /// Assign one to each unit prefab via UnitPathFollower.
    /// </summary>
    [CreateAssetMenu(fileName = "UnitProfile", menuName = "RTS/Pathfinding/Unit Profile")]
    public sealed class UnitProfile : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Display name for debugging and UI")]
        public string displayName = "Unit";

        [Header("Size")]
        [Tooltip("Physical radius in world units. Used for RVO avoidance and overlap checks.")]
        [Min(0.1f)] public float radius = 0.5f;

        [Tooltip("Clearance class determines which nav grid is used for pathfinding.")]
        public ClearanceClass clearanceClass = ClearanceClass.Small;

        [Header("Movement")]
        [Tooltip("Base movement speed in world units per second.")]
        [Min(0.1f)] public float moveSpeed = 5f;

        [Tooltip("Rotation speed in degrees per second.")]
        [Min(1f)] public float rotationSpeed = 720f;

        [Header("Formation")]
        [Tooltip("Minimum distance between units of this type in formation.")]
        [Min(0.5f)] public float formationSpacing = 2f;

        [Header("Idle Yield")]
        [Tooltip("Whether this unit can push idle units out of the way.")]
        public bool canPushIdle = true;

        [Tooltip("Push priority. Higher value = can push units with lower priority. Tanks > Infantry.")]
        public int pushPriority = 1;
    }
}
