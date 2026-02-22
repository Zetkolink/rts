using Pathfinding.Movement.ECS;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Pathfinding.Systems.Debug
{
    /// <summary>
    /// DEBUG ONLY. Draws waypoint path and current target for all moving entities.
    /// Uses Debug.DrawLine — visible in both Scene and Game view (enable Gizmos toggle in Game view).
    /// Remove before shipping.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathFollowSystem))]
    public partial struct DebugPathDrawSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (transform, followState, waypoints, velocity) in
                     SystemAPI.Query<
                             RefRO<LocalTransform>,
                             RefRO<PathFollowState>,
                             DynamicBuffer<PathWaypoint>,
                             RefRO<ComputedVelocity>>()
                         .WithAll<IsMovingTag, HasPathTag>())
            {
                int count = waypoints.Length;
                if (count == 0)
                    continue;

                float3 pos = transform.ValueRO.Position;
                int current = followState.ValueRO.CurrentCorner;

                // Full path in yellow.
                for (int i = 0; i < count - 1; i++)
                {
                    UnityEngine.Debug.DrawLine(
                        (Vector3)waypoints[i].Position + Vector3.up * 0.1f,
                        (Vector3)waypoints[i + 1].Position + Vector3.up * 0.1f,
                        Color.yellow);
                }

                // Line from unit to current waypoint in red.
                if (current < count)
                {
                    UnityEngine.Debug.DrawLine(
                        (Vector3)pos + Vector3.up * 0.1f,
                        (Vector3)waypoints[current].Position + Vector3.up * 0.1f,
                        Color.red);
                }

                // Velocity direction in cyan.
                float3 vel = velocity.ValueRO.Value;
                if (math.lengthsq(vel) > 0.01f)
                {
                    UnityEngine.Debug.DrawRay(
                        (Vector3)pos + Vector3.up * 0.5f,
                        (Vector3)math.normalize(vel) * 1.5f,
                        Color.cyan);
                }
            }
        }
    }
}