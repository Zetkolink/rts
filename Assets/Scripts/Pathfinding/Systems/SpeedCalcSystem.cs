using Pathfinding.Movement.ECS;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Pathfinding.Systems
{
    /// <summary>
    /// Computes <see cref="NormalizedSpeed"/> = |<see cref="ComputedVelocity"/>| / <see cref="MoveSpeed.Speed"/>.
    /// Runs on all entities with these components (moving or not) so that
    /// NormalizedSpeed correctly decays to 0 when the unit stops.
    /// Burst-compiled, ScheduleParallel.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(MovementApplySystem))]
    public partial struct SpeedCalcSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new SpeedCalcJob().ScheduleParallel();
        }

        [BurstCompile]
        private partial struct SpeedCalcJob : IJobEntity
        {
            public void Execute(ref NormalizedSpeed normalizedSpeed, in ComputedVelocity velocity,
                in MoveSpeed moveSpeed)
            {
                normalizedSpeed.Value = moveSpeed.Speed > 0f
                    ? math.length(velocity.Value) / moveSpeed.Speed
                    : 0f;
            }
        }
    }
}