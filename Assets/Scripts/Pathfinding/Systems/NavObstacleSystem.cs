using System.Collections.Generic;
using Pathfinding.Movement.ECS;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;

namespace Pathfinding.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeferredMoveSystem))]
    public partial class NavObstacleSystem : SystemBase
    {
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
            SyncStoppedPositions();
            CleanupDestroyed();
        }

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

            if (pending.Length == 0)
            {
                pending.Dispose();
                return;
            }

            // Batch structural change — one archetype move for all entities.
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < pending.Length; i++)
            {
                var entity = pending[i];
                if (!EntityManager.Exists(entity))
                    continue;

                var config = EntityManager.GetComponentData<NavObstacleConfig>(entity);
                var transform = EntityManager.GetComponentData<LocalTransform>(entity);

                // Check current movement state at creation time.
                bool isMoving = IsEntityMoving(entity);

                var go = new GameObject($"NavObstacle_{entity.Index}_{entity.Version}");
                go.hideFlags = HideFlags.DontSave;
                go.transform.position = transform.Position;

                var obstacle = go.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Capsule;
                obstacle.radius = config.Radius;
                obstacle.height = config.Height;
                obstacle.carving = true;
                obstacle.enabled = !isMoving; // ← disabled if already moving

                _instances[entity] = new NavObstacleInstance
                {
                    Go = go,
                    Obstacle = obstacle,
                    WasMoving = isMoving, // ← correct initial state
                };

                ecb.AddComponent<NavObstacleCleanup>(entity);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
            pending.Dispose();
        }

        private bool IsEntityMoving(Entity entity)
        {
            if (EntityManager.HasComponent<IsMovingTag>(entity)
                && EntityManager.IsComponentEnabled<IsMovingTag>(entity))
                return true;

            if (EntityManager.HasComponent<DeferredMove>(entity))
            {
                var deferred = EntityManager.GetComponentData<DeferredMove>(entity);
                if (deferred.FramesLeft > 0)
                    return true;
            }

            return false;
        }

        private void HandleTransitions()
        {
            // Primary path — entities with DeferredMove.
            foreach (var (transform, deferred, isMoving, entity) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<DeferredMove>,
                             EnabledRefRO<IsMovingTag>>()
                         .WithAll<NavObstacleCleanup>()
                         .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                         .WithEntityAccess())
            {
                if (!_instances.TryGetValue(entity, out var inst) || inst.Go == null)
                    continue;

                bool moving = isMoving.ValueRO || deferred.ValueRO.FramesLeft > 0;
                ApplyTransition(entity, ref inst, moving, transform.ValueRO.Position);
            }

            // Fallback — entities WITHOUT DeferredMove (freshly spawned, buildings, etc).
            foreach (var (transform, isMoving, entity) in
                     SystemAPI.Query<RefRO<LocalTransform>, EnabledRefRO<IsMovingTag>>()
                         .WithAll<NavObstacleCleanup>()
                         .WithNone<DeferredMove>()
                         .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                         .WithEntityAccess())
            {
                if (!_instances.TryGetValue(entity, out var inst) || inst.Go == null)
                    continue;

                bool moving = isMoving.ValueRO;
                ApplyTransition(entity, ref inst, moving, transform.ValueRO.Position);
            }
        }

        private void ApplyTransition(Entity entity, ref NavObstacleInstance inst, 
                                     bool moving, float3 position)
        {
            if (moving == inst.WasMoving)
                return;

            inst.WasMoving = moving;

            if (moving)
            {
                inst.Obstacle.enabled = false;
            }
            else
            {
                inst.Go.transform.position = position;
                inst.Obstacle.enabled = true;
            }

            _instances[entity] = inst;
        }

        /// <summary>
        /// Safety net: sync position for stopped entities that might have been
        /// repositioned externally (formation shuffle, push, teleport).
        /// Runs only for enabled obstacles — cheap float3 comparison.
        /// </summary>
        private void SyncStoppedPositions()
        {
            foreach (var (transform, entity) in
                     SystemAPI.Query<RefRO<LocalTransform>>()
                         .WithAll<NavObstacleCleanup>()
                         .WithEntityAccess())
            {
                if (!_instances.TryGetValue(entity, out var inst))
                    continue;
                if (inst.Go == null || inst.WasMoving || !inst.Obstacle.enabled)
                    continue;

                var pos = transform.ValueRO.Position;
                var goPos = inst.Go.transform.position;

                const float toleranceSq = 0.01f; // 10cm
                float dx = pos.x - goPos.x;
                float dz = pos.z - goPos.z;

                if (dx * dx + dz * dz > toleranceSq)
                {
                    inst.Go.transform.position = pos;
                }
            }
        }

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