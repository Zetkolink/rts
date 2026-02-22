using Combat.ECS;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace Combat.Systems
{
    /// <summary>
    /// Singleton config for the projectile pool. Place on a singleton entity in subscene.
    /// </summary>
    public struct ProjectilePoolConfig : IComponentData
    {
        /// <summary>How many projectiles to pre-allocate.</summary>
        public int InitialCapacity;

        /// <summary>How many to spawn per frame when pool runs dry (avoids spike).</summary>
        public int GrowBatchSize;
    }

    /// <summary>
    /// Manages a pre-allocated pool of projectile entities.
    ///
    /// Creates the projectile prefab archetype in code — no scene GameObject needed.
    /// Pool entries are real entities with all projectile components,
    /// but with IsAliveTag disabled. Acquiring = enable + set components.
    /// Returning = disable IsAliveTag (done by collision/lifetime systems).
    ///
    /// This replaces ECB.Instantiate per shot — zero structural changes during combat.
    ///
    /// IMPORTANT: Projectiles are never destroyed — only deactivated and recycled.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct ProjectilePoolSystem : ISystem
    {
        private NativeQueue<Entity> _available;
        private Entity _prefab;
        private bool _initialized;

        public void OnCreate(ref SystemState state)
        {
            _available = new NativeQueue<Entity>(Allocator.Persistent);
            _prefab = Entity.Null;
            state.RequireForUpdate<ProjectilePoolConfig>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_available.IsCreated)
                _available.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_initialized)
            {
                RecycleDeadProjectiles(ref state);
                return;
            }

            var config = SystemAPI.GetSingleton<ProjectilePoolConfig>();
            if (config.InitialCapacity <= 0)
                return;

            _prefab = CreatePrefab(ref state);
            SpawnBatch(ref state, _prefab, config.InitialCapacity);
            _initialized = true;
        }

        /// <summary>
        /// Try to acquire a projectile from the pool.
        /// Skips destroyed entities (safety net).
        /// Caller must set position, velocity, owner etc. via EntityManager.
        /// </summary>
        public bool TryAcquire(ref SystemState state, out Entity entity)
        {
            while (_available.TryDequeue(out entity))
            {
                if (state.EntityManager.Exists(entity))
                    return true;
            }

            entity = Entity.Null;
            return false;
        }

        /// <summary>
        /// Grow the pool by spawning more projectiles. Call when TryAcquire fails.
        /// </summary>
        public void Grow(ref SystemState state, int count)
        {
            if (_prefab != Entity.Null)
                SpawnBatch(ref state, _prefab, count);
        }

        /// <summary>Current number of available projectiles in pool.</summary>
        public int AvailableCount => _available.Count;

        /// <summary>
        /// Creates the projectile prefab entity with all required components.
        /// No scene GameObject needed — archetype defined entirely in code.
        /// </summary>
        private Entity CreatePrefab(ref SystemState state)
        {
            var em = state.EntityManager;
            var entity = em.CreateEntity(
                ComponentType.ReadWrite<LocalTransform>(),
                ComponentType.ReadWrite<LocalToWorld>(),
                ComponentType.ReadWrite<ProjectileConfig>(),
                ComponentType.ReadWrite<ProjectileState>(),
                ComponentType.ReadWrite<ProjectileOwner>(),
                ComponentType.ReadWrite<AmmoConfig>(),
                ComponentType.ReadWrite<IsAliveTag>(),
                ComponentType.ReadWrite<ProjectilePooled>()
            );

            em.SetComponentEnabled<IsAliveTag>(entity, false);
            em.SetComponentEnabled<ProjectilePooled>(entity, false);

            // Mark as prefab — hidden from all queries, usable for Instantiate.
            em.AddComponent<Unity.Entities.Prefab>(entity);

            return entity;
        }

        /// <summary>
        /// Recycle projectiles that have been deactivated (IsAliveTag disabled)
        /// back into the pool. Runs every frame in InitializationSystemGroup.
        /// </summary>
        private void RecycleDeadProjectiles(ref SystemState state)
        {
            foreach (var (_, entity) in
                     SystemAPI.Query<RefRO<ProjectileConfig>>()
                         .WithDisabled<IsAliveTag>()
                         .WithAll<ProjectilePooled>()
                         .WithEntityAccess())
            {
                // Reset position off-screen to avoid visual artifacts.
                state.EntityManager.SetComponentData(entity, LocalTransform.FromPosition(9999f, -9999f, 9999f));

                // Reset state to prevent stale data on next acquire.
                state.EntityManager.SetComponentData(entity, new ProjectileState());

                _available.Enqueue(entity);

                // Disable pooled marker — prevents re-enqueue next frame.
                state.EntityManager.SetComponentEnabled<ProjectilePooled>(entity, false);
            }
        }

        private void SpawnBatch(ref SystemState state, Entity prefab, int count)
        {
            var em = state.EntityManager;

            for (int i = 0; i < count; i++)
            {
                Entity e = em.Instantiate(prefab);

                // Start deactivated and off-screen.
                em.SetComponentEnabled<IsAliveTag>(e, false);
                em.SetComponentEnabled<ProjectilePooled>(e, false);
                em.SetComponentData(e, LocalTransform.FromPosition(9999f, -9999f, 9999f));

                _available.Enqueue(e);
            }
        }
    }

    /// <summary>
    /// IEnableableComponent marker on pooled projectiles.
    /// Enabled when projectile is in flight — used to detect "just died" for recycling.
    /// Disabled when sitting in pool — prevents double-recycle.
    /// </summary>
    public struct ProjectilePooled : IComponentData, IEnableableComponent
    {
    }
}