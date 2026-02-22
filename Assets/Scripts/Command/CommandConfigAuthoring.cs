using Unity.Entities;
using UnityEngine;

namespace Command
{
    /// <summary>
    /// Singleton config for <see cref="CommandResolverSystem"/>.
    /// Baked from <see cref="CommandConfigAuthoring"/>. One per scene.
    /// </summary>
    public struct CommandConfig : IComponentData
    {
        public int GroundLayerMask;
        public int UnitLayerMask;
        public float MaxRayDistance;
        public float UnitSpacing;
        public int DeferFrames;
    }

    /// <summary>
    /// Place on a single GameObject in the SubScene. Provides config for command resolution.
    /// </summary>
    public sealed class CommandConfigAuthoring : MonoBehaviour
    {
        [SerializeField] private LayerMask groundLayer;
        [SerializeField] private LayerMask unitLayer;
        [SerializeField] private float maxRayDistance = 500f;
        [SerializeField] private float unitSpacing = 1.5f;
        [SerializeField] private int deferFrames = 2;

        private sealed class Baker : Baker<CommandConfigAuthoring>
        {
            public override void Bake(CommandConfigAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new CommandConfig
                {
                    GroundLayerMask = authoring.groundLayer,
                    UnitLayerMask = authoring.unitLayer,
                    MaxRayDistance = authoring.maxRayDistance,
                    UnitSpacing = authoring.unitSpacing,
                    DeferFrames = authoring.deferFrames
                });
            }
        }
    }
}