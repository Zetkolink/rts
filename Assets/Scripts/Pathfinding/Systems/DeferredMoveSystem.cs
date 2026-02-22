using Pathfinding.Movement.ECS;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

namespace Pathfinding.Systems
{
    /// <summary>
    /// Ticks <see cref="DeferredMove.FramesLeft"/> each frame.
    /// When it reaches zero, copies Destination into <see cref="MoveTarget"/>
    /// and enables <see cref="NeedsPathTag"/> to trigger path calculation.
    /// This gives NavMeshObstacle time to disable carving before pathfinding runs.
    /// Burst-compiled, ScheduleParallel.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateBefore(typeof(PathCalculationSystem))]
    public partial struct DeferredMoveSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new DeferredMoveJob().ScheduleParallel();
        }

        [BurstCompile]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        private partial struct DeferredMoveJob : IJobEntity
        {
            public void Execute(
                ref DeferredMove deferred,
                ref MoveTarget moveTarget,
                ref RepathTimer repathTimer,
                EnabledRefRW<NeedsPathTag> needsPath)
            {
                if (deferred.FramesLeft <= 0)
                    return;

                deferred.FramesLeft--;

                if (deferred.FramesLeft > 0)
                    return;

                // Timer expired — issue the real move command.
                moveTarget.Destination = deferred.Destination;
                repathTimer.Timer = 0f;
                needsPath.ValueRW = true;
            }
        }
    }
}