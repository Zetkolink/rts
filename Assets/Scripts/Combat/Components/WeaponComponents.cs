using Unity.Entities;
using Unity.Mathematics;

namespace Combat.ECS
{
    // ════════════════════════════════════════════
    //  WEAPON STATE ENUM
    // ════════════════════════════════════════════

    public enum WeaponFireState : byte
    {
        Idle,
        Firing,
        BurstCooldown,
        MagazineReload
    }

    // ════════════════════════════════════════════
    //  WEAPON CONFIG — baked
    // ════════════════════════════════════════════

    public struct WeaponConfig : IComponentData
    {
        // ── Engagement ──
        public float EffectiveRange;
        public float ProjectileSpeed;
        public float3 MuzzleOffset;

        // ── Projectile stats (stamped onto pooled entity each shot) ──
        public float Damage;
        public float Penetration;
        public float MaxRange;
        public float GravityFactor;
        public AmmoType AmmoType;

        // ── Fire cycle ──
        /// <summary>Seconds between individual shots within a burst.</summary>
        public float CycleTime;

        // ── Burst ──
        /// <summary>Shots per burst. 1 = semi-auto.</summary>
        public int BurstSize;
        /// <summary>Seconds between bursts.</summary>
        public float BurstCooldown;

        // ── Magazine ──
        /// <summary>Total rounds before reload. 0 = unlimited.</summary>
        public int MagazineSize;
        /// <summary>Seconds to reload full magazine.</summary>
        public float MagazineReloadTime;

        // ── Dispersion ──
        /// <summary>Base dispersion angle in degrees (minimum spread).</summary>
        public float BaseDispersion;
        /// <summary>Degrees added per shot.</summary>
        public float DispersionPerShot;
        /// <summary>Maximum dispersion angle in degrees.</summary>
        public float MaxDispersion;
        /// <summary>Degrees per second decay toward base when not firing.</summary>
        public float DispersionRecoveryRate;
    }

    // ════════════════════════════════════════════
    //  WEAPON STATE — runtime
    // ════════════════════════════════════════════

    public struct WeaponState : IComponentData
    {
        public WeaponFireState FireState;

        /// <summary>General-purpose timer (cycle, burst cooldown, reload).</summary>
        public float Timer;

        /// <summary>Shots remaining in current burst.</summary>
        public int BurstRemaining;

        /// <summary>Rounds remaining in magazine. -1 = unlimited.</summary>
        public int MagazineRemaining;

        /// <summary>Current dispersion angle in degrees.</summary>
        public float CurrentDispersion;

        /// <summary>Seconds since last shot. Used for dispersion recovery.</summary>
        public float TimeSinceLastShot;
    }

    // ════════════════════════════════════════════
    //  ATTACK TARGET
    // ════════════════════════════════════════════

    public struct AttackTarget : IComponentData
    {
        public Entity Target;
    }

    public struct IsEngagedTag : IComponentData, IEnableableComponent { }
}