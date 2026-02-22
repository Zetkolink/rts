using Combat.ECS;
using Unity.Entities;
using Unity.Transforms;

namespace Combat.Systems
{
    /// <summary>
    /// Destroys projectile entities that have <see cref="IsAliveTag"/> disabled.
    /// Runs after collision system to give one frame for any cleanup reads.
    /// Main thread — structural changes.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(DamageApplicationSystem))]
    public partial class ProjectileCleanupSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<ProjectileConfig>();
        }

        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

            foreach (var (_, entity) in
                     SystemAPI.Query<RefRO<ProjectileConfig>>()
                         .WithDisabled<IsAliveTag>()
                         .WithEntityAccess())
            {
                ecb.DestroyEntity(entity);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }
    }
}