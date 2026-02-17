using System;
using UnityEngine;
using RTS.Pathfinding.Debugging;

namespace RTS.Pathfinding
{
    /// <summary>
    /// Main entry point for all pathfinding operations.
    /// Initializes subsystems, processes queue each frame.
    /// Singleton — accessed via PathfindingAPI.Instance.
    /// </summary>
    public sealed class PathfindingAPI : MonoBehaviour
    {
        [SerializeField] private GridSettings _settings;
        [SerializeField] private PathfindingDebugOverlay _debugOverlay;

        [Header("Performance")]
        [SerializeField] [Range(0.5f, 5f)] private float _frameBudgetMs = 2f;

        private GridManager _grid;
        private AStarSolver _solver;
        private PathRequestScheduler _scheduler;

        public static PathfindingAPI Instance { get; private set; }

        // ── Read-only access for external systems ──
        public GridManager Grid => _grid;
        public int PendingRequests => _scheduler.QueueDepth;
        public int PathsProcessedLastFrame => _scheduler.PathsProcessedLastFrame;
        public float TimeSpentLastFrameMs => _scheduler.TimeSpentLastFrameMs;

        private void Awake()
        {
            if (Instance != null)
            {
                Debug.LogWarning("[PathfindingAPI] Duplicate instance destroyed");
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _grid = new GridManager(_settings);
            _grid.BakeStaticObstacles();

            _solver = new AStarSolver(_settings.width, _settings.height);
            _scheduler = new PathRequestScheduler(_grid, _solver, _frameBudgetMs);

            if (_debugOverlay != null)
            {
                _debugOverlay.GridManager = _grid;
                _debugOverlay.RefreshGridTexture();
            }

            bool[] raw = _grid.GetRawGrid();
            int blocked = 0;
            for (int i = 0; i < raw.Length; i++)
                if (!raw[i]) blocked++;

            Debug.Log($"[PathfindingAPI] Ready. Grid: {_settings.width}x{_settings.height}, " +
                      $"Blocked: {blocked}, Budget: {_frameBudgetMs}ms");
        }

        private void Update()
        {
            _scheduler.ProcessQueue();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ───────────── PUBLIC API ─────────────

        /// <summary>
        /// Async path request. Callback fires on main thread within 1–N frames.
        /// Returns request ID for cancellation.
        /// </summary>
        public int RequestPath(
            Vector3 start,
            Vector3 end,
            Action<PathResult> callback,
            float priority = 0f)
        {
            return _scheduler.Enqueue(start, end, callback, priority);
        }

        /// <summary>
        /// Synchronous path request. Blocks until complete.
        /// Use sparingly — only for single critical paths (player click).
        /// </summary>
        public PathResult RequestPathImmediate(Vector3 start, Vector3 end)
        {
            var gridStart = _grid.WorldToGrid(start);
            var gridEnd = _grid.WorldToGrid(end);

            var result = _solver.Solve(_grid.GetNavGrid(), gridStart, gridEnd);

            if (result.Status == PathStatus.Found || result.Status == PathStatus.Partial)
            {
                result.Waypoints = PathSmoother.Smooth(
                    result.RawCells, _grid.GetNavGrid(), _grid.Width, _grid);

                result.PathLength = 0f;
                for (int i = 1; i < result.Waypoints.Length; i++)
                    result.PathLength += Vector3.Distance(
                        result.Waypoints[i - 1], result.Waypoints[i]);
            }

            return result;
        }

        /// <summary>Cancel a pending path request.</summary>
        public bool CancelRequest(int requestId) => _scheduler.Cancel(requestId);

        /// <summary>Check if a world position is on a walkable cell.</summary>
        public bool IsPositionWalkable(Vector3 worldPos)
        {
            return _grid.IsPositionValid(worldPos); // uses raw grid
        }
        
        /// <summary>
        /// Notify that obstacles changed within bounds.
        /// Re-scans affected cells using combined static+dynamic mask.
        /// </summary>
        public void NotifyObstacleChanged(Bounds worldBounds)
        {
            _grid.UpdateRegion(worldBounds);

            if (_debugOverlay != null)
                _debugOverlay.RefreshGridTexture();
        }
    }
}