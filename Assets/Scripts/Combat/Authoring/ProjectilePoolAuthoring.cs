using Combat.Systems;
using Unity.Entities;
using UnityEngine;

namespace Combat.Authoring
{
    /// <summary>
    /// Place on a singleton GameObject in the subscene.
    /// Configures pool size. Prefab is discovered at runtime via
    /// <see cref="ProjectilePrefabTag"/> — no GO reference needed.
    /// </summary>
    public class ProjectilePoolAuthoring : MonoBehaviour
    {
        [Tooltip("Pre-allocated projectile count. Set to expected peak: " +
                 "units × fire rate × avg flight time. 2048 covers most RTS scenarios.")]
        public int InitialCapacity = 2048;

        [Tooltip("Batch size when pool runs dry. Rarely needed if InitialCapacity is correct.")]
        public int GrowBatchSize = 128;

        private class Baker : Baker<ProjectilePoolAuthoring>
        {
            public override void Bake(ProjectilePoolAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new ProjectilePoolConfig
                {
                    InitialCapacity = authoring.InitialCapacity,
                    GrowBatchSize = authoring.GrowBatchSize,
                });
            }
        }
    }
}