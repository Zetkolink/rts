using Unity.Entities;

namespace Combat.ECS
{
    // ════════════════════════════════════════════
    //  ENUMS
    // ════════════════════════════════════════════

    public enum AmmoType : byte
    {
        SmallArms,  // rifles, MGs
        AP,         // armor-piercing
        HE,         // high-explosive
        HEAT        // shaped charge (RPG, ATGM)
    }

    public enum ArmorType : byte
    {
        Unarmored,  // infantry, soft vehicles
        Light,      // APCs, light vehicles
        Medium,     // IFVs
        Heavy       // MBTs
    }

    // ════════════════════════════════════════════
    //  INFANTRY ARMOR — flat value, no zones
    // ════════════════════════════════════════════

    /// <summary>
    /// Simple flat armor for infantry / soft targets.
    /// No directional zones. Cover bonuses add to ArmorValue at runtime.
    /// </summary>
    public struct InfantryArmor : IComponentData
    {
        public float ArmorValue;
        public ArmorType ArmorType;
    }

    // ════════════════════════════════════════════
    //  VEHICLE ARMOR — directional zones
    // ════════════════════════════════════════════

    /// <summary>
    /// Directional zoned armor for vehicles.
    /// Zone determined by dot(vehicleForward, -projectileDirection).
    /// Each zone has nominal thickness; effective thickness = nominal / cos(impactAngle).
    /// Ricochet at impact angles > RicochetAngle.
    /// </summary>
    public struct VehicleArmor : IComponentData
    {
        public float FrontThickness;
        public float SideThickness;
        public float RearThickness;
        public float RoofThickness;
        public ArmorType ArmorType;

        /// <summary>Impact angle (degrees) above which projectile ricochets. Typically 70.</summary>
        public float RicochetAngle;
    }

    // ════════════════════════════════════════════
    //  AMMO CONFIG — on projectile prefab
    // ════════════════════════════════════════════

    /// <summary>
    /// Ammo type on the projectile. Used for damage matrix lookup.
    /// Baked from <see cref="Combat.Authoring.ProjectileAuthoring"/>.
    /// </summary>
    public struct AmmoConfig : IComponentData
    {
        public AmmoType AmmoType;
    }

    // ════════════════════════════════════════════
    //  DAMAGE MATRIX — singleton, baked constants
    // ════════════════════════════════════════════

    /// <summary>
    /// 4×4 damage multiplier matrix: AmmoType × ArmorType.
    /// Singleton entity. Values baked from <see cref="Combat.Authoring.DamageMatrixAuthoring"/>.
    /// Accessed as: matrix[ammoType * 4 + armorType].
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct DamageMatrixEntry : IBufferElementData
    {
        public float Multiplier;
    }

    /// <summary>
    /// Tag on the singleton entity holding the damage matrix buffer.
    /// </summary>
    public struct DamageMatrixTag : IComponentData { }
}