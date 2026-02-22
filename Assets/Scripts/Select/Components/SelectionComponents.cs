using Unity.Entities;

namespace Select.ECS
{
    /// <summary>
    /// Enableable tag indicating this entity is currently selected by the player.
    /// Set by <see cref="ScreenSpaceSelectionSystem"/>.
    /// Command systems query <c>WithAll&lt;IsSelectedTag&gt;</c> to affect selected units.
    /// </summary>
    public struct IsSelectedTag : IComponentData, IEnableableComponent { }

    /// <summary>
    /// Config for screen-space selection. Baked from authoring.
    /// </summary>
    public struct SelectableConfig : IComponentData
    {
        /// <summary>Screen-space radius in pixels for single-click selection.</summary>
        public float ScreenRadius;

        /// <summary>World-space radius for projectile collision (replaces collider).</summary>
        public float CollisionRadius;

        /// <summary>Height of the unit (for screen-space projection of top/bottom).</summary>
        public float Height;
    }
}