using Unity.Entities;

namespace Pathfinding.Movement.ECS
{
    /// <summary>
    /// Unmanaged cleanup marker. ECS keeps entity alive until this is removed.
    /// Prevents orphaned NavMeshObstacle GameObjects.
    /// </summary>
    public struct NavObstacleCleanup : ICleanupComponentData { }
}