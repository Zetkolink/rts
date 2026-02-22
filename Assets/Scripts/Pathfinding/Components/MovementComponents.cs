using Unity.Entities;
using Unity.Mathematics;

namespace Pathfinding.Movement.ECS
{
    // ════════════════════════════════════════════
    //  CONFIG — baked once, read-only at runtime
    // ════════════════════════════════════════════

    /// <summary>
    /// Base movement parameters. Baked from authoring.
    /// </summary>
    public struct MoveSpeed : IComponentData
    {
        public float Speed;
        public float RotationSpeed;
    }

    /// <summary>
    /// NavMesh path-following tuning. Baked from authoring.
    /// </summary>
    public struct PathConfig : IComponentData
    {
        /// <summary>Distance at which intermediate waypoints are considered reached.</summary>
        public float WaypointTolerance;

        /// <summary>Distance at which the final waypoint triggers arrival.</summary>
        public float ArrivalTolerance;

        /// <summary>Seconds between automatic path recalculations.</summary>
        public float RepathInterval;

        /// <summary>NavMesh.SamplePosition search radius.</summary>
        public float SampleDistance;

        /// <summary>NavMesh area mask for pathfinding.</summary>
        public int AreaMask;
    }

    // ════════════════════════════════════════════
    //  STATE — written/read by systems each frame
    // ════════════════════════════════════════════

    /// <summary>
    /// World-space destination the unit is trying to reach.
    /// Presence + <see cref="NeedsPathTag"/> enabled = path calculation requested.
    /// </summary>
    public struct MoveTarget : IComponentData
    {
        public float3 Destination;
    }

    /// <summary>
    /// Delayed move order. DeferredMoveSystem ticks FramesLeft;
    /// when zero, copies Destination into <see cref="MoveTarget"/> and enables <see cref="NeedsPathTag"/>.
    /// </summary>
    public struct DeferredMove : IComponentData
    {
        public float3 Destination;
        public int FramesLeft;
    }

    /// <summary>
    /// Counts down to the next automatic repath.
    /// RepathSystem resets to <see cref="PathConfig.RepathInterval"/> and enables <see cref="NeedsPathTag"/>.
    /// </summary>
    public struct RepathTimer : IComponentData
    {
        public float Timer;
    }

    /// <summary>
    /// Tracks which waypoint in the <see cref="PathWaypoint"/> buffer the unit is heading toward.
    /// </summary>
    public struct PathFollowState : IComponentData
    {
        public int CurrentCorner;
    }

    /// <summary>
    /// Direction × speed the unit wants to go before avoidance.
    /// Written by PathFollowSystem, read by RvoSyncOutSystem.
    /// </summary>
    public struct DesiredVelocity : IComponentData
    {
        public float3 Value;
    }

    /// <summary>
    /// Collision-free velocity after RVO avoidance (or direct copy if no RVO).
    /// Written by RvoSyncInSystem, read by MovementApplySystem / RotationApplySystem.
    /// </summary>
    public struct ComputedVelocity : IComponentData
    {
        public float3 Value;
    }

    /// <summary>
    /// |ComputedVelocity| / MoveSpeed.Speed. Used by animation layer.
    /// </summary>
    public struct NormalizedSpeed : IComponentData
    {
        public float Value;
    }

    // ════════════════════════════════════════════
    //  TAGS — IEnableableComponent, no data
    // ════════════════════════════════════════════

    /// <summary>Unit is currently in motion.</summary>
    public struct IsMovingTag : IComponentData, IEnableableComponent { }

    /// <summary>Path needs (re)calculation. Consumed by PathCalculationSystem.</summary>
    public struct NeedsPathTag : IComponentData, IEnableableComponent { }

    /// <summary>Unit has a valid path in its <see cref="PathWaypoint"/> buffer.</summary>
    public struct HasPathTag : IComponentData, IEnableableComponent { }

    // ════════════════════════════════════════════
    //  BUFFER — variable-length waypoint list
    // ════════════════════════════════════════════

    /// <summary>
    /// Single waypoint in the NavMesh path.
    /// 16 elements inline covers most paths without heap allocation.
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct PathWaypoint : IBufferElementData
    {
        public float3 Position;
    }
}