using Combat.ECS;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Combat.Systems
{
    /// <summary>
    /// Weapon state machine per engaged unit:
    ///   Idle → Firing → BurstCooldown → Firing → ... → MagazineReload → Idle
    ///
    /// Handles burst fire, magazine reload, dispersion growth/recovery,
    /// and projectile spawn with random deviation inside dispersion cone.
    ///
    /// Uses <see cref="ProjectilePoolSystem"/> — zero structural changes during combat.
    /// FireShot stamps projectile stats from WeaponConfig onto pooled entities,
    /// so different unit types fire different ammo from a shared pool.
    /// Main thread — target validation + pool access.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(VelocityTrackingSystem))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial class AttackSystem : SystemBase
    {
        private ComponentLookup<LocalTransform> _transformLookup;
        private ComponentLookup<Health> _healthLookup;
        private ComponentLookup<IsDeadTag> _isDeadLookup;
        private ComponentLookup<TrackedVelocity> _velocityLookup;

        protected override void OnCreate()
        {
            RequireForUpdate<WeaponConfig>();
            RequireForUpdate<ProjectilePoolConfig>();

            _transformLookup = GetComponentLookup<LocalTransform>(true);
            _healthLookup = GetComponentLookup<Health>(true);
            _isDeadLookup = GetComponentLookup<IsDeadTag>(true);
            _velocityLookup = GetComponentLookup<TrackedVelocity>(true);
        }

        protected override void OnUpdate()
        {
            CompleteDependency();
            float dt = SystemAPI.Time.DeltaTime;

            // Refresh lookups once per frame.
            _transformLookup.Update(this);
            _healthLookup.Update(this);
            _isDeadLookup.Update(this);
            _velocityLookup.Update(this);

            // Get pool system ref for acquiring projectiles.
            var poolSystemHandle = World.Unmanaged.GetExistingUnmanagedSystem<ProjectilePoolSystem>();
            ref var pool = ref World.Unmanaged.GetUnsafeSystemRef<ProjectilePoolSystem>(poolSystemHandle);
            var poolConfig = SystemAPI.GetSingleton<ProjectilePoolConfig>();

            // SystemState ref for TryAcquire validation.
            ref var systemState = ref CheckedStateRef;

            // Frame-unique base seed from time — each entity will mix in its own index.
            uint frameSeed = (uint)(SystemAPI.Time.ElapsedTime * 100000.0 + 1.0);

            // PredictionAccuracy added to query — no random lookup on self.
            foreach (var (weapon, weaponState, attackTarget, teamTag, transform, prediction, entity) in
                     SystemAPI.Query<RefRO<WeaponConfig>, RefRW<WeaponState>, RefRO<AttackTarget>,
                             RefRO<TeamTag>, RefRO<LocalTransform>, RefRO<PredictionAccuracy>>()
                         .WithAll<IsEngagedTag>()
                         .WithDisabled<IsDeadTag>()
                         .WithEntityAccess())
            {
                ref var state = ref weaponState.ValueRW;
                var config = weapon.ValueRO;
                Entity target = attackTarget.ValueRO.Target;

                // ─── Validate target (cached lookups — one access each) ───
                if (!IsTargetValid(target))
                {
                    Disengage(entity, ref state, config);
                    continue;
                }

                // ─── Target position (cached lookup — one access) ───
                float3 myPos = transform.ValueRO.Position;
                float3 targetPos = _transformLookup[target].Position;
                float distSq = math.distancesq(myPos, targetPos);
                bool inRange = distSq <= config.EffectiveRange * config.EffectiveRange;

                // ─── Dispersion recovery ───
                state.TimeSinceLastShot += dt;
                if (state.TimeSinceLastShot > config.CycleTime)
                {
                    state.CurrentDispersion = math.max(
                        config.BaseDispersion,
                        state.CurrentDispersion - config.DispersionRecoveryRate * dt);
                }

                // ─── Per-entity seed: mix frame seed with entity index + version ───
                uint seed = frameSeed ^ ((uint)entity.Index * 2654435761u
                                         + (uint)entity.Version * 2246822519u);

                // ─── State machine ───
                switch (state.FireState)
                {
                    case WeaponFireState.Idle:
                        if (inRange)
                        {
                            state.FireState = WeaponFireState.Firing;
                            state.BurstRemaining = config.BurstSize;
                            state.Timer = 0f;
                        }

                        break;

                    case WeaponFireState.Firing:
                        if (!inRange)
                        {
                            state.FireState = WeaponFireState.Idle;
                            break;
                        }

                        state.Timer -= dt;
                        if (state.Timer > 0f)
                            break;

                        // ─── Fire a shot ───

                        // Advance RNG with shot counter for unique seed per shot in burst.
                        seed ^= (uint)state.BurstRemaining * 2654435761u;

                        // Lead prediction: aim at where target will be.
                        // Target velocity via cached lookup (single TryGet).
                        // PredictionAccuracy already in query — zero extra lookups.
                        float3 aimPos = ComputeAimPosition(
                            config, myPos, targetPos, target, prediction.ValueRO.Value);

                        FireShot(ref pool, ref systemState, poolConfig,
                            config, ref state, entity, teamTag.ValueRO,
                            myPos, aimPos, seed);

                        state.Timer = config.CycleTime;
                        state.BurstRemaining--;
                        state.TimeSinceLastShot = 0f;

                        // Dispersion grows.
                        state.CurrentDispersion = math.min(
                            config.MaxDispersion,
                            state.CurrentDispersion + config.DispersionPerShot);

                        // Magazine consumed.
                        if (state.MagazineRemaining > 0)
                        {
                            state.MagazineRemaining--;
                            if (state.MagazineRemaining <= 0)
                            {
                                state.FireState = WeaponFireState.MagazineReload;
                                state.Timer = config.MagazineReloadTime;
                                break;
                            }
                        }

                        // Burst exhausted.
                        if (state.BurstRemaining <= 0)
                        {
                            state.FireState = WeaponFireState.BurstCooldown;
                            state.Timer = config.BurstCooldown;
                        }

                        break;

                    case WeaponFireState.BurstCooldown:
                        state.Timer -= dt;
                        if (state.Timer <= 0f)
                        {
                            state.FireState = inRange ? WeaponFireState.Firing : WeaponFireState.Idle;
                            state.BurstRemaining = config.BurstSize;
                            state.Timer = 0f;
                        }

                        break;

                    case WeaponFireState.MagazineReload:
                        state.Timer -= dt;
                        if (state.Timer <= 0f)
                        {
                            state.MagazineRemaining = config.MagazineSize > 0 ? config.MagazineSize : -1;
                            state.BurstRemaining = config.BurstSize;
                            state.FireState = inRange ? WeaponFireState.Firing : WeaponFireState.Idle;
                            state.Timer = 0f;
                        }

                        break;
                }
            }
        }

        // ───────────── Helpers ─────────────

        /// <summary>
        /// Validates target entity via cached ComponentLookups.
        /// 2 cached reads vs 3 SystemAPI random lookups in original.
        /// </summary>
        private bool IsTargetValid(Entity target)
        {
            if (target == Entity.Null)
                return false;

            if (!_healthLookup.HasComponent(target))
                return false;

            if (!_isDeadLookup.HasComponent(target))
                return false;

            return !_isDeadLookup.IsComponentEnabled(target);
        }

        private void Disengage(Entity entity, ref WeaponState state, WeaponConfig config)
        {
            SystemAPI.SetComponentEnabled<IsEngagedTag>(entity, false);
            state.FireState = WeaponFireState.Idle;
            state.Timer = 0f;
            state.BurstRemaining = config.BurstSize;
            state.CurrentDispersion = config.BaseDispersion;
        }

        /// <summary>
        /// Computes lead prediction aim position.
        /// Target velocity via cached TryGetComponent (1 lookup instead of Has + Get = 2).
        /// PredictionAccuracy passed as value from query — zero extra lookups.
        /// </summary>
        private float3 ComputeAimPosition(
            WeaponConfig config, float3 myPos, float3 targetPos,
            Entity target, float accuracy)
        {
            // Single TryGet replaces HasComponent + GetComponent (2 → 1 lookup).
            if (!_velocityLookup.TryGetComponent(target, out var trackedVelocity))
                return targetPos;

            float3 targetVel = trackedVelocity.Value;
            float3 spawnPos = myPos + config.MuzzleOffset;
            float speed = math.max(config.ProjectileSpeed, 0.1f);

            // Iterative prediction (2 passes).
            float dist = math.distance(spawnPos, targetPos);
            float flightTime = dist / speed;
            float3 predicted = targetPos + targetVel * flightTime;

            float dist2 = math.distance(spawnPos, predicted);
            float flightTime2 = dist2 / speed;
            predicted = targetPos + targetVel * flightTime2;

            return math.lerp(targetPos, predicted, accuracy);
        }

        private static void FireShot(
            ref ProjectilePoolSystem pool, ref SystemState systemState,
            ProjectilePoolConfig poolConfig,
            WeaponConfig config, ref WeaponState state,
            Entity source, TeamTag sourceTeam,
            float3 sourcePos, float3 targetPos, uint seed)
        {
            // ─── Acquire from pool ───
            if (!pool.TryAcquire(ref systemState, out Entity projectile))
            {
                pool.Grow(ref systemState, poolConfig.GrowBatchSize);

                if (!pool.TryAcquire(ref systemState, out projectile))
                {
                    UnityEngine.Debug.LogWarning("[AttackSystem] Projectile pool grow failed.");
                    return;
                }
            }

            float3 spawnPos = sourcePos + config.MuzzleOffset;
            float3 baseDir = math.normalizesafe(targetPos - spawnPos);

            // Apply dispersion cone.
            float3 finalDir = ApplyDispersion(baseDir, state.CurrentDispersion, seed);
            float3 velocity = finalDir * config.ProjectileSpeed;

            var em = systemState.EntityManager;

            // Stamp projectile stats from weapon — different units fire different ammo.
            em.SetComponentData(projectile, new ProjectileConfig
            {
                Speed = config.ProjectileSpeed,
                MaxRange = config.MaxRange,
                Damage = config.Damage,
                Penetration = config.Penetration,
                GravityFactor = config.GravityFactor,
            });

            em.SetComponentData(projectile, new AmmoConfig
            {
                AmmoType = config.AmmoType,
            });

            em.SetComponentData(projectile, LocalTransform.FromPosition(spawnPos));
            em.SetComponentData(projectile, new ProjectileOwner
            {
                Source = source,
                SourceTeamId = sourceTeam.TeamId
            });
            em.SetComponentData(projectile, new ProjectileState
            {
                Velocity = velocity,
                PreviousPosition = spawnPos,
                DistanceTravelled = 0f
            });

            // Activate.
            em.SetComponentEnabled<IsAliveTag>(projectile, true);
            em.SetComponentEnabled<ProjectilePooled>(projectile, true);
        }

        /// <summary>
        /// Deviates direction by a random angle within a cone of given half-angle (degrees).
        /// </summary>
        private static float3 ApplyDispersion(float3 baseDir, float dispersionDegrees, uint seed)
        {
            if (dispersionDegrees <= 0f)
                return baseDir;

            float halfAngle = math.radians(dispersionDegrees * 0.5f);

            // Two pseudo-random values from seed.
            uint s1 = seed ^ 0xDEADBEEF;
            s1 = s1 * 1103515245 + 12345;
            float r1 = (s1 & 0x7FFFFFFF) / (float)0x7FFFFFFF; // 0..1

            uint s2 = s1 * 1103515245 + 12345;
            float r2 = (s2 & 0x7FFFFFFF) / (float)0x7FFFFFFF; // 0..1

            // Random point in circle → cone angle.
            float angle = r1 * halfAngle;
            float rotation = r2 * math.PI * 2f;

            // Build perpendicular axes.
            float3 up = math.abs(baseDir.y) < 0.99f ? math.up() : math.right();
            float3 right = math.normalizesafe(math.cross(up, baseDir));
            float3 upAxis = math.cross(baseDir, right);

            // Rotate base direction.
            float sinAngle = math.sin(angle);
            float cosAngle = math.cos(angle);
            float3 deviated = baseDir * cosAngle +
                              (right * math.cos(rotation) + upAxis * math.sin(rotation)) * sinAngle;

            return math.normalizesafe(deviated);
        }
    }
}