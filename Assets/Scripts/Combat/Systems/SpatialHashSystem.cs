using Combat.ECS;
using Select.ECS;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Combat.Systems
{
    /// <summary>
    /// Singleton component holding the spatial hash map.
    /// Rebuilt every frame by <see cref="SpatialHashSystem"/>.
    /// </summary>
    public struct SpatialHashData : IComponentData
    {
        /// <summary>World-space cell size. Larger = fewer cells, more entities per cell.</summary>
        public float CellSize;
    }

    /// <summary>
    /// Entry in the spatial hash map.
    /// Used by collision, target acquisition, suppression radius checks.
    /// </summary>
    public struct SpatialHashEntry
    {
        public Entity Entity;
        public float3 Position;
        public float  CollisionRadius;
        public float  HalfHeight;
        public byte   TeamId;
    }

    /// <summary>
    /// Rebuilds a <see cref="NativeParallelMultiHashMap{TKey,TValue}"/> each frame with all alive units
    /// keyed by grid cell. Used by ProjectileCollisionSystem, TargetAcquisitionSystem, etc.
    /// Burst-compiled main thread (rebuild is fast, parallel query is the consumer).
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateBefore(typeof(TargetAcquisitionSystem))]
    public partial struct SpatialHashSystem : ISystem
    {
        private NativeParallelMultiHashMap<int, SpatialHashEntry> _hashMap;

        public const float DefaultCellSize = 10f;

        public NativeParallelMultiHashMap<int, SpatialHashEntry> HashMap => _hashMap;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _hashMap = new NativeParallelMultiHashMap<int, SpatialHashEntry>(256, Allocator.Persistent);

            state.EntityManager.CreateSingleton(new SpatialHashData
            {
                CellSize = DefaultCellSize
            });

            state.RequireForUpdate<SpatialHashData>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (_hashMap.IsCreated)
                _hashMap.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float cellSize = SystemAPI.GetSingleton<SpatialHashData>().CellSize;

            // Count alive units for capacity.
            int count = 0;
            foreach (var _ in SystemAPI.Query<RefRO<LocalTransform>, RefRO<TeamTag>>()
                         .WithDisabled<IsDeadTag>())
            {
                count++;
            }

            _hashMap.Clear();
            if (_hashMap.Capacity < count)
                _hashMap.Capacity = count;

            // Insert all alive units.
            foreach (var (transform, team, config, entity) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<TeamTag>, RefRO<SelectableConfig>>()
                         .WithDisabled<IsDeadTag>()
                         .WithEntityAccess())
            {
                float3 pos = transform.ValueRO.Position;
                int key = Hash(pos, cellSize);

                _hashMap.Add(key, new SpatialHashEntry
                {
                    Entity          = entity,
                    Position        = pos,
                    CollisionRadius = config.ValueRO.CollisionRadius,
                    HalfHeight      = config.ValueRO.Height * 0.5f,
                    TeamId          = team.ValueRO.TeamId,
                });
            }
        }

        public static int Hash(float3 position, float cellSize)
        {
            int x = (int)math.floor(position.x / cellSize);
            int z = (int)math.floor(position.z / cellSize);
            return x * 73856093 ^ z * 19349663;
        }
    }
}