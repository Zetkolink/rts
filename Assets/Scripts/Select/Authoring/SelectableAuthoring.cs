using Select.ECS;
using Unity.Entities;
using UnityEngine;

namespace Select.Authoring
{
    /// <summary>
    /// Bakes selection config. No companion GameObjects created.
    /// Selection is handled via screen-space projection in <see cref="ScreenSpaceSelectionSystem"/>.
    /// </summary>
    public sealed class SelectableAuthoring : MonoBehaviour
    {
        [Tooltip("Screen radius in pixels for click-selection tolerance.")]
        [SerializeField] private float screenRadius = 30f;

        [Tooltip("World-space collision radius (for projectile hits).")]
        [SerializeField] private float collisionRadius = 0.5f;

        [Tooltip("Unit height for screen-space bounding.")]
        [SerializeField] private float height = 2f;

        private sealed class Baker : Baker<SelectableAuthoring>
        {
            public override void Bake(SelectableAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent<IsSelectedTag>(entity);
                SetComponentEnabled<IsSelectedTag>(entity, false);

                AddComponent(entity, new SelectableConfig
                {
                    ScreenRadius = authoring.screenRadius,
                    CollisionRadius = authoring.collisionRadius,
                    Height = authoring.height
                });
            }
        }
    }
}