using UnityEngine;

namespace RTS.Pathfinding
{
    public sealed class GridManager
    {
        // Raw grid: actual physics, no erosion
        private readonly bool[] _rawWalkable;

        // Navigation grid: raw + erosion applied. Used by A* solver.
        private readonly bool[] _navWalkable;

        private readonly GridSettings _settings;

        public int Width => _settings.width;
        public int Height => _settings.height;
        public float CellSize => _settings.cellSize;
        public Vector3 WorldOrigin => _settings.worldOrigin;

        public GridManager(GridSettings settings)
        {
            _settings = settings;
            int total = settings.width * settings.height;
            _rawWalkable = new bool[total];
            _navWalkable = new bool[total];
        }

        // ───────────── Coordinate Conversion ─────────────

        public Vector2Int WorldToGrid(Vector3 worldPos)
        {
            float localX = worldPos.x - _settings.worldOrigin.x;
            float localZ = worldPos.z - _settings.worldOrigin.z;
            int gx = Mathf.FloorToInt(localX / _settings.cellSize);
            int gz = Mathf.FloorToInt(localZ / _settings.cellSize);
            return new Vector2Int(
                Mathf.Clamp(gx, 0, _settings.width - 1),
                Mathf.Clamp(gz, 0, _settings.height - 1)
            );
        }

        public Vector3 GridToWorld(int x, int z)
        {
            float wx = _settings.worldOrigin.x + (x + 0.5f) * _settings.cellSize;
            float wz = _settings.worldOrigin.z + (z + 0.5f) * _settings.cellSize;
            return new Vector3(wx, _settings.worldOrigin.y, wz);
        }

        public Vector3 GridToWorld(Vector2Int cell) => GridToWorld(cell.x, cell.y);

        // ───────────── Grid Access ─────────────

        public bool InBounds(int x, int z) =>
            x >= 0 && x < _settings.width && z >= 0 && z < _settings.height;

        public bool InBounds(Vector2Int cell) => InBounds(cell.x, cell.y);

        /// <summary>
        /// Raw physics walkability. Use for unit position validation:
        /// "is the unit physically standing on solid ground?"
        /// </summary>
        public bool IsRawWalkable(int x, int z)
        {
            if (!InBounds(x, z)) return false;
            return _rawWalkable[z * _settings.width + x];
        }

        public bool IsRawWalkable(Vector2Int cell) => IsRawWalkable(cell.x, cell.y);

        /// <summary>
        /// Navigation walkability (raw + erosion). Use for pathfinding:
        /// "can an agent of agentRadius traverse this cell safely?"
        /// </summary>
        public bool IsNavWalkable(int x, int z)
        {
            if (!InBounds(x, z)) return false;
            return _navWalkable[z * _settings.width + x];
        }

        public bool IsNavWalkable(Vector2Int cell) => IsNavWalkable(cell.x, cell.y);

        /// <summary>
        /// Combined check: position is valid if raw-walkable.
        /// Path will use nav grid, but unit is allowed to BE here.
        /// </summary>
        public bool IsPositionValid(Vector3 worldPos)
        {
            var cell = WorldToGrid(worldPos);
            return IsRawWalkable(cell);
        }

        public void SetWalkable(int x, int z, bool walkable)
        {
            if (!InBounds(x, z)) return;
            int idx = z * _settings.width + x;
            _rawWalkable[idx] = walkable;
            _navWalkable[idx] = walkable;
        }

        // ───────────── Bake ─────────────

        public void BakeStaticObstacles()
        {
            Vector3 halfExtents = BuildHalfExtents();
            LayerMask mask = _settings.staticObstacleMask;

            for (int z = 0; z < _settings.height; z++)
            {
                for (int x = 0; x < _settings.width; x++)
                {
                    Vector3 center = GridToWorld(x, z);
                    center.y += _settings.obstacleCheckHeight * 0.5f;

                    bool blocked = Physics.CheckBox(
                        center, halfExtents,
                        Quaternion.identity, mask,
                        QueryTriggerInteraction.Ignore
                    );

                    int idx = z * _settings.width + x;
                    _rawWalkable[idx] = !blocked;
                    _navWalkable[idx] = !blocked;
                }
            }

            RebuildNavGrid();
        }

        public void UpdateRegion(Bounds worldBounds)
        {
            float erodeWorld = _settings.agentRadius;
            Bounds expandedBounds = worldBounds;
            expandedBounds.Expand(erodeWorld * 2f);

            Vector2Int min = WorldToGrid(expandedBounds.min);
            Vector2Int max = WorldToGrid(expandedBounds.max);
            Vector3 halfExtents = BuildHalfExtents();
            LayerMask combinedMask = _settings.staticObstacleMask | _settings.dynamicObstacleMask;

            // Physics scan → raw grid
            for (int z = min.y; z <= max.y; z++)
            {
                for (int x = min.x; x <= max.x; x++)
                {
                    Vector3 center = GridToWorld(x, z);
                    center.y += _settings.obstacleCheckHeight * 0.5f;

                    bool blocked = Physics.CheckBox(
                        center, halfExtents,
                        Quaternion.identity, combinedMask,
                        QueryTriggerInteraction.Ignore
                    );

                    int idx = z * _settings.width + x;
                    _rawWalkable[idx] = !blocked;
                }
            }

            // Rebuild nav grid for this region
            RebuildNavGridRegion(min, max);
        }

        // ───────────── Navigation Grid (erosion) ─────────────

        /// <summary>
        /// Full rebuild: copy raw → nav, then erode.
        /// Called after BakeStaticObstacles.
        /// </summary>
        private void RebuildNavGrid()
        {
            int total = _settings.width * _settings.height;
            System.Array.Copy(_rawWalkable, _navWalkable, total);

            int erodeSteps = Mathf.CeilToInt(_settings.agentRadius / _settings.cellSize);
            if (erodeSteps <= 0) return;

            // Erode: for each blocked cell in raw, block neighbors in nav
            for (int z = 0; z < _settings.height; z++)
            {
                for (int x = 0; x < _settings.width; x++)
                {
                    if (_rawWalkable[z * _settings.width + x]) continue;
                    ErodeAround(x, z, erodeSteps);
                }
            }
        }

        /// <summary>
        /// Regional rebuild: reset nav from raw, then erode.
        /// Reads raw grid beyond region borders for correct erosion at edges.
        /// </summary>
        private void RebuildNavGridRegion(Vector2Int min, Vector2Int max)
        {
            int erodeSteps = Mathf.CeilToInt(_settings.agentRadius / _settings.cellSize);

            // Reset nav = raw for the region
            for (int z = min.y; z <= max.y; z++)
                for (int x = min.x; x <= max.x; x++)
                    _navWalkable[z * _settings.width + x] = _rawWalkable[z * _settings.width + x];

            if (erodeSteps <= 0) return;

            // Scan wider area for erosion sources (blocked cells outside region
            // can erode INTO the region)
            int scanMinX = Mathf.Max(0, min.x - erodeSteps);
            int scanMinZ = Mathf.Max(0, min.y - erodeSteps);
            int scanMaxX = Mathf.Min(_settings.width - 1, max.x + erodeSteps);
            int scanMaxZ = Mathf.Min(_settings.height - 1, max.y + erodeSteps);

            for (int z = scanMinZ; z <= scanMaxZ; z++)
            {
                for (int x = scanMinX; x <= scanMaxX; x++)
                {
                    if (_rawWalkable[z * _settings.width + x]) continue;

                    // Only erode targets within the original region
                    ErodeAroundClamped(x, z, erodeSteps, min, max);
                }
            }
        }

        private void ErodeAround(int cx, int cz, int erodeSteps)
        {
            for (int dz = -erodeSteps; dz <= erodeSteps; dz++)
            {
                for (int dx = -erodeSteps; dx <= erodeSteps; dx++)
                {
                    if (dx == 0 && dz == 0) continue;

                    int nx = cx + dx;
                    int nz = cz + dz;
                    if (!InBounds(nx, nz)) continue;

                    // Circular erosion
                    if (dx * dx + dz * dz <= erodeSteps * erodeSteps)
                        _navWalkable[nz * _settings.width + nx] = false;
                }
            }
        }

        private void ErodeAroundClamped(int cx, int cz, int erodeSteps, Vector2Int min, Vector2Int max)
        {
            for (int dz = -erodeSteps; dz <= erodeSteps; dz++)
            {
                for (int dx = -erodeSteps; dx <= erodeSteps; dx++)
                {
                    if (dx == 0 && dz == 0) continue;

                    int nx = cx + dx;
                    int nz = cz + dz;

                    // Only write within target region
                    if (nx < min.x || nx > max.x || nz < min.y || nz > max.y) continue;

                    if (dx * dx + dz * dz <= erodeSteps * erodeSteps)
                        _navWalkable[nz * _settings.width + nx] = false;
                }
            }
        }

        // ───────────── Grid Accessors for Solver ─────────────

        /// <summary>Navigation grid for A* solver.</summary>
        public bool[] GetNavGrid() => _navWalkable;

        /// <summary>Raw grid for debug overlay and position validation.</summary>
        public bool[] GetRawGrid() => _rawWalkable;

        private Vector3 BuildHalfExtents()
        {
            return new Vector3(
                _settings.cellSize * 0.45f,
                _settings.obstacleCheckHeight * 0.5f,
                _settings.cellSize * 0.45f
            );
        }
    }
}