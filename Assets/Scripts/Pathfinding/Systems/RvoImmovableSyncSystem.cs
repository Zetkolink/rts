using Pathfinding.Avoidance.ECS;
using Pathfinding.Movement.ECS;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

namespace Pathfinding.Systems
{
    /// <summary>
    /// Keeps <see cref="RvoImmovableTag"/> in sync with <see cref="IsMovingTag"/>.
    /// Moving → immovable OFF. Stopped → immovable ON.
    /// Runs after <see cref="PathFollowSystem"/> so arrival state is already resolved.
    /// Burst-compiled, main thread (trivial cost — one bool flip per entity).
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(PathFollowSystem))]
    public partial struct RvoImmovableSyncSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (isMoving, rvoImmovable) in
                     SystemAPI.Query<EnabledRefRO<IsMovingTag>, EnabledRefRW<RvoImmovableTag>>()
                         .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            {
                rvoImmovable.ValueRW = !isMoving.ValueRO;
            }
        }
    }
}