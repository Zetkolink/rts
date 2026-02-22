using Combat.ECS;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Combat.Systems
{
    /// <summary>
    /// Moves live projectiles: position += velocity * dt.
    /// Applies gravity for mortar/arty projectiles.
    /// Deactivates projectiles that exceed max range.
    /// Burst-compiled, ScheduleParallel.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(AttackSystem))]
    public partial struct ProjectileMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;

            var job = new ProjectileMoveJob
            {
                DeltaTime = dt,
                Gravity = new float3(0f, -9.81f, 0f)
            };
            job.ScheduleParallel();
        }

        [BurstCompile]
        [WithAll(typeof(IsAliveTag))]
        private partial struct ProjectileMoveJob : IJobEntity
        {
            public float DeltaTime;
            public float3 Gravity;

            public void Execute(
                ref LocalTransform transform,
                ref ProjectileState projectileState,
                in ProjectileConfig config,
                EnabledRefRW<IsAliveTag> isAlive)
            {
                // Store previous position for raycast collision.
                projectileState.PreviousPosition = transform.Position;

                // Apply gravity.
                if (config.GravityFactor > 0f)
                    projectileState.Velocity += Gravity * config.GravityFactor * DeltaTime;

                // Move.
                float3 displacement = projectileState.Velocity * DeltaTime;
                transform.Position += displacement;
                projectileState.DistanceTravelled += math.length(displacement);

                // Orient projectile along velocity.
                float3 vel = projectileState.Velocity;
                if (math.lengthsq(vel) > 0.001f)
                    transform.Rotation = quaternion.LookRotationSafe(vel, math.up());

                // Max range check.
                if (projectileState.DistanceTravelled >= config.MaxRange)
                    isAlive.ValueRW = false;
            }
        }
    }
}