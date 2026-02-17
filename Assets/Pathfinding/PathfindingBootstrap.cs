using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using RTS.Pathfinding.Debugging;

namespace RTS.Pathfinding
{
    public sealed class PathfindingBootstrap : MonoBehaviour
    {
        [Header("Debug Overlay")]
        [SerializeField] private PathfindingDebugOverlay _debugOverlay;

        [Header("Click to move units")]
        [SerializeField] private LayerMask _groundMask;

        private UnitPathFollower[] _groupLeftClick;
        private UnitPathFollower[] _groupRightClick;

        private void Start()
        {
            var all = FindObjectsByType<UnitPathFollower>(FindObjectsSortMode.None);
            Debug.Log($"[Test] Found {all.Length} units");

            var left = new List<UnitPathFollower>(all.Length / 2 + 1);
            var right = new List<UnitPathFollower>(all.Length / 2 + 1);

            for (int i = 0; i < all.Length; i++)
            {
                var u = all[i];
                if (u == null) continue;

                if ((i & 1) == 0) left.Add(u);
                else right.Add(u);
            }

            _groupLeftClick = left.ToArray();
            _groupRightClick = right.ToArray();

            Debug.Log($"[Test] Groups: LMB={_groupLeftClick.Length}, RMB={_groupRightClick.Length}");
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            // ЛКМ -> группа 1
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (TryGetGroundPoint(out var point))
                {
                    MoveGroup(_groupLeftClick, point);
                    Debug.Log($"[Test] LMB group({_groupLeftClick.Length}) moving to {point}");
                }
            }

            // ПКМ -> группа 2
            if (mouse.rightButton.wasPressedThisFrame)
            {
                if (TryGetGroundPoint(out var point))
                {
                    MoveGroup(_groupRightClick, point);
                    Debug.Log($"[Test] RMB group({_groupRightClick.Length}) moving to {point}");
                }
            }
        }

        private bool TryGetGroundPoint(out Vector3 point)
        {
            point = default;

            var cam = Camera.main;
            if (cam == null) return false;

            var mouse = Mouse.current;
            if (mouse == null) return false;

            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, 500f, _groundMask)) return false;

            point = hit.point;
            return true;
        }

        private static void MoveGroup(UnitPathFollower[] group, Vector3 destination)
        {
            if (group == null) return;

            for (int i = 0; i < group.Length; i++)
            {
                var u = group[i];
                if (u != null) u.MoveTo(destination);
            }
        }
    }
}
