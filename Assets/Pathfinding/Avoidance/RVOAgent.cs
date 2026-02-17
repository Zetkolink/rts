using UnityEngine;

namespace RTS.Pathfinding.Avoidance
{
    /// <summary>
    /// Per-unit RVO agent. Registers with RVO2 Simulator,
    /// syncs position/velocity each frame, applies computed safe velocity.
    ///
    /// Workflow per frame:
    ///   1. [FixedUpdate early] Sync Unity position → RVO
    ///   2. [FixedUpdate early] Set preferred velocity from pathfinding
    ///   3. [FixedUpdate] RVOSimulatorBridge.doStep() computes safe velocities
    ///   4. [Update] Read computed velocity, move transform, check walkability
    /// </summary>
    [RequireComponent(typeof(UnitPathFollower))]
    public sealed class RVOAgent : MonoBehaviour
    {
        [Header("Agent Properties")]
        [SerializeField] private float _radius = 0.5f;
        [SerializeField] private float _maxSpeed = 5f;

        [Header("Tuning")]
        [Tooltip("Scale preferred velocity near destination to prevent oscillation")]
        [SerializeField] private float _arrivalSlowdownRadius = 1.5f;

        private UnitPathFollower _follower;
        private int _rvoId = -1;
        private Vector3 _computedVelocity;
        private bool _registered;

        // ── Public state ──
        public Vector3 ComputedVelocity => _computedVelocity;
        public float Radius => _radius;
        public bool IsRegistered => _registered;

        private void Awake()
        {
            _follower = GetComponent<UnitPathFollower>();
        }

        private void OnEnable()
        {
            TryRegister();
        }

        private void Start()
        {
            if (!_registered) TryRegister();
        }

        private void OnDisable()
        {
            // RVO2 doesn't support removing agents by ID.
            // Set agent to zero radius + zero speed to effectively disable it.
            if (_registered && _rvoId >= 0)
            {
                var sim = RVO.Simulator.Instance;
                sim.setAgentRadius(_rvoId, 0f);
                sim.setAgentMaxSpeed(_rvoId, 0f);
                sim.setAgentPrefVelocity(_rvoId, new RVO.Vector2(0f, 0f));
                _registered = false;
            }
        }

        private void OnEnable2ndPass()
        {
            // Re-enable a disabled agent
            if (_rvoId >= 0 && !_registered)
            {
                var sim = RVO.Simulator.Instance;
                sim.setAgentRadius(_rvoId, _radius);
                sim.setAgentMaxSpeed(_rvoId, _maxSpeed);
                _registered = true;
            }
        }

        private void TryRegister()
        {
            if (_registered) return;
            if (RVOSimulatorBridge.Instance == null) return;

            var sim = RVO.Simulator.Instance;
            var pos = ToRVO(transform.position);

            _rvoId = sim.addAgent(
                pos,
                RVOSimulatorBridge.Instance != null ? 5f : 5f, // neighborDist
                10,                                              // maxNeighbors
                2f,                                              // timeHorizon
                1f,                                              // timeHorizonObst
                _radius,
                _maxSpeed,
                new RVO.Vector2(0f, 0f)
            );

            _registered = true;
            Debug.Log($"[RVO] Agent registered: {gameObject.name}, id={_rvoId}");
        }

        /// <summary>
        /// Sync Unity state → RVO before doStep().
        /// Must run before RVOSimulatorBridge.FixedUpdate.
        /// </summary>
        private void FixedUpdate()
        {
            if (!_registered || _rvoId < 0) return;

            var sim = RVO.Simulator.Instance;

            sim.setAgentPosition(_rvoId, ToRVO(transform.position));

            RVO.Vector2 prefVel = new RVO.Vector2(0f, 0f);

            if (_follower.HasPath)
            {
                Vector3 toWaypoint = _follower.CurrentWaypoint - transform.position;
                toWaypoint.y = 0f;

                float distance = toWaypoint.magnitude;

                if (distance > 0.01f)
                {
                    Vector3 dir = toWaypoint / distance;
                    float speed = _maxSpeed * _follower.SpeedMultiplier;

                    if (distance < _arrivalSlowdownRadius)
                        speed *= distance / _arrivalSlowdownRadius;

                    Vector3 desired = dir * speed;
                    prefVel = ToRVO(desired);
                }
            }

            sim.setAgentPrefVelocity(_rvoId, prefVel);
        }

        /// <summary>
        /// Read computed velocity from RVO, apply to transform.
        /// </summary>
        private void Update()
        {
            if (!_registered || _rvoId < 0) return;

            if (!_follower.IsMoving && !_follower.HasPath)
            {
                _computedVelocity = Vector3.zero;
                return;
            }

            // Read safe velocity computed by doStep()
            RVO.Vector2 rvoVel = RVO.Simulator.Instance.getAgentVelocity(_rvoId);
            _computedVelocity = FromRVO(rvoVel);

            // Apply movement
            if (_computedVelocity.sqrMagnitude > 0.0001f)
            {
                Vector3 newPos = transform.position + _computedVelocity * Time.deltaTime;

                // Walkability check
                if (PathfindingAPI.Instance != null &&
                    PathfindingAPI.Instance.IsPositionWalkable(newPos))
                {
                    transform.position = newPos;
                }
                else
                {
                    // Try sliding along each axis separately
                    Vector3 slideX = transform.position +
                        new Vector3(_computedVelocity.x, 0f, 0f) * Time.deltaTime;
                    Vector3 slideZ = transform.position +
                        new Vector3(0f, 0f, _computedVelocity.z) * Time.deltaTime;

                    if (PathfindingAPI.Instance.IsPositionWalkable(slideX))
                        transform.position = slideX;
                    else if (PathfindingAPI.Instance.IsPositionWalkable(slideZ))
                        transform.position = slideZ;
                }
            }
        }

        // ── Coordinate conversion: Unity XZ plane ↔ RVO XY plane ──

        private static RVO.Vector2 ToRVO(Vector3 v)
        {
            return new RVO.Vector2(v.x, v.z);
        }

        private static Vector3 FromRVO(RVO.Vector2 v)
        {
            return new Vector3(v.x(), 0f, v.y());
        }

        // ───────────── Debug ─────────────

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Avoidance radius
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, _radius);

            if (!Application.isPlaying) return;

            // Preferred velocity (green)
            if (_follower != null && _follower.HasPath)
            {
                Vector3 toWp = _follower.CurrentWaypoint - transform.position;
                toWp.y = 0f;
                Gizmos.color = Color.green;
                Gizmos.DrawRay(transform.position + Vector3.up * 0.5f,
                    toWp.normalized * 2f);
            }

            // Computed RVO velocity (cyan)
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position + Vector3.up * 0.5f,
                _computedVelocity * 0.4f);
        }
#endif
    }
}