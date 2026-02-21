using System;
using UnityEngine;

namespace Select
{
    public class Selectable : MonoBehaviour
    {
        public event Action<bool> SelectionChanged;
        bool IsSelected { get; set; }
        public Vector3 ScreenPosition { get; set; }

        private void OnEnable()
        {
            SelectionRegistry.Register(this);
        }

        private void OnDisable()
        {
            SelectionRegistry.Unregister(this);
            if (IsSelected)
                SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            if (IsSelected == selected)
                return;

            IsSelected = selected;
            SelectionChanged?.Invoke(selected);
        }
    }
}