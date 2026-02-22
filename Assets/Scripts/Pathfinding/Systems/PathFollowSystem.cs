using Pathfinding.Movement.ECS;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Pathfinding.Systems
{
    /// <summary>
    /// Reads <see cref="PathWaypoint"/> buffer, steers toward the current corner,
    /// advances corners on arrival, writes <see cref="DesiredVelocity"/>.
    /// When the final waypoint is reached — clears movement state (arrival).
    /// <see cref="ComputedVelocity"/> is written by RVO sync systems, not here.
    /// Burst-compiled, ScheduleParallel.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateBefore(typeof(MovementApplySystem))]
    public partial struct PathFollowSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new PathFollowJob().ScheduleParallel();
        }

        [BurstCompile]
        [WithAll(typeof(HasPathTag), typeof(IsMovingTag))]
        private partial struct PathFollowJob : IJobEntity
        {
            public void Execute(
                ref PathFollowState followState,
                ref DesiredVelocity desiredVelocity,
                ref ComputedVelocity computedVelocity,
                ref NormalizedSpeed normalizedSpeed,
                EnabledRefRW<IsMovingTag> isMoving,
                EnabledRefRW<HasPathTag> hasPath,
                in LocalTransform transform,
                in MoveSpeed moveSpeed,
                in PathConfig pathConfig,
                in DynamicBuffer<PathWaypoint> waypoints)
            {
                int cornerCount = waypoints.Length;
                if (cornerCount == 0 || followState.CurrentCorner >= cornerCount)
                {
                    Arrive(ref desiredVelocity, ref computedVelocity, ref normalizedSpeed,
                        isMoving, hasPath);
                    return;
                }

                // Direction to current waypoint (XZ plane).
                float3 currentPos = transform.Position;
                float3 waypointPos = waypoints[followState.CurrentCorner].Position;
                float3 toTarget = waypointPos - currentPos;
                toTarget.y = 0f;

                bool isFinal = followState.CurrentCorner >= cornerCount - 1;
                float tolerance = isFinal ? pathConfig.ArrivalTolerance : pathConfig.WaypointTolerance;

                // Waypoint reached?
                if (math.lengthsq(toTarget) <= tolerance * tolerance)
                {
                    if (isFinal)
                    {
                        Arrive(ref desiredVelocity, ref computedVelocity, ref normalizedSpeed,
                            isMoving, hasPath);
                        return;
                    }

                    // Advance to next corner and recalculate direction.
                    followState.CurrentCorner++;
                    waypointPos = waypoints[followState.CurrentCorner].Position;
                    toTarget = waypointPos - currentPos;
                    toTarget.y = 0f;
                }

                // Steer toward waypoint — RVO will produce ComputedVelocity from this.
                float dist = math.length(toTarget);
                desiredVelocity.Value = dist > 0.001f
                    ? (toTarget / dist) * moveSpeed.Speed
                    : float3.zero;
            }

            private static void Arrive(
                ref DesiredVelocity desiredVelocity,
                ref ComputedVelocity computedVelocity,
                ref NormalizedSpeed normalizedSpeed,
                EnabledRefRW<IsMovingTag> isMoving,
                EnabledRefRW<HasPathTag> hasPath)
            {
                desiredVelocity.Value = float3.zero;
                computedVelocity.Value = float3.zero;
                normalizedSpeed.Value = 0f;
                isMoving.ValueRW = false;
                hasPath.ValueRW = false;
            }
        }
    }
}