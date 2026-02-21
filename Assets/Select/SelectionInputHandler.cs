using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Select
{
    public sealed class SelectionInputHandler : MonoBehaviour
    {
        [Header("References")] [SerializeField]
        private SelectionManager selectionManager;

        [SerializeField] private Camera cameraController;

        [Header("Layers")] [SerializeField] private LayerMask selectableLayer; // Units
        [SerializeField] private LayerMask groundLayer;

        [Header("Tuning")] [Tooltip("Minimum drag distance in pixels before box-selection activates.")] [SerializeField]
        private float dragThreshold = 5f;

        [Header("Box Visual")] [SerializeField]
        private Color boxBorderColor = new Color(0.3f, 0.8f, 0.3f, 1f);

        [SerializeField] private Color boxFillColor = new Color(0.3f, 0.8f, 0.3f, 0.15f);

        // ---- State ----
        private Vector2 _mouseDownScreenPos;
        private bool _isDragging;
        private bool _boxSelectionActive;
        private Rect _selectionRect;

        // Reusable buffer to avoid per-frame allocations during box-select.
        private readonly List<Selectable> _boxBuffer = new List<Selectable>(128);

        // Cached texture for OnGUI drawing (1x1 white pixel).
        private Texture2D _whiteTexture;

        private void Awake()
        {
            _whiteTexture = new Texture2D(1, 1);
            _whiteTexture.SetPixel(0, 0, Color.white);
            _whiteTexture.Apply();
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            HandleMouseDown(mouse);
            HandleMouseDrag(mouse);
            HandleMouseUp(mouse);
        }

        // ================================================================
        //  Input phases
        // ================================================================

        // ReSharper disable Unity.PerformanceAnalysis
        private void HandleMouseDown(Mouse mouse)
        {
            if (!mouse.leftButton.wasPressedThisFrame)
                return;

            _mouseDownScreenPos = mouse.position.ReadValue();
            _isDragging = false;
            _boxSelectionActive = false;
            Debug.Log("1232222ww223322");
        }

        // ReSharper disable Unity.PerformanceAnalysis
        private void HandleMouseDrag(Mouse mouse)
        {
            if (!mouse.leftButton.isPressed)
                return;

            Vector2 current = mouse.position.ReadValue();
            float distance = Vector2.Distance(_mouseDownScreenPos, current);

            if (distance >= dragThreshold)
            {
                _isDragging = true;
                _boxSelectionActive = true;
                _selectionRect = ScreenRectFromTwoPoints(_mouseDownScreenPos, current);
            }
        }

        // ReSharper disable Unity.PerformanceAnalysis
        private void HandleMouseUp(Mouse mouse)
        {
            if (!mouse.leftButton.wasReleasedThisFrame)
                return;

            Keyboard keyboard = Keyboard.current;
            bool isShift = keyboard != null &&
                           (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);

            if (_isDragging && _boxSelectionActive)
            {
                PerformBoxSelection(isShift);
            }
            else
            {
                PerformClickSelection(mouse, isShift);
            }

            _isDragging = false;
            _boxSelectionActive = false;
        }

        // ================================================================
        //  Click selection (raycast)
        // ================================================================

        private void PerformClickSelection(Mouse mouse, bool additive)
        {
            Vector2 mousePos = mouse.position.ReadValue();
            Ray ray = cameraController.ScreenPointToRay(mousePos);

            // Try to hit a selectable unit first.
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, selectableLayer))
            {
                Selectable selectable = SelectionRegistry.GetByCollider(hit.collider);
                if (!ReferenceEquals(selectable, null))
                {
                    if (additive)
                    {
                        selectionManager.Toggle(selectable);
                    }
                    else
                    {
                        selectionManager.DeselectAll();
                        selectionManager.Select(selectable);
                    }

                    return;
                }
            }

            // Hit ground or nothing — deselect all (unless Shift is held).
            if (!additive)
            {
                selectionManager.DeselectAll();
            }
        }

        // ================================================================
        //  Box selection (screen-space bounds check)
        // ================================================================

        private void PerformBoxSelection(bool additive)
        {
            _boxBuffer.Clear();

            IReadOnlyList<Selectable> all = SelectionRegistry.All;
            int count = SelectionRegistry.Count;

            for (int i = 0; i < count; i++)
            {
                Selectable s = all[i];
                Vector3 screenPos = cameraController.WorldToScreenPoint(s.transform.position);

                // Behind camera — skip.
                if (screenPos.z < 0f)
                    continue;

                // Convert to GUI-space Y (OnGUI uses top-left origin, same as our Rect).
                Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);

                if (_selectionRect.Contains(guiPos))
                    _boxBuffer.Add(s);
            }

            if (additive)
                selectionManager.AddToSelection(_boxBuffer);
            else
                selectionManager.SetSelection(_boxBuffer);
        }

        // ================================================================
        //  Selection rectangle drawing (OnGUI — no Canvas needed)
        // ================================================================

        private void OnGUI()
        {
            if (!_boxSelectionActive || !_isDragging)
                return;

            // Fill
            GUI.color = boxFillColor;
            GUI.DrawTexture(_selectionRect, _whiteTexture);

            // Border (four thin rects)
            GUI.color = boxBorderColor;
            const float b = 2f; // border thickness
            GUI.DrawTexture(new Rect(_selectionRect.x, _selectionRect.y, _selectionRect.width, b),
                _whiteTexture); // top
            GUI.DrawTexture(new Rect(_selectionRect.x, _selectionRect.yMax - b, _selectionRect.width, b),
                _whiteTexture); // bottom
            GUI.DrawTexture(new Rect(_selectionRect.x, _selectionRect.y, b, _selectionRect.height),
                _whiteTexture); // left
            GUI.DrawTexture(new Rect(_selectionRect.xMax - b, _selectionRect.y, b, _selectionRect.height),
                _whiteTexture); // right
        }

        // ================================================================
        //  Utility
        // ================================================================

        /// <summary>
        /// Builds a Rect from two screen points. Handles any drag direction.
        /// Converts to GUI-space (Y-flipped) for OnGUI and Contains() checks.
        /// </summary>
        private static Rect ScreenRectFromTwoPoints(Vector2 a, Vector2 b)
        {
            // Flip Y to GUI space (origin top-left).
            a.y = Screen.height - a.y;
            b.y = Screen.height - b.y;

            float xMin = Mathf.Min(a.x, b.x);
            float xMax = Mathf.Max(a.x, b.x);
            float yMin = Mathf.Min(a.y, b.y);
            float yMax = Mathf.Max(a.y, b.y);

            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }
    }
}