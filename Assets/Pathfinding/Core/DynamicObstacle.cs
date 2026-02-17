using UnityEngine;

namespace RTS.Pathfinding
{
    /// <summary>
    /// Attach to any runtime obstacle (building, barricade, large unit).
    /// Registers/unregisters its footprint on the pathfinding grid automatically.
    /// 
    /// Footprint is defined in cells, centered on transform.position.
    /// When enabled: blocks cells. When disabled/destroyed: unblocks cells.
    /// Call RefreshFootprint() after moving the obstacle.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class DynamicObstacle : MonoBehaviour
    {
        [Tooltip("Footprint size in grid cells (X and Z)")]
        [SerializeField] private Vector2Int _footprintCells = new Vector2Int(2, 2);

        [Tooltip("Extra padding in cells around the footprint")]
        [SerializeField] [Min(0)] private int _padding;

        private Bounds _lastBounds;
        private bool _registered;

        public Vector2Int FootprintCells => _footprintCells;

        private Bounds CalculateWorldBounds()
        {
            var api = PathfindingAPI.Instance;
            if (api == null) return default;

            float cs = api.Grid.CellSize;
            int totalX = _footprintCells.x + _padding * 2;
            int totalZ = _footprintCells.y + _padding * 2;

            Vector3 size = new Vector3(
                totalX * cs,
                api.Grid.WorldOrigin.y + 2f, // enough height for CheckBox
                totalZ * cs
            );

            return new Bounds(transform.position, size);
        }

        private void OnEnable()
        {
            // Delay one frame if PathfindingAPI hasn't initialized yet
            if (PathfindingAPI.Instance == null)
            {
                _registered = false;
                return;
            }

            Register();
        }

        private void Start()
        {
            // Catch case where OnEnable fired before PathfindingAPI.Awake
            if (!_registered && PathfindingAPI.Instance != null)
                Register();
        }

        private void Register()
        {
            _lastBounds = CalculateWorldBounds();
            PathfindingAPI.Instance.NotifyObstacleChanged(_lastBounds);
            _registered = true;

            Debug.Log($"[DynamicObstacle] Registered: {gameObject.name}, " +
                      $"footprint: {_footprintCells}, bounds: {_lastBounds}");
        }

        private void OnDisable()
        {
            if (!_registered || PathfindingAPI.Instance == null) return;

            // Re-scan old region — cells become walkable if collider is gone
            PathfindingAPI.Instance.NotifyObstacleChanged(_lastBounds);
            _registered = false;

            Debug.Log($"[DynamicObstacle] Unregistered: {gameObject.name}");
        }

        /// <summary>
        /// Call after moving this obstacle. Updates both old and new positions.
        /// </summary>
        public void RefreshFootprint()
        {
            if (PathfindingAPI.Instance == null) return;

            Bounds newBounds = CalculateWorldBounds();

            // Combined bounds covers old + new position
            Bounds combined = _lastBounds;
            combined.Encapsulate(newBounds);

            PathfindingAPI.Instance.NotifyObstacleChanged(combined);
            _lastBounds = newBounds;
        }
    }
}