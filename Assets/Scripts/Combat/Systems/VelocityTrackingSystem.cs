using Combat.ECS;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Combat.Systems
{
    /// <summary>
    /// Updates <see cref="TrackedVelocity"/> each frame from position delta.
    /// Shooters read this from their target to compute lead prediction.
    /// Burst-compiled, ScheduleParallel.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateBefore(typeof(AttackSystem))]
    public partial struct VelocityTrackingSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var job = new TrackVelocityJob { DeltaTime = dt };
            job.ScheduleParallel();
        }

        [BurstCompile]
        [WithDisabled(typeof(IsDeadTag))]
        private partial struct TrackVelocityJob : IJobEntity
        {
            public float DeltaTime;

            public void Execute(ref TrackedVelocity tracked, in LocalTransform transform)
            {
                if (DeltaTime > 0f)
                    tracked.Value = (transform.Position - tracked.PreviousPosition) / DeltaTime;
                else
                    tracked.Value = float3.zero;

                tracked.PreviousPosition = transform.Position;
            }
        }
    }
}