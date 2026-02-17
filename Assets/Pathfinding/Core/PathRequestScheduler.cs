using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RTS.Pathfinding
{
    public sealed class PathRequest
    {
        public int Id;
        public Vector3 Start;
        public Vector3 End;
        public Action<PathResult> Callback;
        public float Priority;       // lower = processed first
        public float TimeRequested;
        public ClearanceClass Clearance;
    }

    /// <summary>
    /// Queues path requests and processes them within a per-frame time budget.
    /// Handles prioritization, adaptive iteration caps, and cancellation.
    /// </summary>
    public sealed class PathRequestScheduler
    {
        private readonly List<PathRequest> _queue = new List<PathRequest>(64);
        private readonly GridManager _grid;
        private readonly AStarSolver _solver;
        private readonly float _frameBudgetMs;
        private int _nextId;

        // ── Debug stats ──
        public int QueueDepth => _queue.Count;
        public int PathsProcessedLastFrame { get; private set; }
        public float TimeSpentLastFrameMs { get; private set; }

        public PathRequestScheduler(
            GridManager grid,
            AStarSolver solver,
            float frameBudgetMs = 2f)
        {
            _grid = grid;
            _solver = solver;
            _frameBudgetMs = frameBudgetMs;
        }

        public int Enqueue(
            Vector3 start,
            Vector3 end,
            Action<PathResult> callback,
            float priority = 0f,
            ClearanceClass clearance = ClearanceClass.Small)
        {
            int id = _nextId++;
            _queue.Add(new PathRequest
            {
                Id = id,
                Start = start,
                End = end,
                Callback = callback,
                Priority = priority,
                TimeRequested = Time.time,
                Clearance = clearance
            });
            return id;
        }

        public bool Cancel(int requestId)
        {
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                if (_queue[i].Id == requestId)
                {
                    // Swap-remove: O(1)
                    _queue[i] = _queue[_queue.Count - 1];
                    _queue.RemoveAt(_queue.Count - 1);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Call once per frame. Processes queued requests until budget exhausted.
        /// </summary>
        public void ProcessQueue()
        {
            PathsProcessedLastFrame = 0;
            TimeSpentLastFrameMs = 0f;

            if (_queue.Count == 0) return;

            // Sort by priority (low value = high priority)
            _queue.Sort((a, b) => a.Priority.CompareTo(b.Priority));

            var timer = Stopwatch.StartNew();

            while (_queue.Count > 0 && timer.Elapsed.TotalMilliseconds < _frameBudgetMs)
            {
                var request = _queue[0];
                _queue.RemoveAt(0);

                var gridStart = _grid.WorldToGrid(request.Start);
                var gridEnd = _grid.WorldToGrid(request.End);

                // Adaptive iteration cap based on remaining budget
                double remainingMs = _frameBudgetMs - timer.Elapsed.TotalMilliseconds;
                int maxIter = Mathf.Max(500, (int)(remainingMs * 3000));

                bool[] navGrid = _grid.GetNavGrid(request.Clearance);
                var result = _solver.Solve(navGrid, gridStart, gridEnd, maxIter);
                result.RequestId = request.Id;

                // Smooth if path found
                if (result.Status == PathStatus.Found || result.Status == PathStatus.Partial)
                {
                    result.Waypoints = PathSmoother.Smooth(
                        result.RawCells, navGrid, _grid.Width, _grid);

                    // Calculate total path length
                    result.PathLength = 0f;
                    for (int i = 1; i < result.Waypoints.Length; i++)
                        result.PathLength += Vector3.Distance(
                            result.Waypoints[i - 1], result.Waypoints[i]);
                }

                request.Callback?.Invoke(result);
                PathsProcessedLastFrame++;
            }

            timer.Stop();
            TimeSpentLastFrameMs = (float)timer.Elapsed.TotalMilliseconds;
        }
    }
}