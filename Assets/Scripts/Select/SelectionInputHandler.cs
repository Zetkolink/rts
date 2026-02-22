using UnityEngine;
using UnityEngine.InputSystem;

namespace Select
{
    /// <summary>
    /// Captures mouse input for unit selection and writes to <see cref="SelectionInput"/>.
    /// Draws the box-selection rectangle with OnGUI.
    ///
    /// Place on a GameObject outside the SubScene (with Camera reference).
    /// All actual selection logic is in <see cref="ScreenSpaceSelectionSystem"/> (ECS).
    /// </summary>
    public sealed class SelectionInputHandler : MonoBehaviour
    {
        [SerializeField] private float dragThreshold = 5f;
        [SerializeField] private Color boxColor = new Color(0.8f, 0.8f, 0.95f, 0.25f);
        [SerializeField] private Color boxBorderColor = new Color(0.8f, 0.8f, 0.95f, 0.8f);

        private Vector2 _dragStart;
        private bool _isDragging;
        private bool _mouseDown;

        private static Texture2D _boxTexture;
        private static Texture2D _borderTexture;

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return;

            Vector2 mousePos = mouse.position.ReadValue();
            bool shift = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

            // LMB pressed.
            if (mouse.leftButton.wasPressedThisFrame)
            {
                _dragStart = mousePos;
                _mouseDown = true;
                _isDragging = false;
            }

            // Dragging.
            if (_mouseDown && !_isDragging)
            {
                if (Vector2.Distance(_dragStart, mousePos) > dragThreshold)
                    _isDragging = true;
            }

            // Update drag rect for visual.
            if (_isDragging)
            {
                SelectionInput.IsDragging = true;
                SelectionInput.DragRect = MakeScreenRect(_dragStart, mousePos);
            }

            // LMB released.
            if (mouse.leftButton.wasReleasedThisFrame && _mouseDown)
            {
                _mouseDown = false;
                SelectionInput.Additive = shift;

                if (_isDragging)
                {
                    // Box select.
                    SelectionInput.Action = SelectionInput.ActionType.Box;
                    SelectionInput.ScreenPointA = _dragStart;
                    SelectionInput.ScreenPointB = mousePos;
                }
                else
                {
                    // Click select.
                    SelectionInput.Action = SelectionInput.ActionType.Click;
                    SelectionInput.ScreenPointA = mousePos;
                }

                _isDragging = false;
                SelectionInput.IsDragging = false;
            }
        }

        private void OnGUI()
        {
            if (!_isDragging)
                return;

            EnsureTextures();

            Rect screenRect = SelectionInput.DragRect;

            // Flip Y for OnGUI (screen Y is bottom-up, GUI Y is top-down).
            float guiY = Screen.height - screenRect.y - screenRect.height;
            Rect guiRect = new Rect(screenRect.x, guiY, screenRect.width, screenRect.height);

            // Fill.
            GUI.DrawTexture(guiRect, _boxTexture);

            // Border.
            float b = 1f;
            GUI.DrawTexture(new Rect(guiRect.x, guiRect.y, guiRect.width, b), _borderTexture);
            GUI.DrawTexture(new Rect(guiRect.x, guiRect.yMax - b, guiRect.width, b), _borderTexture);
            GUI.DrawTexture(new Rect(guiRect.x, guiRect.y, b, guiRect.height), _borderTexture);
            GUI.DrawTexture(new Rect(guiRect.xMax - b, guiRect.y, b, guiRect.height), _borderTexture);
        }

        private void EnsureTextures()
        {
            if (_boxTexture == null)
            {
                _boxTexture = new Texture2D(1, 1);
                _boxTexture.SetPixel(0, 0, boxColor);
                _boxTexture.Apply();
            }

            if (_borderTexture == null)
            {
                _borderTexture = new Texture2D(1, 1);
                _borderTexture.SetPixel(0, 0, boxBorderColor);
                _borderTexture.Apply();
            }
        }

        /// <summary>
        /// Returns a screen-space rect from two corners, ensuring positive width/height.
        /// </summary>
        private static Rect MakeScreenRect(Vector2 a, Vector2 b)
        {
            float x = Mathf.Min(a.x, b.x);
            float y = Mathf.Min(a.y, b.y);
            float w = Mathf.Abs(a.x - b.x);
            float h = Mathf.Abs(a.y - b.y);
            return new Rect(x, y, w, h);
        }
    }
}