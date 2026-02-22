using Unity.Entities;
using Unity.Mathematics;

namespace Combat.ECS
{
    // ════════════════════════════════════════════
    //  PROJECTILE CONFIG — baked from prefab
    // ════════════════════════════════════════════

    /// <summary>
    /// Immutable config baked onto the projectile prefab entity.
    /// </summary>
    public struct ProjectileConfig : IComponentData
    {
        public float Speed;
        public float MaxRange;
        public float Damage;
        public float Penetration;
        public float GravityFactor; // 0 = bullet, >0 = mortar/arty
    }

    // ════════════════════════════════════════════
    //  PROJECTILE STATE — runtime per-instance
    // ════════════════════════════════════════════

    /// <summary>
    /// Runtime state of a live projectile.
    /// </summary>
    public struct ProjectileState : IComponentData
    {
        public float3 Velocity;
        public float3 PreviousPosition;
        public float DistanceTravelled;
    }

    /// <summary>
    /// Who fired this projectile. Used to avoid self-hits and for kill tracking.
    /// </summary>
    public struct ProjectileOwner : IComponentData
    {
        public Entity Source;
        public byte SourceTeamId;
    }

    /// <summary>
    /// Tag: projectile is alive and flying.
    /// Disabled when projectile hits or exceeds max range.
    /// </summary>
    public struct IsAliveTag : IComponentData, IEnableableComponent
    {
    }
}