using UnityEngine;

namespace RTS.Pathfinding.Debugging
{
    /// <summary>
    /// Visualizes the pathfinding grid and debug data.
    /// 
    /// Grid walkability: rendered as a Texture2D on a runtime-generated quad.
    /// Works in both Scene View and Game View.
    /// 
    /// Paths and A* sets: rendered via Gizmos (Scene View only).
    /// 
    /// Attach to any GameObject in the scene. Call RefreshGridTexture()
    /// after bake or grid changes to update the visual.
    /// </summary>
    public sealed class PathfindingDebugOverlay : MonoBehaviour
    {
        [Header("Grid Visualization")]
        [SerializeField] private bool _showGrid = true;
        [SerializeField] private Color _walkableColor = new Color(0f, 0.8f, 0f, 0.25f);
        [SerializeField] private Color _blockedColor = new Color(1f, 0f, 0f, 0.5f);
        [SerializeField] [Range(0f, 0.5f)] private float _gridElevation = 0.05f;

        [Header("Path Visualization")]
        [SerializeField] private bool _showPath = true;
        [SerializeField] private Color _pathColor = Color.yellow;
        [SerializeField] private Color _rawPathColor = new Color(1f, 0.5f, 0f, 0.4f);
        [SerializeField] private bool _showRawPath;
        [SerializeField] [Range(0.05f, 0.5f)] private float _waypointRadius = 0.15f;

        [Header("A* Debug")]
        [SerializeField] private bool _showSearchSets;
        [SerializeField] private Color _openSetColor = new Color(0f, 0.5f, 1f, 0.5f);
        [SerializeField] private Color _closedSetColor = new Color(0.5f, 0f, 1f, 0.25f);

        // ───────────── Set by external systems ─────────────

        public GridManager GridManager { get; set; }

        /// <summary>Smoothed path (world-space). Set after pathfinding.</summary>
        public Vector3[] DebugPath { get; set; }

        /// <summary>Raw grid-space path before smoothing.</summary>
        public Vector2Int[] DebugRawPath { get; set; }

        /// <summary>A* open set snapshot (grid-space).</summary>
        public Vector2Int[] OpenSet { get; set; }

        /// <summary>A* closed set snapshot (grid-space).</summary>
        public Vector2Int[] ClosedSet { get; set; }

        // ───────────── Grid texture rendering ─────────────

        private Texture2D _gridTexture;
        private GameObject _gridQuad;
        private MaterialPropertyBlock _mpb;
        private MeshRenderer _quadRenderer;
        private bool _textureNeedsRefresh;

        /// <summary>
        /// Call after BakeStaticObstacles() or UpdateRegion() to update the visual.
        /// </summary>
        public void RefreshGridTexture()
        {
            _textureNeedsRefresh = true;
        }

        private void LateUpdate()
        {
            if (!_showGrid || GridManager == null) return;

            if (_gridQuad == null)
            {
                CreateGridQuad();
            }

            if (_textureNeedsRefresh)
            {
                RebuildTexture();
                _textureNeedsRefresh = false;
            }

            _gridQuad.SetActive(_showGrid);
        }

        private void CreateGridQuad()
        {
            _gridQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _gridQuad.name = "[PathfindingDebug] Grid Overlay";
            _gridQuad.transform.SetParent(transform);

            _gridQuad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            var col = _gridQuad.GetComponent<Collider>();
            if (col != null) Destroy(col);

            float totalWidth = GridManager.Width * GridManager.CellSize;
            float totalHeight = GridManager.Height * GridManager.CellSize;

            Vector3 center = GridManager.WorldOrigin + new Vector3(
                totalWidth * 0.5f,
                _gridElevation,
                totalHeight * 0.5f
            );

            _gridQuad.transform.position = center;
            _gridQuad.transform.localScale = new Vector3(totalWidth, totalHeight, 1f);

            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Transparent");
            }
            if (shader == null)
            {
                return;
            }

            _quadRenderer = _gridQuad.GetComponent<MeshRenderer>();
            var material = new Material(shader);
            material.renderQueue = 3100;
            _quadRenderer.material = material;

            _mpb = new MaterialPropertyBlock();
        }

        private void RebuildTexture()
        {
            if (GridManager == null) return;

            int w = GridManager.Width;
            int h = GridManager.Height;

            if (_gridTexture == null || _gridTexture.width != w || _gridTexture.height != h)
            {
                if (_gridTexture != null) Destroy(_gridTexture);
                _gridTexture = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point, // sharp per-cell pixels
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            bool[] walkable = GridManager.GetRawGrid();
            var pixels = _gridTexture.GetPixelData<Color32>(0);

            Color32 walkColor32 = _walkableColor;
            Color32 blockColor32 = _blockedColor;

            // Open/closed set overlay (if enabled)
            bool[] isOpen = null;
            bool[] isClosed = null;

            if (_showSearchSets)
            {
                int total = w * h;
                if (OpenSet != null)
                {
                    isOpen = new bool[total];
                    foreach (var cell in OpenSet)
                    {
                        int idx = cell.y * w + cell.x;
                        if (idx >= 0 && idx < total) isOpen[idx] = true;
                    }
                }
                if (ClosedSet != null)
                {
                    isClosed = new bool[total];
                    foreach (var cell in ClosedSet)
                    {
                        int idx = cell.y * w + cell.x;
                        if (idx >= 0 && idx < total) isClosed[idx] = true;
                    }
                }
            }

            Color32 openColor32 = _openSetColor;
            Color32 closedColor32 = _closedSetColor;

            for (int i = 0; i < pixels.Length; i++)
            {
                if (_showSearchSets && isOpen != null && isOpen[i])
                    pixels[i] = openColor32;
                else if (_showSearchSets && isClosed != null && isClosed[i])
                    pixels[i] = closedColor32;
                else
                    pixels[i] = walkable[i] ? walkColor32 : blockColor32;
            }

            _gridTexture.Apply();

            _quadRenderer.GetPropertyBlock(_mpb);
            _mpb.SetTexture("_MainTex", _gridTexture);
            _quadRenderer.SetPropertyBlock(_mpb);
        }

        // ───────────── Gizmos: paths and waypoints (Scene View) ─────────────

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (GridManager == null) return;

            DrawSmoothedPath();
            DrawRawPath();
        }

        private void DrawSmoothedPath()
        {
            if (!_showPath || DebugPath == null || DebugPath.Length < 2) return;

            float lift = _gridElevation + 0.1f;

            Gizmos.color = _pathColor;
            for (int i = 0; i < DebugPath.Length - 1; i++)
            {
                Vector3 a = DebugPath[i] + Vector3.up * lift;
                Vector3 b = DebugPath[i + 1] + Vector3.up * lift;
                Gizmos.DrawLine(a, b);
            }

            // Waypoint spheres
            for (int i = 0; i < DebugPath.Length; i++)
            {
                float radius = (i == 0 || i == DebugPath.Length - 1)
                    ? _waypointRadius * 1.5f  // start/end larger
                    : _waypointRadius;
                Gizmos.DrawSphere(DebugPath[i] + Vector3.up * lift, radius);
            }
        }

        private void DrawRawPath()
        {
            if (!_showRawPath || DebugRawPath == null || DebugRawPath.Length < 2) return;
            if (GridManager == null) return;

            float lift = _gridElevation + 0.05f;

            Gizmos.color = _rawPathColor;
            for (int i = 0; i < DebugRawPath.Length - 1; i++)
            {
                Vector3 a = GridManager.GridToWorld(DebugRawPath[i]) + Vector3.up * lift;
                Vector3 b = GridManager.GridToWorld(DebugRawPath[i + 1]) + Vector3.up * lift;
                Gizmos.DrawLine(a, b);
            }
        }
#endif

        // ───────────── Cleanup ─────────────

        private void OnDisable()
        {
            if (_gridQuad != null) _gridQuad.SetActive(false);
        }

        private void OnEnable()
        {
            if (_gridQuad != null && _showGrid) _gridQuad.SetActive(true);
        }

        private void OnDestroy()
        {
            if (_gridTexture != null) Destroy(_gridTexture);
            if (_gridQuad != null) Destroy(_gridQuad);
        }
    }
}