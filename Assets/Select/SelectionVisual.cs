using UnityEngine;

namespace Select
{
    [RequireComponent(typeof(Selectable))]
    public sealed class SelectionVisual : MonoBehaviour
    {
        [SerializeField] private GameObject selectionIndicator;

        private Selectable _selectable;

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();

            if (selectionIndicator != null)
                selectionIndicator.SetActive(false);
        }

        private void OnEnable()
        {
            _selectable.SelectionChanged += OnSelectionChanged;
        }

        private void OnDisable()
        {
            _selectable.SelectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged(bool selected)
        {
            selectionIndicator?.SetActive(selected);
        }
    }
}