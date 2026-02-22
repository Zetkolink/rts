using System.Collections.Generic;
using Pathfinding.Movement.ECS;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;

namespace Pathfinding.Systems
{
    /// <summary>
    /// Manages runtime <see cref="NavMeshObstacle"/> GameObjects for entities with
    /// <see cref="NavObstacleConfig"/>.
    ///
    /// Uses <see cref="NavObstacleCleanup"/> (ICleanupComponentData) to guarantee
    /// GO destruction — ECS will not fully destroy an entity until cleanup is removed.
    ///
    /// Reacts only to state transitions — zero work while unit stays idle or keeps moving.
    ///
    /// Main thread — managed GO access.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeferredMoveSystem))]
    public partial class NavObstacleSystem : SystemBase
    {
        /// <summary>Managed storage: Entity → runtime GO. Single source of truth.</summary>
        private readonly Dictionary<Entity, NavObstacleInstance> _instances = new(256);

        private struct NavObstacleInstance
        {
            public GameObject Go;
            public NavMeshObstacle Obstacle;
            public bool WasMoving;
        }

        protected override void OnCreate()
        {
            RequireForUpdate<NavObstacleConfig>();
        }

        protected override void OnUpdate()
        {
            CreateObstacles();
            HandleTransitions();
            CleanupDestroyed();
        }

        /// <summary>
        /// Spawn runtime GameObjects for entities that have config but no cleanup marker yet.
        /// </summary>
        private void CreateObstacles()
        {
            var pending = new NativeList<Entity>(16, Allocator.Temp);

            foreach (var (_, _, entity) in
                     SystemAPI.Query<RefRO<NavObstacleConfig>, RefRO<LocalTransform>>()
                         .WithNone<NavObstacleCleanup>()
                         .WithEntityAccess())
            {
                pending.Add(entity);
            }

            for (int i = 0; i < pending.Length; i++)
            {
                var entity = pending[i];
                if (!EntityManager.Exists(entity))
                    continue;

                var config = EntityManager.GetComponentData<NavObstacleConfig>(entity);
                var transform = EntityManager.GetComponentData<LocalTransform>(entity);

                var go = new GameObject($"NavObstacle_{entity.Index}_{entity.Version}");
                go.hideFlags = HideFlags.DontSave;
                go.transform.position = transform.Position;

                var obstacle = go.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Capsule;
                obstacle.radius = config.Radius;
                obstacle.height = config.Height;
                obstacle.carving = true;
                obstacle.enabled = true;

                _instances[entity] = new NavObstacleInstance
                {
                    Go = go,
                    Obstacle = obstacle,
                    WasMoving = false,
                };

                EntityManager.AddComponent<NavObstacleCleanup>(entity);
            }

            pending.Dispose();
        }

        /// <summary>
        /// Detect moving→stopped and stopped→moving transitions.
        /// Only those frames do any work.
        /// </summary>
        private void HandleTransitions()
        {
            foreach (var (transform, deferred, isMoving, entity) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<DeferredMove>,
                             EnabledRefRO<IsMovingTag>>()
                         .WithAll<NavObstacleCleanup>()
                         .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                         .WithEntityAccess())
            {
                if (!_instances.TryGetValue(entity, out var inst))
                    continue;
                if (inst.Go == null)
                    continue;

                bool moving = isMoving.ValueRO || deferred.ValueRO.FramesLeft > 0;

                if (moving == inst.WasMoving)
                    continue;

                inst.WasMoving = moving;
                _instances[entity] = inst;

                if (moving)
                {
                    inst.Obstacle.enabled = false;
                }
                else
                {
                    inst.Go.transform.position = transform.ValueRO.Position;
                    inst.Obstacle.enabled = true;
                }
            }
        }

        /// <summary>
        /// Detect zombie entities: have Cleanup but lost Config (= entity was destroyed).
        /// ECS kept them alive for us. Now we clean up GO and let them die.
        /// </summary>
        private void CleanupDestroyed()
        {
            var zombies = new NativeList<Entity>(8, Allocator.Temp);

            foreach (var entity in
                     SystemAPI.QueryBuilder()
                         .WithAll<NavObstacleCleanup>()
                         .WithNone<NavObstacleConfig>()
                         .Build()
                         .ToEntityArray(Allocator.Temp))
            {
                if (_instances.TryGetValue(entity, out var inst))
                {
                    if (inst.Go != null)
                        Object.Destroy(inst.Go);
                    _instances.Remove(entity);
                }

                zombies.Add(entity);
            }

            for (int i = 0; i < zombies.Length; i++)
            {
                EntityManager.RemoveComponent<NavObstacleCleanup>(zombies[i]);
            }

            zombies.Dispose();
        }

        protected override void OnDestroy()
        {
            foreach (var inst in _instances.Values)
            {
                if (inst.Go != null)
                    Object.Destroy(inst.Go);
            }

            _instances.Clear();
        }
    }
}