using UnityEngine;
using UnityEngine.InputSystem;
using RTS.Pathfinding.Debugging;

namespace RTS.Pathfinding
{
    public sealed class PathfindingBootstrap : MonoBehaviour
    {
        [Header("Debug Overlay")]
        [SerializeField] private PathfindingDebugOverlay _debugOverlay;

        [Header("Click to move all units")]
        [SerializeField] private LayerMask _groundMask;

        private UnitPathFollower[] _units;

        private void Start()
        {
            _units = FindObjectsByType<UnitPathFollower>(FindObjectsSortMode.None);
            Debug.Log($"[Test] Found {_units.Length} units");
        }

        private void Update()
        {
            if (Mouse.current == null) return;

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
                if (Physics.Raycast(ray, out RaycastHit hit, 500f, _groundMask))
                {
                    for (int i = 0; i < _units.Length; i++)
                    {
                        if (_units[i] != null)
                            _units[i].MoveTo(hit.point);
                    }

                    Debug.Log($"[Test] {_units.Length} units moving to {hit.point}");
                }
            }
        }
    }
}