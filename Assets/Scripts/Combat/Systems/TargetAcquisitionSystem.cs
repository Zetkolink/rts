using Combat.ECS;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Combat.Systems
{
    /// <summary>
    /// Staggered target acquisition: each unit scans for enemies every
    /// <see cref="TargetScanTimer.Interval"/> seconds using the spatial hash.
    /// Picks nearest enemy within <see cref="WeaponConfig.EffectiveRange"/>.
    /// Sets <see cref="AttackTarget"/> + enables <see cref="IsEngagedTag"/>.
    ///
    /// Only scans units that are NOT already engaged (manual attack orders take priority).
    /// Main thread — reads spatial hash, writes components.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(SpatialHashSystem))]
    [UpdateBefore(typeof(AttackSystem))]
    public partial class TargetAcquisitionSystem : SystemBase
    {
        private SystemHandle _spatialHashHandle;

        protected override void OnCreate()
        {
            RequireForUpdate<SpatialHashData>();
            RequireForUpdate<TargetScanTimer>();
        }

        protected override void OnStartRunning()
        {
            _spatialHashHandle = World.Unmanaged.GetExistingUnmanagedSystem<SpatialHashSystem>();
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            float cellSize = SystemAPI.GetSingleton<SpatialHashData>().CellSize;

            ref var spatialSystem = ref World.Unmanaged.GetUnsafeSystemRef<SpatialHashSystem>(_spatialHashHandle);
            var hashMap = spatialSystem.HashMap;

            if (!hashMap.IsCreated)
                return;

            foreach (var (scanTimer, weapon, team, transform, attackTarget, isEngaged, entity) in
                SystemAPI.Query<RefRW<TargetScanTimer>, RefRO<WeaponConfig>, RefRO<TeamTag>,
                        RefRO<LocalTransform>, RefRW<AttackTarget>, EnabledRefRW<IsEngagedTag>>()
                    .WithDisabled<IsDeadTag>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                    .WithEntityAccess())
            {
                // Skip already engaged (manual orders persist).
                if (isEngaged.ValueRO)
                    continue;

                // Tick scan timer.
                scanTimer.ValueRW.Timer -= dt;
                if (scanTimer.ValueRO.Timer > 0f)
                    continue;

                scanTimer.ValueRW.Timer = scanTimer.ValueRO.Interval;

                // Search for nearest enemy in range.
                float3 myPos = transform.ValueRO.Position;
                float range = weapon.ValueRO.EffectiveRange;
                byte myTeam = team.ValueRO.TeamId;

                Entity bestTarget = FindNearestEnemy(
                    hashMap, myPos, range, myTeam, cellSize);

                if (bestTarget != Entity.Null)
                {
                    attackTarget.ValueRW.Target = bestTarget;
                    isEngaged.ValueRW = true;
                }
            }
        }

        private static Entity FindNearestEnemy(
            NativeParallelMultiHashMap<int, SpatialHashEntry> hashMap,
            float3 position, float range, byte myTeam, float cellSize)
        {
            float rangeSq = range * range;
            float bestDistSq = rangeSq;
            Entity bestEntity = Entity.Null;

            // Search cells in range.
            int cellRange = (int)math.ceil(range / cellSize);
            int cx = (int)math.floor(position.x / cellSize);
            int cz = (int)math.floor(position.z / cellSize);

            for (int dx = -cellRange; dx <= cellRange; dx++)
            {
                for (int dz = -cellRange; dz <= cellRange; dz++)
                {
                    int key = (cx + dx) * 73856093 ^ (cz + dz) * 19349663;

                    if (!hashMap.TryGetFirstValue(key, out SpatialHashEntry entry, out var it))
                        continue;

                    do
                    {
                        // Skip friendlies and self.
                        if (entry.TeamId == myTeam)
                            continue;

                        float distSq = math.distancesq(position, entry.Position);
                        if (distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            bestEntity = entry.Entity;
                        }
                    } while (hashMap.TryGetNextValue(out entry, ref it));
                }
            }

            return bestEntity;
        }
    }
}