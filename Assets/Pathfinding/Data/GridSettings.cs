using UnityEngine;

namespace RTS.Pathfinding
{
    [CreateAssetMenu(fileName = "GridSettings", menuName = "RTS/Pathfinding/Grid Settings")]
    public sealed class GridSettings : ScriptableObject
    {
        [Header("Grid Dimensions")]
        [Tooltip("World-space origin of the grid (bottom-left corner)")]
        public Vector3 worldOrigin = Vector3.zero;

        [Tooltip("Number of cells along X axis")]
        [Min(1)] public int width = 128;

        [Tooltip("Number of cells along Z axis")]
        [Min(1)] public int height = 128;

        [Tooltip("Size of one cell in world units")]
        [Min(0.1f)] public float cellSize = 1f;

        [Header("Obstacle Detection")]
        [Tooltip("LayerMask for static obstacles baked into grid")]
        public LayerMask staticObstacleMask;

        [Tooltip("LayerMask for dynamic obstacles updated at runtime")]
        public LayerMask dynamicObstacleMask;

        [Tooltip("Height of the overlap box used for obstacle checks")]
        [Min(0.1f)] public float obstacleCheckHeight = 2f;
        
        [Header("Agent")]
        [Tooltip("Agent radius in world units. Obstacles are expanded by this amount.")]
        [Min(0f)] public float agentRadius = 0.4f;
    }
}