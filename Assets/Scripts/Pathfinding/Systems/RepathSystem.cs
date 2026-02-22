using Pathfinding.Movement.ECS;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

namespace Pathfinding.Systems
{
    /// <summary>
    /// Ticks <see cref="RepathTimer"/> on moving entities.
    /// When the timer expires, enables <see cref="NeedsPathTag"/> so
    /// <see cref="PathCalculationSystem"/> recalculates the path.
    /// Burst-compiled, ScheduleParallel.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateBefore(typeof(PathCalculationSystem))]
    public partial struct RepathSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            new RepathJob { DeltaTime = dt }.ScheduleParallel();
        }

        [BurstCompile]
        [WithAll(typeof(IsMovingTag), typeof(HasPathTag))]
        [WithDisabled(typeof(NeedsPathTag))]
        private partial struct RepathJob : IJobEntity
        {
            public float DeltaTime;

            public void Execute(
                ref RepathTimer timer,
                in PathConfig pathConfig,
                EnabledRefRW<NeedsPathTag> needsPath)
            {
                timer.Timer += DeltaTime;

                if (timer.Timer < pathConfig.RepathInterval)
                    return;

                timer.Timer = 0f;
                needsPath.ValueRW = true;
            }
        }
    }
}