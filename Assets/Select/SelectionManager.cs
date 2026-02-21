using System;
using System.Collections.Generic;
using UnityEngine;

namespace Select
{
    public sealed class SelectionManager : MonoBehaviour
    {
        public event Action SelectionChanged;

        private readonly HashSet<Selectable> _selected = new HashSet<Selectable>();
        private readonly List<Selectable> _selectedList = new List<Selectable>(64);
        private bool _listDirty;

        // ---- Public read-only access ----

        public int Count => _selected.Count;

        /// <summary>
        /// Materialized list of selected units. Rebuilt lazily when selection changes.
        /// Safe to iterate — returns a snapshot that won't throw during enumeration.
        /// </summary>
        public IReadOnlyList<Selectable> Selected
        {
            get
            {
                if (_listDirty)
                    RebuildList();
                return _selectedList;
            }
        }

        public bool IsSelected(Selectable selectable) => _selected.Contains(selectable);

        // ---- Mutation API (called by SelectionInputHandler) ----

        public void Toggle(Selectable selectable)
        {
            if (_selected.Contains(selectable))
                Deselect(selectable);
            else
                Select(selectable);
        }

        public void DeselectAll()
        {
            if (_selected.Count == 0)
                return;

            foreach (Selectable s in _selected)
                s.SetSelected(false);

            _selected.Clear();
            MarkDirty();
        }

        /// <summary>
        /// Replace entire selection with a new set (used after box-select).
        /// Minimizes event noise — only fires once at the end.
        /// </summary>
        public void SetSelection(List<Selectable> newSelection)
        {
            Deselect();
            Select(newSelection);
            MarkDirty();
        }

        public void Select(Selectable selectable)
        {
            if (!_selected.Add(selectable))
                return;

            selectable.SetSelected(true);
            MarkDirty();
        }
        
        void Deselect(Selectable selectable)
        {
            if (!_selected.Remove(selectable))
                return;

            selectable.SetSelected(false);
            MarkDirty();
        }

        void Select(List<Selectable> additions)
        {
            foreach (Selectable s in additions)
            {
                _selected.Add(s);
                s.SetSelected(true);
            }
        }
        
        void Deselect()
        {
            foreach (Selectable s in _selected)
                s.SetSelected(false);

            _selected.Clear();
        }

        /// <summary>
        /// Add a batch to the current selection (Shift + box-drag).
        /// </summary>
        public void AddToSelection(List<Selectable> additions)
        {
            bool changed = false;
            additions.ForEach(delegate(Selectable s)
            {
                if (!_selected.Add(s))
                    return;

                s.SetSelected(true);
                changed = true;
            });

            if (changed)
                MarkDirty();
        }

        // ---- Internal ----

        private void MarkDirty()
        {
            _listDirty = true;
            SelectionChanged?.Invoke();
        }

        private void RebuildList()
        {
            _selectedList.Clear();
            foreach (Selectable s in _selected)
                _selectedList.Add(s);
            _listDirty = false;
        }
    }
}