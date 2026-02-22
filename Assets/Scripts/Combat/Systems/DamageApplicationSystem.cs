using Combat.ECS;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Combat.Systems
{
    /// <summary>
    /// Consumes <see cref="DamageEvent"/> buffer, resolves armor, applies damage to <see cref="Health"/>.
    ///
    /// Resolution pipeline per event:
    ///   1. Determine armor (infantry flat / vehicle zoned)
    ///   2. Penetration check (penetration vs effective armor)
    ///   3. Ricochet check (vehicle only, impact angle > threshold)
    ///   4. Damage matrix multiplier (AmmoType × ArmorType)
    ///   5. Damage reduction: finalDamage = baseDamage * matrixMult * (1 - armor/(armor + K))
    ///
    /// Main thread — component lookups per event are sparse, not worth parallelizing.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(ProjectileCollisionSystem))]
    public partial class DamageApplicationSystem : SystemBase
    {
        /// <summary>Scaling constant for diminishing armor reduction.</summary>
        private const float ArmorK = 50f;

        /// <summary>Minimum damage fraction when penetration succeeds (prevents zero damage).</summary>
        private const float MinDamageFraction = 0.1f;

        /// <summary>HE deals small splash damage even on failed penetration.</summary>
        private const float HeSplashFraction = 0.15f;

        protected override void OnUpdate()
        {
            // Get damage matrix singleton.
            var matrixBuffer = default(DynamicBuffer<DamageMatrixEntry>);
            bool hasMatrix = false;

            foreach (var buf in SystemAPI.Query<DynamicBuffer<DamageMatrixEntry>>()
                         .WithAll<DamageMatrixTag>())
            {
                matrixBuffer = buf;
                hasMatrix = true;
                break;
            }

            foreach (var (health, damageBuffer, transform, isDead, entity) in
                SystemAPI.Query<RefRW<Health>, DynamicBuffer<DamageEvent>,
                        RefRO<LocalTransform>, EnabledRefRW<IsDeadTag>>()
                    .WithDisabled<IsDeadTag>()
                    .WithEntityAccess())
            {
                if (damageBuffer.Length == 0)
                    continue;

                // Determine armor type for this entity.
                bool isVehicle = SystemAPI.HasComponent<VehicleArmor>(entity);
                bool isInfantry = SystemAPI.HasComponent<InfantryArmor>(entity);

                VehicleArmor vehicleArmor = default;
                InfantryArmor infantryArmor = default;
                if (isVehicle)
                    vehicleArmor = SystemAPI.GetComponent<VehicleArmor>(entity);
                else if (isInfantry)
                    infantryArmor = SystemAPI.GetComponent<InfantryArmor>(entity);

                float3 entityPos = transform.ValueRO.Position;
                float3 entityForward = math.mul(transform.ValueRO.Rotation, math.forward());

                float totalDamage = 0f;

                for (int i = 0; i < damageBuffer.Length; i++)
                {
                    var evt = damageBuffer[i];
                    float damage = ResolveEvent(
                        evt, entityPos, entityForward,
                        isVehicle, vehicleArmor,
                        isInfantry, infantryArmor,
                        hasMatrix, matrixBuffer);
                    totalDamage += damage;
                }

                damageBuffer.Clear();

                if (totalDamage <= 0f)
                    continue;

                health.ValueRW.Current -= totalDamage;
                if (health.ValueRO.Current <= 0f)
                {
                    health.ValueRW.Current = 0f;
                    isDead.ValueRW = true;
                }
            }
        }

        private static float ResolveEvent(
            DamageEvent evt, float3 entityPos, float3 entityForward,
            bool isVehicle, VehicleArmor vehicleArmor,
            bool isInfantry, InfantryArmor infantryArmor,
            bool hasMatrix, DynamicBuffer<DamageMatrixEntry> matrixBuffer)
        {
            float effectiveArmor;
            ArmorType armorType;
            bool isRicochet = false;

            if (isVehicle)
            {
                ResolveVehicleArmor(evt.Direction, entityForward,
                    vehicleArmor, out effectiveArmor, out armorType, out isRicochet);
            }
            else if (isInfantry)
            {
                effectiveArmor = infantryArmor.ArmorValue;
                armorType = infantryArmor.ArmorType;
            }
            else
            {
                // No armor component — take full damage.
                effectiveArmor = 0f;
                armorType = ArmorType.Unarmored;
            }

            // Ricochet = no damage (except HE splash).
            if (isRicochet)
            {
                if (evt.AmmoType == AmmoType.HE)
                    return evt.Amount * HeSplashFraction;
                return 0f;
            }

            // Penetration check.
            bool penetrated = evt.Penetration >= effectiveArmor;

            if (!penetrated)
            {
                // Failed penetration. HE still deals splash.
                if (evt.AmmoType == AmmoType.HE)
                    return evt.Amount * HeSplashFraction;
                return 0f;
            }

            // Damage matrix multiplier.
            float matrixMult = 1f;
            if (hasMatrix)
            {
                int index = (int)evt.AmmoType * 4 + (int)armorType;
                if (index >= 0 && index < matrixBuffer.Length)
                    matrixMult = matrixBuffer[index].Multiplier;
            }

            // Damage reduction from armor (diminishing returns).
            float reduction = effectiveArmor / (effectiveArmor + ArmorK);
            float finalDamage = evt.Amount * matrixMult * math.max(MinDamageFraction, 1f - reduction);

            return finalDamage;
        }

        // ───────────── Vehicle Zone Resolution ─────────────

        private static void ResolveVehicleArmor(
            float3 projectileDir, float3 vehicleForward,
            VehicleArmor armor,
            out float effectiveArmor, out ArmorType armorType, out bool isRicochet)
        {
            armorType = armor.ArmorType;
            isRicochet = false;

            // Incoming direction is projectile's travel direction.
            // We compare -projectileDir (direction FROM projectile TO vehicle) with vehicle forward.
            float3 incomingDir = -math.normalizesafe(projectileDir);
            float dotForward = math.dot(vehicleForward, incomingDir);

            // Determine zone by forward dot product.
            float nominalThickness;
            if (dotForward > 0.7071f) // > 45° from front = front zone
                nominalThickness = armor.FrontThickness;
            else if (dotForward < -0.7071f) // > 45° from rear = rear zone
                nominalThickness = armor.RearThickness;
            else
                nominalThickness = armor.SideThickness;

            // Check for roof hit: steep downward angle.
            float dotUp = math.dot(math.up(), -math.normalizesafe(projectileDir));
            if (dotUp > 0.7071f) // projectile coming from above at > 45°
                nominalThickness = armor.RoofThickness;

            // Impact angle: angle between surface normal and projectile direction.
            // Simplified: use abs(dot) of incoming vs zone normal.
            float absDot = math.abs(dotForward);
            if (absDot < 0.001f) absDot = 0.001f; // prevent division by zero

            // Effective thickness = nominal / cos(impactAngle).
            // cos(impactAngle) ≈ absDot for front/rear.
            effectiveArmor = nominalThickness / absDot;

            // Ricochet check: impact angle in degrees.
            float impactAngleDeg = math.degrees(math.acos(math.saturate(absDot)));
            if (impactAngleDeg > armor.RicochetAngle)
                isRicochet = true;
        }
    }
}