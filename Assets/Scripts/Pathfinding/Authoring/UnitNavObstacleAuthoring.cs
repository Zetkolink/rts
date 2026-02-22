using Pathfinding.Movement.ECS;
using Unity.Entities;
using UnityEngine;

namespace Pathfinding.Authoring
{
    /// <summary>
    /// Authoring component for units that carve the NavMesh when stationary.
    /// Place on the same GameObject as <see cref="UnitMovementAuthoring"/> inside a SubScene.
    /// Bakes <see cref="NavObstacleConfig"/> only — the runtime GameObject with NavMeshObstacle
    /// is created by <see cref="Pathfinding.Systems.NavObstacleSystem"/>.
    /// </summary>
    public sealed class UnitNavObstacleAuthoring : MonoBehaviour
    {
        [Tooltip("NavMeshObstacle carving radius. Should match RVO radius.")] [SerializeField]
        private float radius = 0.5f;

        [Tooltip("NavMeshObstacle height.")] [SerializeField]
        private float height = 2f;

        private sealed class Baker : Baker<UnitNavObstacleAuthoring>
        {
            public override void Bake(UnitNavObstacleAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new NavObstacleConfig
                {
                    Radius = authoring.radius,
                    Height = authoring.height
                });
            }
        }
    }
}