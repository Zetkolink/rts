using Pathfinding.Avoidance.ECS;
using Pathfinding.Avoidance.RVO;
using Pathfinding.Movement.ECS;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Pathfinding.Systems
{
    /// <summary>
    /// Reads collision-free velocity from RVO2 <see cref="Simulator"/> into
    /// <see cref="ComputedVelocity"/>. This replaces the direct passthrough
    /// that was in PathFollowSystem during Steps 2-4.
    /// Main thread — Simulator API is managed.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(RvoSimulatorSystem))]
    [UpdateBefore(typeof(MovementApplySystem))]
    public partial class RvoSyncInSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<RvoAgentRef>();
        }

        protected override void OnUpdate()
        {
            var sim = Simulator.Instance;

            foreach (var (computedVel, agentRef) in
                     SystemAPI.Query<
                         RefRW<ComputedVelocity>,
                         RefRO<RvoAgentRef>>())
            {
                int id = agentRef.ValueRO.AgentId;
                if (id < 0)
                    continue;

                // RVO XY → Unity XZ.
                var rvoVel = sim.getAgentVelocity(id);
                computedVel.ValueRW.Value = new float3(rvoVel.x(), 0f, rvoVel.y());
            }
        }
    }
}