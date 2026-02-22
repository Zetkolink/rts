using UnityEngine;

namespace Select
{
    /// <summary>
    /// Static shared data written by <see cref="SelectionInputHandler"/> (MonoBehaviour),
    /// read by <see cref="ScreenSpaceSelectionSystem"/> (ECS).
    ///
    /// Holds the current frame's selection action (click, box, or none).
    /// Consumed once per frame by ECS, then cleared.
    /// </summary>
    public static class SelectionInput
    {
        public enum ActionType : byte
        {
            None,
            Click,
            Box
        }

        /// <summary>Current action this frame.</summary>
        public static ActionType Action;

        /// <summary>Screen position for click, or box start corner.</summary>
        public static Vector2 ScreenPointA;

        /// <summary>Box end corner (only used for Box action).</summary>
        public static Vector2 ScreenPointB;

        /// <summary>True if Shift is held (additive selection).</summary>
        public static bool Additive;

        /// <summary>True while dragging a box (for visual feedback in MonoBehaviour).</summary>
        public static bool IsDragging;

        /// <summary>Current drag rectangle in screen space (for OnGUI drawing).</summary>
        public static Rect DragRect;

        public static void Clear()
        {
            Action = ActionType.None;
        }
    }
}