using Unity.Entities;
using Unity.Mathematics;

namespace Combat.ECS
{
    // ════════════════════════════════════════════
    //  HEALTH
    // ════════════════════════════════════════════

    /// <summary>
    /// Current and max health. When Current reaches 0, unit is dead.
    /// </summary>
    public struct Health : IComponentData
    {
        public float Current;
        public float Max;
    }

    /// <summary>
    /// Tag enabled when entity is dead (HP ≤ 0).
    /// Systems skip dead entities via WithDisabled or WithNone.
    /// </summary>
    public struct IsDeadTag : IComponentData, IEnableableComponent { }

    // ════════════════════════════════════════════
    //  TEAM
    // ════════════════════════════════════════════

    /// <summary>
    /// Which team this entity belongs to. Used for target validation.
    /// </summary>
    public struct TeamTag : IComponentData
    {
        public byte TeamId;
    }

    // ════════════════════════════════════════════
    //  DAMAGE
    // ════════════════════════════════════════════

    /// <summary>
    /// Append-buffer for incoming damage events. Multiple projectiles can
    /// hit the same entity in one frame — buffer collects them all.
    /// Consumed by <see cref="DamageApplicationSystem"/> each frame.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct DamageEvent : IBufferElementData
    {
        public float Amount;
        public float Penetration;
        public float3 HitPoint;
        public float3 Direction;
        public AmmoType AmmoType;
        // public Entity Source; // future: for kill tracking
    }
}