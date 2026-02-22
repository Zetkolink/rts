using Combat.ECS;
using Select.ECS;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Combat.Systems.Debug
{
    /// <summary>
    /// DEBUG ONLY. Draws projectile trails, health bars, and weapon state.
    /// Remove before shipping.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DamageApplicationSystem))]
    public partial struct DebugCombatDrawSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            // Projectile trails.
            foreach (var (transform, projectileState) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<ProjectileState>>()
                         .WithAll<IsAliveTag>())
            {
                float3 from = projectileState.ValueRO.PreviousPosition;
                float3 to = transform.ValueRO.Position;
                UnityEngine.Debug.DrawLine(from, to, Color.yellow);
            }

            // Health bars + weapon state on damaged/engaged units.
            foreach (var (transform, health, weaponState) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<Health>, RefRO<WeaponState>>()
                         .WithDisabled<IsDeadTag>())
            {
                float3 pos = transform.ValueRO.Position;

                // Health bar (only if damaged).
                if (health.ValueRO.Current < health.ValueRO.Max)
                {
                    float ratio = health.ValueRO.Current / health.ValueRO.Max;
                    float barWidth = 1f;
                    float3 barStart = pos + new float3(-barWidth * 0.5f, 2.5f, 0f);
                    float3 barHp = pos + new float3(-barWidth * 0.5f + barWidth * ratio, 2.5f, 0f);
                    float3 barEnd = pos + new float3(barWidth * 0.5f, 2.5f, 0f);

                    UnityEngine.Debug.DrawLine(barStart, barEnd, Color.red);
                    UnityEngine.Debug.DrawLine(barStart, barHp, Color.green);
                }

                // Weapon state indicator (colored line above unit).
                var ws = weaponState.ValueRO;
                Color stateColor = ws.FireState switch
                {
                    WeaponFireState.Firing => Color.red,
                    WeaponFireState.BurstCooldown => Color.yellow,
                    WeaponFireState.MagazineReload => Color.cyan,
                    _ => Color.clear
                };

                if (stateColor.a > 0f)
                {
                    float3 stateStart = pos + new float3(-0.3f, 2.8f, 0f);
                    float3 stateEnd = pos + new float3(0.3f, 2.8f, 0f);
                    UnityEngine.Debug.DrawLine(stateStart, stateEnd, stateColor);
                }
            }

            // Collision spheres.
            foreach (var (transform, config) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<SelectableConfig>>()
                         .WithDisabled<IsDeadTag>())
            {
                float3 pos = transform.ValueRO.Position;
                float3 center = pos + new float3(0f, config.ValueRO.Height * 0.5f, 0f);
                float radius = config.ValueRO.CollisionRadius;

                DrawWireSphere(center, radius, Color.magenta, 16);
            }

            // Attack line + dispersion cone: engaged unit → target.
            foreach (var (transform, attackTarget, weaponState, weaponConfig) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<AttackTarget>,
                             RefRO<WeaponState>, RefRO<WeaponConfig>>()
                         .WithAll<IsEngagedTag>()
                         .WithDisabled<IsDeadTag>())
            {
                Entity target = attackTarget.ValueRO.Target;
                if (target == Entity.Null || !SystemAPI.Exists(target))
                    continue;

                float3 from = transform.ValueRO.Position + weaponConfig.ValueRO.MuzzleOffset;
                float3 targetPos = SystemAPI.GetComponent<LocalTransform>(target).Position + new float3(0f, 1f, 0f);

                // Compute predicted aim position (mirrors AttackSystem.ComputeAimPosition).
                float3 aimPos = targetPos;
                if (SystemAPI.HasComponent<TrackedVelocity>(target))
                {
                    float3 targetVel = SystemAPI.GetComponent<TrackedVelocity>(target).Value;
                    float speed = weaponConfig.ValueRO.ProjectileSpeed;
                    if (speed > 0f)
                    {
                        // Iterative prediction (2 passes) — same as AttackSystem.
                        float dist1 = math.distance(from, targetPos);
                        float flightTime1 = dist1 / speed;
                        float3 predicted = targetPos + targetVel * flightTime1;

                        float dist2 = math.distance(from, predicted);
                        float flightTime2 = dist2 / speed;
                        predicted = targetPos + targetVel * flightTime2;

                        // Apply prediction accuracy if available.
                        // Use entity from outer query — need entity access.
                        aimPos = predicted;

                        // Green cross at predicted position.
                        float crossSize = 0.3f;
                        UnityEngine.Debug.DrawLine(
                            predicted - new float3(crossSize, 0, 0),
                            predicted + new float3(crossSize, 0, 0), Color.green);
                        UnityEngine.Debug.DrawLine(
                            predicted - new float3(0, 0, crossSize),
                            predicted + new float3(0, 0, crossSize), Color.green);
                    }
                }

                // Engagement line: muzzle → current target pos (dim red).
                UnityEngine.Debug.DrawLine(from, targetPos, new Color(1f, 0.3f, 0.3f, 0.3f));

                // Lead line: muzzle → predicted aim pos (green).
                UnityEngine.Debug.DrawLine(from, aimPos, new Color(0f, 1f, 0.5f, 0.5f));

                // Dispersion cone toward predicted position (where bullets actually go).
                float3 dir = math.normalizesafe(aimPos - from);
                float dist = math.distance(from, aimPos);
                float halfAngle = math.radians(weaponState.ValueRO.CurrentDispersion * 0.5f);
                float coneRadius = math.tan(halfAngle) * dist;

                // Build perpendicular axes.
                float3 up = math.abs(dir.y) < 0.99f ? math.up() : math.right();
                float3 right = math.normalizesafe(math.cross(up, dir));
                float3 upAxis = math.cross(dir, right);

                // Draw cone as 8 lines from muzzle to circle at aim distance.
                Color coneColor = new Color(1f, 0.6f, 0f, 0.4f);
                const int segments = 8;
                for (int i = 0; i < segments; i++)
                {
                    float angle = (i / (float)segments) * math.PI * 2f;
                    float3 offset = (right * math.cos(angle) + upAxis * math.sin(angle)) * coneRadius;
                    float3 edgePoint = aimPos + offset;
                    UnityEngine.Debug.DrawLine(from, edgePoint, coneColor);
                }

                // Draw circle at aim distance.
                for (int i = 0; i < segments; i++)
                {
                    float a1 = (i / (float)segments) * math.PI * 2f;
                    float a2 = ((i + 1) / (float)segments) * math.PI * 2f;
                    float3 p1 = aimPos + (right * math.cos(a1) + upAxis * math.sin(a1)) * coneRadius;
                    float3 p2 = aimPos + (right * math.cos(a2) + upAxis * math.sin(a2)) * coneRadius;
                    UnityEngine.Debug.DrawLine(p1, p2, coneColor);
                }
            }
        }

        private static void DrawWireSphere(float3 center, float radius, Color color, int segments)
        {
            DrawWireCircle(center, new float3(1, 0, 0), new float3(0, 1, 0), radius, color, segments);
            DrawWireCircle(center, new float3(1, 0, 0), new float3(0, 0, 1), radius, color, segments);
            DrawWireCircle(center, new float3(0, 1, 0), new float3(0, 0, 1), radius, color, segments);
        }

        private static void DrawWireCircle(float3 center, float3 axisA, float3 axisB,
            float radius, Color color, int segments)
        {
            float step = math.PI * 2f / segments;
            for (int i = 0; i < segments; i++)
            {
                float a1 = i * step;
                float a2 = (i + 1) * step;
                float3 p1 = center + (axisA * math.cos(a1) + axisB * math.sin(a1)) * radius;
                float3 p2 = center + (axisA * math.cos(a2) + axisB * math.sin(a2)) * radius;
                UnityEngine.Debug.DrawLine(p1, p2, color);
            }
        }
    }
}