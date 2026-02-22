using Unity.Entities;
using UnityEngine;
using UnityEngine.AI;

namespace Pathfinding.Movement.ECS
{
    /// <summary>
    /// Config for NavMeshObstacle carving. Baked from authoring.
    /// </summary>
    public struct NavObstacleConfig : IComponentData
    {
        public float Radius;
        public float Height;
    }

    /// <summary>
    /// Managed component holding a runtime-created <see cref="GameObject"/>
    /// with <see cref="NavMeshObstacle"/>. Created by <see cref="Pathfinding.Systems.NavObstacleSystem"/>
    /// at runtime, position synced from <see cref="Unity.Transforms.LocalTransform"/> each frame.
    /// </summary>
    public class NavObstacleRef : IComponentData, System.IDisposable
    {
        public GameObject Go;
        public NavMeshObstacle Obstacle;
        public bool WasMoving;

        public void Dispose()
        {
            if (Go != null)
            {
                Object.Destroy(Go);
                Go = null;
                Obstacle = null;
            }
        }
    }
}