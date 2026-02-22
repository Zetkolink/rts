using Pathfinding.Movement.ECS;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Pathfinding.Systems
{
    /// <summary>
    /// Applies <see cref="ComputedVelocity"/> to <see cref="LocalTransform.Position"/> each frame.
    /// Only runs on entities that are currently moving.
    /// Burst-compiled, ScheduleParallel.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct MovementApplySystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new MovementApplyJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            };
            job.ScheduleParallel();
        }

        [BurstCompile]
        [WithAll(typeof(IsMovingTag))]
        private partial struct MovementApplyJob : IJobEntity
        {
            public float DeltaTime;

            public void Execute(ref LocalTransform transform, in ComputedVelocity velocity)
            {
                transform.Position += velocity.Value * DeltaTime;
            }
        }
    }
}