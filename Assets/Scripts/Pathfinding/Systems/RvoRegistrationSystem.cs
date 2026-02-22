using Pathfinding.Avoidance.ECS;
using Pathfinding.Avoidance.RVO;
using Unity.Entities;
using Unity.Transforms;
using RvoVector2 = Pathfinding.Avoidance.RVO.Vector2;

namespace Pathfinding.Systems
{
    /// <summary>
    /// Registers entities that have <see cref="AvoidanceConfig"/> but an unregistered
    /// <see cref="RvoAgentRef"/> (AgentId == -1) with the RVO2 <see cref="Simulator"/>.
    /// Runs once per unregistered entity, then the AgentId is valid for the entity's lifetime.
    /// Main thread — Simulator.addAgent is managed.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateBefore(typeof(RvoSyncOutSystem))]
    public partial class RvoRegistrationSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<AvoidanceConfig>();
        }

        protected override void OnUpdate()
        {
            var sim = Simulator.Instance;

            foreach (var (avoidance, transform, agentRef) in
                     SystemAPI.Query<
                         RefRO<AvoidanceConfig>,
                         RefRO<LocalTransform>,
                         RefRW<RvoAgentRef>>())
            {
                if (agentRef.ValueRO.IsRegistered)
                    continue;

                var config = avoidance.ValueRO;
                var pos = transform.ValueRO.Position;

                int id = sim.addAgent(
                    new RvoVector2(pos.x, pos.z),
                    config.NeighborDist,
                    config.MaxNeighbors,
                    config.TimeHorizon,
                    config.TimeHorizonObst,
                    config.Radius,
                    config.MaxSpeed,
                    new RvoVector2(0f, 0f));

                agentRef.ValueRW = new RvoAgentRef { AgentId = id };
            }
        }
    }
}