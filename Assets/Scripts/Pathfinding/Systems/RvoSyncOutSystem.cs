using Pathfinding.Avoidance.ECS;
using Pathfinding.Avoidance.RVO;
using Pathfinding.Movement.ECS;
using Unity.Entities;
using Unity.Transforms;
using RvoVector2 = Pathfinding.Avoidance.RVO.Vector2;

namespace Pathfinding.Systems
{
    /// <summary>
    /// Pushes ECS data into RVO2 <see cref="Simulator"/> each frame:
    /// position from <see cref="LocalTransform"/>,
    /// preferred velocity from <see cref="DesiredVelocity"/>,
    /// maxSpeed = 0 when <see cref="RvoImmovableTag"/> is enabled.
    /// Main thread — Simulator API is managed.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateBefore(typeof(RvoSimulatorSystem))]
    [UpdateAfter(typeof(PathFollowSystem))]
    public partial class RvoSyncOutSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<RvoAgentRef>();
        }

        protected override void OnUpdate()
        {
            var sim = Simulator.Instance;

            foreach (var (transform, desiredVel, agentRef, avoidance, immovable) in
                SystemAPI.Query<
                    RefRO<LocalTransform>,
                    RefRO<DesiredVelocity>,
                    RefRO<RvoAgentRef>,
                    RefRO<AvoidanceConfig>,
                    EnabledRefRO<RvoImmovableTag>>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            {
                int id = agentRef.ValueRO.AgentId;
                if (id < 0)
                    continue;

                // Sync position: Unity XZ → RVO XY.
                var pos = transform.ValueRO.Position;
                sim.setAgentPosition(id, new RvoVector2(pos.x, pos.z));

                // Sync preferred velocity.
                var vel = desiredVel.ValueRO.Value;
                sim.setAgentPrefVelocity(id, new RvoVector2(vel.x, vel.z));

                // Immovable: maxSpeed = 0 so RVO treats unit as static.
                float speed = immovable.ValueRO ? 0f : avoidance.ValueRO.MaxSpeed;
                sim.setAgentMaxSpeed(id, speed);
            }
        }
    }
}