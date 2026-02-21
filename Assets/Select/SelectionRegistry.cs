using System.Collections.Generic;
using UnityEngine;

namespace Select
{
    public static class SelectionRegistry
    {
        private static readonly List<Selectable> Items = new List<Selectable>(256);

        private static readonly Dictionary<Collider, Selectable> ColliderMap =
            new Dictionary<Collider, Selectable>(256);

        public static IReadOnlyList<Selectable> All => Items;
        public static int Count => Items.Count;

        public static void Register(Selectable selectable)
        {
            if (Items.Contains(selectable))
            {
                return;
            }

            Items.Add(selectable);
            Collider[] colliders = selectable.GetComponentsInChildren<Collider>();
            foreach (Collider collider in colliders)
                ColliderMap[collider] = selectable;
        }

        public static Selectable GetByCollider(Collider collider)
        {
            ColliderMap.TryGetValue(collider, out Selectable selectable);
            return selectable;
        }

        public static void Unregister(Selectable selectable)
        {
            Items.Remove(selectable);
        }

        public static void Clear()
        {
            Items.Clear();
            ColliderMap.Clear();
        }
    }
}