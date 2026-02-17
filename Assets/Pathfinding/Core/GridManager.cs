using System.Collections.Generic;
using UnityEngine;

namespace RTS.Pathfinding
{
    public sealed class GridManager
    {
        // Raw grid: actual physics, no erosion
        private readonly bool[] _rawWalkable;

        // Navigation grids: one per clearance class, each with different erosion
        private readonly Dictionary<ClearanceClass, bool[]> _navGrids;

        // Legacy single nav grid — points to Small clearance for backwards compat
        private readonly bool[] _navWalkable;

        private readonly GridSettings _settings;

        // Pre-computed erode steps per clearance class
        private readonly Dictionary<ClearanceClass, int> _erodeSteps;

        public int Width => _settings.width;
        public int Height => _settings.height;
        public float CellSize => _settings.cellSize;
        public Vector3 WorldOrigin => _settings.worldOrigin;

        private static readonly ClearanceClass[] AllClasses =
        {
            ClearanceClass.Small,
            ClearanceClass.Medium,
            ClearanceClass.Large
        };

        public GridManager(GridSettings settings)
        {
            _settings = settings;
            int total = settings.width * settings.height;
            _rawWalkable = new bool[total];

            _navGrids = new Dictionary<ClearanceClass, bool[]>(3);
            _erodeSteps = new Dictionary<ClearanceClass, int>(3);

            foreach (var cls in AllClasses)
            {
                _navGrids[cls] = new bool[total];
                float radius = settings.GetClearanceRadius(cls);
                _erodeSteps[cls] = Mathf.CeilToInt(radius / settings.cellSize);
            }

            // Legacy compat: _navWalkable is the Small grid
            _navWalkable = _navGrids[ClearanceClass.Small];
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
        /// Navigation walkability for default (Small) clearance.
        /// Backwards-compatible with existing callers.
        /// </summary>
        public bool IsNavWalkable(int x, int z)
        {
            if (!InBounds(x, z)) return false;
            return _navWalkable[z * _settings.width + x];
        }

        public bool IsNavWalkable(Vector2Int cell) => IsNavWalkable(cell.x, cell.y);

        /// <summary>
        /// Navigation walkability for a specific clearance class.
        /// </summary>
        public bool IsNavWalkable(int x, int z, ClearanceClass clearance)
        {
            if (!InBounds(x, z)) return false;
            return _navGrids[clearance][z * _settings.width + x];
        }

        public bool IsNavWalkable(Vector2Int cell, ClearanceClass clearance) =>
            IsNavWalkable(cell.x, cell.y, clearance);

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
            foreach (var cls in AllClasses)
                _navGrids[cls][idx] = walkable;
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
                    foreach (var cls in AllClasses)
                        _navGrids[cls][idx] = !blocked;
                }
            }

            RebuildAllNavGrids();
        }

        public void UpdateRegion(Bounds worldBounds)
        {
            // Use largest clearance radius for expansion to ensure all grids are correct
            float maxRadius = Mathf.Max(
                _settings.clearanceRadiusSmall,
                Mathf.Max(_settings.clearanceRadiusMedium, _settings.clearanceRadiusLarge)
            );
            Bounds expandedBounds = worldBounds;
            expandedBounds.Expand(maxRadius * 2f);

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

            // Rebuild all nav grids for this region
            foreach (var cls in AllClasses)
                RebuildNavGridRegion(cls, min, max);
        }

        // ───────────── Navigation Grids (multi-clearance erosion) ─────────────

        private void RebuildAllNavGrids()
        {
            foreach (var cls in AllClasses)
                RebuildNavGrid(cls);
        }

        private void RebuildNavGrid(ClearanceClass clearance)
        {
            int total = _settings.width * _settings.height;
            bool[] navGrid = _navGrids[clearance];
            System.Array.Copy(_rawWalkable, navGrid, total);

            int erode = _erodeSteps[clearance];
            if (erode <= 0) return;

            for (int z = 0; z < _settings.height; z++)
            {
                for (int x = 0; x < _settings.width; x++)
                {
                    if (_rawWalkable[z * _settings.width + x]) continue;
                    ErodeAround(navGrid, x, z, erode);
                }
            }
        }

        private void RebuildNavGridRegion(ClearanceClass clearance, Vector2Int min, Vector2Int max)
        {
            int erode = _erodeSteps[clearance];
            bool[] navGrid = _navGrids[clearance];

            // Reset nav = raw for the region
            for (int z = min.y; z <= max.y; z++)
                for (int x = min.x; x <= max.x; x++)
                    navGrid[z * _settings.width + x] = _rawWalkable[z * _settings.width + x];

            if (erode <= 0) return;

            int scanMinX = Mathf.Max(0, min.x - erode);
            int scanMinZ = Mathf.Max(0, min.y - erode);
            int scanMaxX = Mathf.Min(_settings.width - 1, max.x + erode);
            int scanMaxZ = Mathf.Min(_settings.height - 1, max.y + erode);

            for (int z = scanMinZ; z <= scanMaxZ; z++)
            {
                for (int x = scanMinX; x <= scanMaxX; x++)
                {
                    if (_rawWalkable[z * _settings.width + x]) continue;
                    ErodeAroundClamped(navGrid, x, z, erode, min, max);
                }
            }
        }

        private void ErodeAround(bool[] navGrid, int cx, int cz, int erodeSteps)
        {
            int radiusSq = erodeSteps * erodeSteps;
            for (int dz = -erodeSteps; dz <= erodeSteps; dz++)
            {
                for (int dx = -erodeSteps; dx <= erodeSteps; dx++)
                {
                    if (dx == 0 && dz == 0) continue;

                    int nx = cx + dx;
                    int nz = cz + dz;
                    if (!InBounds(nx, nz)) continue;

                    if (dx * dx + dz * dz <= radiusSq)
                        navGrid[nz * _settings.width + nx] = false;
                }
            }
        }

        private void ErodeAroundClamped(bool[] navGrid, int cx, int cz, int erodeSteps,
            Vector2Int min, Vector2Int max)
        {
            int radiusSq = erodeSteps * erodeSteps;
            for (int dz = -erodeSteps; dz <= erodeSteps; dz++)
            {
                for (int dx = -erodeSteps; dx <= erodeSteps; dx++)
                {
                    if (dx == 0 && dz == 0) continue;

                    int nx = cx + dx;
                    int nz = cz + dz;

                    if (nx < min.x || nx > max.x || nz < min.y || nz > max.y) continue;

                    if (dx * dx + dz * dz <= radiusSq)
                        navGrid[nz * _settings.width + nx] = false;
                }
            }
        }

        // ───────────── Grid Accessors for Solver ─────────────

        /// <summary>Navigation grid for default (Small) clearance. Backwards compat.</summary>
        public bool[] GetNavGrid() => _navWalkable;

        /// <summary>Navigation grid for a specific clearance class.</summary>
        public bool[] GetNavGrid(ClearanceClass clearance) => _navGrids[clearance];

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
