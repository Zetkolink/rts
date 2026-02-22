using Combat.ECS;
using Select.ECS;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Combat.Systems
{
    /// <summary>
    /// Detects projectile hits via spatial-hash-accelerated ray-sphere intersection
    /// against alive unit entities, plus CollisionWorld.CastRay for terrain/obstacles.
    ///
    /// Architecture:
    ///   1. Collect all projectile data into a NativeArray (main thread, cheap).
    ///   2. Run a Burst-compiled parallel job that:
    ///      a) Queries spatial hash cells along the projectile segment.
    ///      b) Ray-sphere tests only against candidates from those cells.
    ///      c) CollisionWorld.CastRay for terrain (Burst-compatible).
    ///      d) Writes results to a NativeArray.
    ///   3. Main thread applies results: DamageEvent buffers + disable IsAliveTag.
    ///
    /// Complexity: O(P * K) where P = projectiles, K = avg units per queried cell (~5-20).
    /// Previous brute-force was O(P * N) where N = all alive units.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(ProjectileMovementSystem))]
    [UpdateAfter(typeof(SpatialHashSystem))]
    public partial struct ProjectileCollisionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SpatialHashData>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // ─── Gather projectile data ───
            var projectileQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, ProjectileState, ProjectileConfig,
                    ProjectileOwner, AmmoConfig, IsAliveTag>()
                .Build();

            int projectileCount = projectileQuery.CalculateEntityCount();
            if (projectileCount == 0) return;

            var projectileData = new NativeArray<ProjectileCollisionInput>(projectileCount, Allocator.TempJob);
            var projectileEntities = new NativeArray<Entity>(projectileCount, Allocator.TempJob);
            var results = new NativeArray<ProjectileCollisionResult>(projectileCount, Allocator.TempJob);

            // Fill arrays — main thread, sequential but cheap (just reading components).
            int idx = 0;
            foreach (var (transform, projState, config, owner, ammoConfig, entity) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<ProjectileState>,
                             RefRO<ProjectileConfig>, RefRO<ProjectileOwner>,
                             RefRO<AmmoConfig>>()
                         .WithAll<IsAliveTag>()
                         .WithEntityAccess())
            {
                projectileData[idx] = new ProjectileCollisionInput
                {
                    From = projState.ValueRO.PreviousPosition,
                    To = transform.ValueRO.Position,
                    Damage = config.ValueRO.Damage,
                    Penetration = config.ValueRO.Penetration,
                    AmmoType = ammoConfig.ValueRO.AmmoType,
                    SourceTeamId = owner.ValueRO.SourceTeamId,
                    Source = owner.ValueRO.Source,
                };
                projectileEntities[idx] = entity;
                idx++;
            }

            // ─── Get spatial hash + physics world ───
            var spatialHashSystemHandle = state.WorldUnmanaged.GetExistingUnmanagedSystem<SpatialHashSystem>();
            ref var spatialHashSystemRef =
                ref state.WorldUnmanaged.GetUnsafeSystemRef<SpatialHashSystem>(spatialHashSystemHandle);
            var hashMap = spatialHashSystemRef.HashMap;

            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            float cellSize = SystemAPI.GetSingleton<SpatialHashData>().CellSize;
            float invCellSize = 1f / cellSize;

            // ─── Schedule parallel Burst job ───
            var collisionJob = new ProjectileCollisionJob
            {
                Projectiles = projectileData,
                Results = results,
                HashMap = hashMap,
                CollisionWorld = physicsWorld.CollisionWorld,
                CellSize = cellSize,
                InvCellSize = invCellSize,
            };

            state.Dependency = collisionJob.Schedule(projectileCount, 64, state.Dependency);
            state.Dependency.Complete(); // Need results immediately for structural changes.

            // ─── Apply results ───
            for (int i = 0; i < projectileCount; i++)
            {
                var result = results[i];
                Entity projEntity = projectileEntities[i];

                if (result.HitType == HitType.None)
                    continue;

                // Deactivate projectile.
                SystemAPI.SetComponentEnabled<IsAliveTag>(projEntity, false);

                // Write damage event if unit was hit.
                if (result.HitType == HitType.Unit && result.HitEntity != Entity.Null)
                {
                    if (SystemAPI.HasBuffer<DamageEvent>(result.HitEntity))
                    {
                        var buffer = SystemAPI.GetBuffer<DamageEvent>(result.HitEntity);
                        var input = projectileData[i];
                        buffer.Add(new DamageEvent
                        {
                            Amount = input.Damage,
                            Penetration = input.Penetration,
                            HitPoint = result.HitPoint,
                            Direction = result.HitDirection,
                            AmmoType = input.AmmoType,
                        });
                    }
                }
            }

            projectileData.Dispose();
            projectileEntities.Dispose();
            results.Dispose();
        }
    }

    public enum HitType : byte
    {
        None,
        Unit,
        Terrain,
    }

    public struct ProjectileCollisionInput
    {
        public float3 From;
        public float3 To;
        public float Damage;
        public float Penetration;
        public AmmoType AmmoType;
        public int SourceTeamId;
        public Entity Source;
    }

    public struct ProjectileCollisionResult
    {
        public HitType HitType;
        public Entity HitEntity;
        public float3 HitPoint;
        public float3 HitDirection;
    }

    /// <summary>
    /// Burst-compiled parallel job. Each work item = one projectile.
    /// Reads spatial hash (NativeParallelMultiHashMap — concurrent read safe).
    /// Reads CollisionWorld (concurrent read safe).
    /// Writes only to its own Results[index].
    /// </summary>
    [BurstCompile]
    public struct ProjectileCollisionJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ProjectileCollisionInput> Projectiles;
        [WriteOnly] public NativeArray<ProjectileCollisionResult> Results;

        [ReadOnly] public NativeParallelMultiHashMap<int, SpatialHashEntry> HashMap;
        [ReadOnly] public CollisionWorld CollisionWorld;

        public float CellSize;
        public float InvCellSize;

        public void Execute(int index)
        {
            var proj = Projectiles[index];
            float3 from = proj.From;
            float3 to = proj.To;
            float3 delta = to - from;
            float segmentLen = math.length(delta);

            if (segmentLen < 0.001f)
            {
                Results[index] = default;
                return;
            }

            float3 direction = delta / segmentLen;

            // ─── 1. Spatial hash query: ray-sphere against nearby units ───
            Entity hitEntity = Entity.Null;
            float hitT = segmentLen;
            float3 hitPoint = to;

            // Walk cells along the XZ projection of the segment.
            float3 minPos = math.min(from, to);
            float3 maxPos = math.max(from, to);

            int minCellX = (int)math.floor(minPos.x * InvCellSize);
            int maxCellX = (int)math.floor(maxPos.x * InvCellSize);
            int minCellZ = (int)math.floor(minPos.z * InvCellSize);
            int maxCellZ = (int)math.floor(maxPos.z * InvCellSize);

            for (int cx = minCellX; cx <= maxCellX; cx++)
            {
                for (int cz = minCellZ; cz <= maxCellZ; cz++)
                {
                    int hash = cx * 73856093 ^ cz * 19349663;

                    if (!HashMap.TryGetFirstValue(hash, out var entry, out var it))
                        continue;

                    do
                    {
                        // Skip friendlies and self.
                        if (entry.TeamId == (byte)proj.SourceTeamId)
                            continue;
                        if (entry.Entity == proj.Source)
                            continue;

                        float3 center = entry.Position + new float3(0f, entry.HalfHeight, 0f);
                        float radius = entry.CollisionRadius;

                        if (RaySphereIntersect(from, direction, center, radius, segmentLen, out float t))
                        {
                            if (t < hitT)
                            {
                                hitT = t;
                                hitEntity = entry.Entity;
                                hitPoint = from + direction * t;
                            }
                        }
                    } while (HashMap.TryGetNextValue(out entry, ref it));
                }
            }

            // ─── 2. CollisionWorld.CastRay for terrain/obstacles ───
            var rayInput = new RaycastInput
            {
                Start = from,
                End = to,
                Filter = new CollisionFilter
                {
                    BelongsTo = ~0u,
                    CollidesWith = 1u << 0, // Terrain/static obstacle layer. Adjust to your layer setup.
                    GroupIndex = 0,
                },
            };

            if (CollisionWorld.CastRay(rayInput, out var terrainHit))
            {
                float terrainDist = terrainHit.Fraction * segmentLen;
                if (terrainDist < hitT)
                {
                    Results[index] = new ProjectileCollisionResult
                    {
                        HitType = HitType.Terrain,
                        HitEntity = Entity.Null,
                        HitPoint = math.lerp(from, to, terrainHit.Fraction),
                        HitDirection = direction,
                    };
                    return;
                }
            }

            // ─── 3. Unit hit result ───
            if (hitEntity != Entity.Null)
            {
                Results[index] = new ProjectileCollisionResult
                {
                    HitType = HitType.Unit,
                    HitEntity = hitEntity,
                    HitPoint = hitPoint,
                    HitDirection = direction,
                };
                return;
            }

            Results[index] = default;
        }

        /// <summary>
        /// Ray-sphere intersection. Burst-compatible, no branching on managed types.
        /// </summary>
        private static bool RaySphereIntersect(
            float3 rayOrigin, float3 rayDir, float3 sphereCenter, float sphereRadius,
            float maxT, out float t)
        {
            t = 0f;
            float3 oc = rayOrigin - sphereCenter;
            float b = math.dot(oc, rayDir);
            float c = math.dot(oc, oc) - sphereRadius * sphereRadius;

            float discriminant = b * b - c;
            if (discriminant < 0f)
                return false;

            float sqrtD = math.sqrt(discriminant);

            float t0 = -b - sqrtD;
            if (t0 > 0f && t0 < maxT)
            {
                t = t0;
                return true;
            }

            float t1 = -b + sqrtD;
            if (t1 > 0f && t1 < maxT)
            {
                t = t1;
                return true;
            }

            return false;
        }
    }
}