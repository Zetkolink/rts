using Combat.ECS;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Combat.Authoring
{
    public enum ArmorMode : byte
    {
        Infantry,
        Vehicle
    }

    /// <summary>
    /// Bakes combat components onto a unit entity: Health, Team, Armor, Weapon, AttackTarget.
    /// Place on the same GO as UnitMovementAuthoring inside a SubScene.
    ///
    /// Projectile prefab is owned by the pool (<see cref="ProjectilePoolAuthoring"/>).
    /// Weapon config stamps damage, penetration, ammoType etc. onto pooled entities each shot.
    /// </summary>
    public sealed class UnitCombatAuthoring : MonoBehaviour
    {
        [Header("Health")] [SerializeField] private float maxHealth = 100f;

        [Header("Armor")] [SerializeField] private ArmorMode armorMode = ArmorMode.Infantry;

        [Header("Infantry Armor (if ArmorMode = Infantry)")] [SerializeField]
        private float infantryArmorValue = 1f;

        [SerializeField] private ArmorType infantryArmorType = ArmorType.Unarmored;

        [Header("Vehicle Armor (if ArmorMode = Vehicle)")] [SerializeField]
        private float frontThickness = 200f;

        [SerializeField] private float sideThickness = 80f;
        [SerializeField] private float rearThickness = 40f;
        [SerializeField] private float roofThickness = 20f;
        [SerializeField] private ArmorType vehicleArmorType = ArmorType.Heavy;
        [SerializeField] private float ricochetAngle = 70f;

        [Header("Team")] [Tooltip("0 = Player, 1 = Enemy.")] [SerializeField]
        private byte teamId;

        [Header("Engagement")] [SerializeField]
        private float effectiveRange = 30f;

        [SerializeField] private Vector3 muzzleOffset = new Vector3(0f, 1.2f, 0.5f);

        [Header("Projectile Stats")] [SerializeField]
        private float projectileSpeed = 50f;

        [SerializeField] private float damage = 25f;
        [SerializeField] private float penetration = 1f;

        [Tooltip("Max range projectile can travel. Usually >= effectiveRange.")] [SerializeField]
        private float maxRange = 200f;

        [Tooltip("0 = straight line (bullet). >0 = affected by gravity (mortar, arty).")] [SerializeField]
        private float gravityFactor;

        [SerializeField] private AmmoType ammoType = AmmoType.SmallArms;

        [Header("Fire Cycle")] [Tooltip("Seconds between individual shots.")] [SerializeField]
        private float cycleTime = 0.1f;

        [Header("Burst")] [Tooltip("Shots per burst. 1 = semi-auto.")] [SerializeField]
        private int burstSize = 3;

        [Tooltip("Seconds between bursts.")] [SerializeField]
        private float burstCooldown = 0.8f;

        [Header("Magazine")] [Tooltip("Rounds before reload. 0 = unlimited.")] [SerializeField]
        private int magazineSize = 30;

        [Tooltip("Seconds for full magazine reload.")] [SerializeField]
        private float magazineReloadTime = 2.5f;

        [Header("Dispersion")] [Tooltip("Base dispersion angle (degrees).")] [SerializeField]
        private float baseDispersion = 1f;

        [Tooltip("Degrees added per shot.")] [SerializeField]
        private float dispersionPerShot = 0.3f;

        [Tooltip("Max dispersion angle (degrees).")] [SerializeField]
        private float maxDispersion = 5f;

        [Tooltip("Degrees/sec recovery toward base.")] [SerializeField]
        private float dispersionRecoveryRate = 3f;

        [Header("Target Acquisition")] [Tooltip("Seconds between auto-target scans.")] [SerializeField]
        private float scanInterval = 0.3f;

        [Header("Lead Prediction")]
        [Tooltip("0 = no prediction, 1 = perfect. Affects aim lead on moving targets.")]
        [Range(0f, 1f)]
        [SerializeField]
        private float predictionAccuracy = 0.7f;

        private sealed class Baker : Baker<UnitCombatAuthoring>
        {
            public override void Bake(UnitCombatAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                // Health
                AddComponent(entity, new Health
                {
                    Current = authoring.maxHealth,
                    Max = authoring.maxHealth
                });
                AddComponent<IsDeadTag>(entity);
                SetComponentEnabled<IsDeadTag>(entity, false);
                AddBuffer<DamageEvent>(entity);

                // Team
                AddComponent(entity, new TeamTag { TeamId = authoring.teamId });

                // Armor
                if (authoring.armorMode == ArmorMode.Vehicle)
                {
                    AddComponent(entity, new VehicleArmor
                    {
                        FrontThickness = authoring.frontThickness,
                        SideThickness = authoring.sideThickness,
                        RearThickness = authoring.rearThickness,
                        RoofThickness = authoring.roofThickness,
                        ArmorType = authoring.vehicleArmorType,
                        RicochetAngle = authoring.ricochetAngle
                    });
                }
                else
                {
                    AddComponent(entity, new InfantryArmor
                    {
                        ArmorValue = authoring.infantryArmorValue,
                        ArmorType = authoring.infantryArmorType
                    });
                }

                // Weapon config — includes projectile stats
                AddComponent(entity, new WeaponConfig
                {
                    EffectiveRange = authoring.effectiveRange,
                    ProjectileSpeed = authoring.projectileSpeed,
                    MuzzleOffset = authoring.muzzleOffset,
                    Damage = authoring.damage,
                    Penetration = authoring.penetration,
                    MaxRange = authoring.maxRange,
                    GravityFactor = authoring.gravityFactor,
                    AmmoType = authoring.ammoType,
                    CycleTime = authoring.cycleTime,
                    BurstSize = authoring.burstSize,
                    BurstCooldown = authoring.burstCooldown,
                    MagazineSize = authoring.magazineSize,
                    MagazineReloadTime = authoring.magazineReloadTime,
                    BaseDispersion = authoring.baseDispersion,
                    DispersionPerShot = authoring.dispersionPerShot,
                    MaxDispersion = authoring.maxDispersion,
                    DispersionRecoveryRate = authoring.dispersionRecoveryRate
                });

                // Weapon state (initialized)
                AddComponent(entity, new WeaponState
                {
                    FireState = WeaponFireState.Idle,
                    Timer = 0f,
                    BurstRemaining = authoring.burstSize,
                    MagazineRemaining = authoring.magazineSize > 0 ? authoring.magazineSize : -1,
                    CurrentDispersion = authoring.baseDispersion,
                    TimeSinceLastShot = 0f
                });

                // Attack target (inactive at spawn)
                AddComponent(entity, new AttackTarget { Target = Entity.Null });
                AddComponent<IsEngagedTag>(entity);
                SetComponentEnabled<IsEngagedTag>(entity, false);

                // Target acquisition (staggered scan)
                AddComponent(entity, new TargetScanTimer
                {
                    Interval = authoring.scanInterval,
                    Timer = authoring.scanInterval * ((float)(entity.Index % 10) / 10f)
                });

                // Velocity tracking (for being a target of lead prediction)
                AddComponent(entity, new TrackedVelocity
                {
                    Value = float3.zero,
                    PreviousPosition = float3.zero
                });

                // Prediction accuracy (for being a shooter)
                AddComponent(entity, new PredictionAccuracy
                {
                    Value = authoring.predictionAccuracy
                });
            }
        }
    }
}