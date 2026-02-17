using RTS.Pathfinding.Avoidance;
using UnityEngine;

namespace RTS.Pathfinding
{
    /// <summary>
    /// Follows a path using transform-based movement.
    /// Exposes read-only state for animation and other systems.
    /// No coupling to animation, combat, or AI — only moves transform.
    /// </summary>
    public sealed class UnitPathFollower : MonoBehaviour
    {
        [Header("Profile")]
        [Tooltip("Unit profile ScriptableObject. Overrides speed/rotation if assigned.")]
        [SerializeField] private UnitProfile _profile;

        [Header("Movement")] [SerializeField] private float _baseSpeed = 5f;
        [SerializeField] private float _waypointReachedThreshold = 0.15f;
        [SerializeField] private float _rotationSpeed = 720f;

        [Header("Repath")] [SerializeField] private int _stuckFrameThreshold = 30;

        private Vector3[] _waypoints;
        private int _currentWaypointIndex;
        private int _activeRequestId = -1;
        private bool _hasPath;
        private Vector3 _finalDestination;
        private Vector3 _lastMoveDirection;
        private ClearanceClass _activeClearance = ClearanceClass.Small;

        // Stuck detection
        private int _stuckFrames;
        private float _lastDistanceToWaypoint = float.MaxValue;
        private float _lastDistanceToFinal = float.MaxValue;
        private RTS.Pathfinding.Avoidance.RVOAgent _rvoAgent;
        private bool _hasRVO;
        private int _arrivalStuckFrames;
        private const int ArrivalStuckThreshold = 40;


        // ───────────── Public Read-Only State ─────────────

        /// <summary>Assigned UnitProfile. May be null if not set.</summary>
        public UnitProfile Profile => _profile;

        /// <summary>Effective base speed (profile overrides serialized value).</summary>
        public float BaseSpeed => _profile != null ? _profile.moveSpeed : _baseSpeed;

        /// <summary>Effective rotation speed (profile overrides serialized value).</summary>
        public float RotationSpeed => _profile != null ? _profile.rotationSpeed : _rotationSpeed;

        /// <summary>Current movement speed in world units/sec.</summary>
        public float CurrentSpeed { get; private set; }

        /// <summary>Speed normalized to [0..1] range. Use for blend trees.</summary>
        public float NormalizedSpeed => BaseSpeed > 0f ? CurrentSpeed / BaseSpeed : 0f;

        /// <summary>True if unit has a path and hasn't reached the end.</summary>
        public bool HasPath => _hasPath;

        /// <summary>True if unit is actively moving (has path + speed > 0).</summary>
        public bool IsMoving => _hasPath && CurrentSpeed > 0.01f;

        /// <summary>Final destination of current path.</summary>
        public Vector3 Destination => _finalDestination;

        /// <summary>Current waypoint the unit is moving toward.</summary>
        public Vector3 CurrentWaypoint =>
            _hasPath && _waypoints != null && _currentWaypointIndex < _waypoints.Length
                ? _waypoints[_currentWaypointIndex]
                : transform.position;

        /// <summary>
        /// External speed multiplier. Set by gameplay systems
        /// (buffs, slow effects, terrain penalty).
        /// </summary>
        public float SpeedMultiplier { get; set; } = 1f;

        /// <summary>The clearance class used for the current/last path request.</summary>
        public ClearanceClass ActiveClearance => _activeClearance;

        private void Awake()
        {
            _rvoAgent = GetComponent<RTS.Pathfinding.Avoidance.RVOAgent>();
            _hasRVO = _rvoAgent != null;

            // Apply profile defaults
            if (_profile != null)
            {
                _activeClearance = _profile.clearanceClass;
            }
        }

        // ───────────── Public API ─────────────

        /// <summary>
        /// Request unit to move to world position.
        /// Cancels any active path/request.
        /// Uses profile's clearance class by default.
        /// </summary>
        public void MoveTo(Vector3 destination)
        {
            ClearanceClass clearance = _profile != null
                ? _profile.clearanceClass
                : ClearanceClass.Small;
            MoveTo(destination, clearance);
        }

        /// <summary>
        /// Request unit to move to world position with explicit clearance class.
        /// Cancels any active path/request.
        /// </summary>
        public void MoveTo(Vector3 destination, ClearanceClass clearance)
        {
            Stop();
            _finalDestination = destination;
            _activeClearance = clearance;

            if (PathfindingAPI.Instance == null)
            {
                Debug.LogError("[UnitPathFollower] PathfindingAPI not found!");
                return;
            }

            _activeRequestId = PathfindingAPI.Instance.RequestPath(
                transform.position,
                destination,
                OnPathReceived,
                priority: 0f,
                clearance: _activeClearance
            );
        }

        /// <summary>
        /// Assign a pre-computed path directly.
        /// Useful for formations or shared paths.
        /// </summary>
        public void SetPath(Vector3[] waypoints, Vector3 destination)
        {
            Stop();

            if (waypoints == null || waypoints.Length == 0) return;

            _waypoints = waypoints;
            _currentWaypointIndex = 0;
            _hasPath = true;
            _finalDestination = destination;
            _lastDistanceToWaypoint = float.MaxValue;
            _stuckFrames = 0;
        }

        /// <summary>Stop movement and cancel pending requests.</summary>
        public void Stop()
        {
            if (_activeRequestId >= 0 && PathfindingAPI.Instance != null)
            {
                PathfindingAPI.Instance.CancelRequest(_activeRequestId);
            }

            _activeRequestId = -1;
            _hasPath = false;
            _waypoints = null;
            _currentWaypointIndex = 0;
            CurrentSpeed = 0f;
            _stuckFrames = 0;
            _lastDistanceToWaypoint = float.MaxValue;
            _arrivalStuckFrames = 0;
            _lastDistanceToFinal = float.MaxValue;
        }

        // ───────────── Internals ─────────────

        private void OnPathReceived(PathResult result)
        {
            _activeRequestId = -1;

            if (result.Status == PathStatus.Found || result.Status == PathStatus.Partial)
            {
                _waypoints = result.Waypoints;

                // Replace first waypoint with actual position — prevents
                // snapping to cell center at path start
                if (_waypoints.Length > 0)
                    _waypoints[0] = transform.position;

                _currentWaypointIndex = FindBestStartWaypoint(_waypoints);
                _hasPath = true;
                _lastDistanceToWaypoint = float.MaxValue;
                _stuckFrames = 0;
            }
            else
            {
                _hasPath = false;
                Debug.Log($"[UnitPathFollower] {gameObject.name}: path {result.Status}");
            }
        }

        /// <summary>
        /// Finds the best waypoint to start following from.
        /// Skips waypoints that are behind the unit's current position
        /// to prevent snapping backwards.
        /// </summary>
        private int FindBestStartWaypoint(Vector3[] waypoints)
        {
            if (waypoints.Length <= 1) return 0;

            Vector3 pos = transform.position;
            Vector3 forward = transform.forward;

            int bestIndex = 0;
            float bestScore = float.MaxValue;

            // Check first few waypoints — don't scan entire path
            int scanLimit = Mathf.Min(waypoints.Length, 4);

            for (int i = 0; i < scanLimit; i++)
            {
                Vector3 toWaypoint = waypoints[i] - pos;
                toWaypoint.y = 0f;

                float distance = toWaypoint.magnitude;
                float dot = Vector3.Dot(forward, toWaypoint.normalized);

                // Behind us (dot < 0) and close = bad, skip it
                // Ahead of us (dot > 0) and close = good, start here
                // Far away = probably a real waypoint, don't skip
                if (dot < 0f && distance < BaseSpeed * 0.5f)
                    continue;

                // Score: prefer close + ahead
                float score = distance - dot * 2f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private void Update()
        {
            if (!_hasPath || _waypoints == null || _waypoints.Length == 0)
            {
                CurrentSpeed = 0f;
                return;
            }

            Vector3 target = _waypoints[_currentWaypointIndex];
            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            // ── Stuck detection ──
            if (distance >= _lastDistanceToWaypoint - 0.01f)
            {
                _stuckFrames++;
                if (_stuckFrames >= _stuckFrameThreshold)
                {
                    Debug.Log($"[UnitPathFollower] {gameObject.name}: stuck, requesting repath");
                    _stuckFrames = 0;
                    Vector3 dest = _finalDestination;
                    MoveTo(dest, _activeClearance);
                    return;
                }
            }
            else
            {
                _stuckFrames = 0;
            }

            _lastDistanceToWaypoint = distance;

            // ── Waypoint reached ──
            bool isFinalWaypoint = _currentWaypointIndex >= _waypoints.Length - 1;

            if (isFinalWaypoint)
            {
                float distToFinal = distance;
                bool closeEnough = distToFinal <= _waypointReachedThreshold * 3f;

                // RVO pushing away from target — other units blocking
                bool rvoPushingAway = false;
                if (_hasRVO && _rvoAgent.ComputedVelocity.sqrMagnitude > 0.01f)
                {
                    Vector3 rvoDir = _rvoAgent.ComputedVelocity.normalized;
                    float dot = Vector3.Dot(rvoDir, toTarget.normalized);
                    rvoPushingAway = dot < 0.2f && distToFinal < BaseSpeed * 1.5f;
                }

                // Not making progress toward destination — area is full
                if (distToFinal >= _lastDistanceToFinal - 0.01f)
                {
                    _arrivalStuckFrames++;
                }
                else
                {
                    _arrivalStuckFrames = 0;
                }
                _lastDistanceToFinal = distToFinal;

                bool cantGetCloser = _arrivalStuckFrames >= ArrivalStuckThreshold;

                if (closeEnough || rvoPushingAway || cantGetCloser)
                {
                    _hasPath = false;
                    CurrentSpeed = 0f;
                    _waypoints = null;
                    _arrivalStuckFrames = 0;
                    _lastDistanceToFinal = float.MaxValue;
                    return;
                }
            }
            else
            {
                if (distance <= _waypointReachedThreshold)
                {
                    _currentWaypointIndex++;
                    _lastDistanceToWaypoint = float.MaxValue;
                    return;
                }
            }

            // ── Rotate ──
            Vector3 direction = toTarget / distance;
            Vector3 moveDir;

            if (_hasRVO && _rvoAgent.ComputedVelocity.sqrMagnitude > 0.1f)
                moveDir = _rvoAgent.ComputedVelocity.normalized;
            else if (!_hasRVO)
                moveDir = direction;
            else
                moveDir = Vector3.zero;

// Track last meaningful direction
            if (moveDir.sqrMagnitude > 0.01f)
                _lastMoveDirection = moveDir;

// Only rotate when actually moving
            if (_lastMoveDirection.sqrMagnitude > 0.01f && CurrentSpeed > 0.1f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(_lastMoveDirection);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    RotationSpeed * Time.deltaTime
                );
            }
        }

        private void OnDisable()
        {
            Stop();
        }

        // ───────────── Debug ─────────────

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!_hasPath || _waypoints == null) return;

            Gizmos.color = Color.cyan;
            for (int i = _currentWaypointIndex; i < _waypoints.Length - 1; i++)
            {
                Gizmos.DrawLine(
                    _waypoints[i] + Vector3.up * 0.3f,
                    _waypoints[i + 1] + Vector3.up * 0.3f
                );
            }

            Gizmos.color = Color.green;
            Gizmos.DrawSphere(CurrentWaypoint + Vector3.up * 0.3f, 0.2f);

            Gizmos.color = Color.red;
            Gizmos.DrawSphere(_finalDestination + Vector3.up * 0.3f, 0.25f);
        }
#endif
    }
}