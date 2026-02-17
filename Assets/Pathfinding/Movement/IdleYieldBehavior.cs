using UnityEngine;

namespace RTS.Pathfinding
{
    /// <summary>
    /// Makes idle units step aside when a moving unit approaches.
    /// "Moving has right of way over idle" — similar to SC2/AoE2 behavior.
    ///
    /// When idle, checks for nearby moving units heading toward us.
    /// If found, sidesteps perpendicular to their path, then returns
    /// to original position after a delay (if still free).
    ///
    /// Requirements:
    /// - UnitPathFollower on same GameObject
    /// - UnitProfile assigned (for pushPriority and radius)
    /// </summary>
    [RequireComponent(typeof(UnitPathFollower))]
    public sealed class IdleYieldBehavior : MonoBehaviour
    {
        [Header("Detection")]
        [Tooltip("How far to look for approaching moving units.")]
        [SerializeField] private float _detectionRadius = 3f;

        [Tooltip("Minimum dot product of incoming unit's velocity toward us to trigger yield.")]
        [SerializeField] private float _approachDotThreshold = 0.3f;

        [Header("Yield Movement")]
        [Tooltip("How far to step aside (multiplier on own radius).")]
        [SerializeField] private float _yieldDistanceMultiplier = 3f;

        [Tooltip("Speed of the yield sidestep (world units/sec).")]
        [SerializeField] private float _yieldSpeed = 6f;

        [Tooltip("Seconds to wait before returning to original position.")]
        [SerializeField] private float _returnDelay = 2f;

        [Tooltip("Minimum seconds between yield triggers.")]
        [SerializeField] private float _yieldCooldown = 1f;

        private UnitPathFollower _follower;
        private UnitProfile _profile;

        private enum YieldState { Idle, Yielding, Waiting, Returning }
        private YieldState _state = YieldState.Idle;

        private Vector3 _originalPosition;
        private Vector3 _yieldTarget;
        private float _waitTimer;
        private float _cooldownTimer;

        private void Awake()
        {
            _follower = GetComponent<UnitPathFollower>();
        }

        private void Start()
        {
            _profile = _follower.Profile;
        }

        private void Update()
        {
            // Cooldown tick
            if (_cooldownTimer > 0f)
                _cooldownTimer -= Time.deltaTime;

            switch (_state)
            {
                case YieldState.Idle:
                    UpdateIdle();
                    break;
                case YieldState.Yielding:
                    UpdateYielding();
                    break;
                case YieldState.Waiting:
                    UpdateWaiting();
                    break;
                case YieldState.Returning:
                    UpdateReturning();
                    break;
            }
        }

        private void UpdateIdle()
        {
            // Only yield if we are truly idle (not moving, no path)
            if (_follower.HasPath || _follower.IsMoving) return;
            if (_cooldownTimer > 0f) return;

            float myRadius = _profile != null ? _profile.radius : 0.5f;
            int myPriority = _profile != null ? _profile.pushPriority : 1;

            // Check for nearby moving units
            var colliders = Physics.OverlapSphere(transform.position, _detectionRadius);

            for (int i = 0; i < colliders.Length; i++)
            {
                var other = colliders[i].GetComponentInParent<UnitPathFollower>();
                if (other == null || other == _follower) continue;
                if (!other.IsMoving) continue;

                // Check push priority: only yield to units with >= priority
                var otherProfile = other.Profile;
                int otherPriority = otherProfile != null ? otherProfile.pushPriority : 1;
                bool otherCanPush = otherProfile != null ? otherProfile.canPushIdle : true;

                if (!otherCanPush) continue;
                if (otherPriority < myPriority) continue;

                // Is the other unit heading toward us?
                Vector3 toMe = transform.position - other.transform.position;
                toMe.y = 0f;
                float distToMe = toMe.magnitude;
                if (distToMe < 0.01f) continue;

                Vector3 otherDir = other.CurrentWaypoint - other.transform.position;
                otherDir.y = 0f;
                if (otherDir.sqrMagnitude < 0.01f) continue;

                float dot = Vector3.Dot(otherDir.normalized, toMe.normalized);
                if (dot < _approachDotThreshold) continue;

                // Compute perpendicular yield direction (prefer right side of their path)
                Vector3 approachDir = otherDir.normalized;
                Vector3 perpendicular = new Vector3(approachDir.z, 0f, -approachDir.x);

                // Choose the side that moves us away from the other unit's path
                Vector3 sideOffset = toMe - Vector3.Dot(toMe, approachDir) * approachDir;
                if (Vector3.Dot(sideOffset, perpendicular) < 0f)
                    perpendicular = -perpendicular;

                float yieldDist = myRadius * _yieldDistanceMultiplier;
                Vector3 target = transform.position + perpendicular * yieldDist;

                // Validate the yield target is walkable
                if (PathfindingAPI.Instance != null &&
                    !PathfindingAPI.Instance.IsPositionWalkable(target))
                {
                    // Try the other side
                    target = transform.position - perpendicular * yieldDist;
                    if (!PathfindingAPI.Instance.IsPositionWalkable(target))
                        continue; // Neither side works, don't yield
                }

                // Start yielding
                _originalPosition = transform.position;
                _yieldTarget = target;
                _state = YieldState.Yielding;
                _cooldownTimer = _yieldCooldown;
                return;
            }
        }

        private void UpdateYielding()
        {
            // If pathfinding gives us a new path, abort yield
            if (_follower.HasPath)
            {
                _state = YieldState.Idle;
                return;
            }

            Vector3 toTarget = _yieldTarget - transform.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;

            if (dist < 0.1f)
            {
                transform.position = _yieldTarget;
                _state = YieldState.Waiting;
                _waitTimer = _returnDelay;
                return;
            }

            // Move toward yield target via transform (no pathfinding)
            Vector3 step = toTarget.normalized * _yieldSpeed * Time.deltaTime;
            if (step.magnitude > dist)
                step = toTarget;

            Vector3 newPos = transform.position + step;

            // Walkability check
            if (PathfindingAPI.Instance != null &&
                PathfindingAPI.Instance.IsPositionWalkable(newPos))
            {
                transform.position = newPos;
            }
            else
            {
                // Can't move further, wait here
                _state = YieldState.Waiting;
                _waitTimer = _returnDelay;
            }
        }

        private void UpdateWaiting()
        {
            // If pathfinding gives us a new path, abort return
            if (_follower.HasPath)
            {
                _state = YieldState.Idle;
                return;
            }

            _waitTimer -= Time.deltaTime;
            if (_waitTimer <= 0f)
                _state = YieldState.Returning;
        }

        private void UpdateReturning()
        {
            // If pathfinding gives us a new path, abort return
            if (_follower.HasPath)
            {
                _state = YieldState.Idle;
                return;
            }

            Vector3 toOriginal = _originalPosition - transform.position;
            toOriginal.y = 0f;
            float dist = toOriginal.magnitude;

            if (dist < 0.1f)
            {
                transform.position = _originalPosition;
                _state = YieldState.Idle;
                return;
            }

            Vector3 step = toOriginal.normalized * _yieldSpeed * Time.deltaTime;
            if (step.magnitude > dist)
                step = toOriginal;

            Vector3 newPos = transform.position + step;

            if (PathfindingAPI.Instance != null &&
                PathfindingAPI.Instance.IsPositionWalkable(newPos))
            {
                transform.position = newPos;
            }
            else
            {
                // Can't return, stay here
                _state = YieldState.Idle;
            }
        }

        // ───────────── Debug ─────────────

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;

            switch (_state)
            {
                case YieldState.Yielding:
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawLine(transform.position + Vector3.up * 0.5f,
                        _yieldTarget + Vector3.up * 0.5f);
                    Gizmos.DrawSphere(_yieldTarget + Vector3.up * 0.5f, 0.15f);
                    break;
                case YieldState.Waiting:
                    Gizmos.color = new Color(1f, 0.5f, 0f);
                    Gizmos.DrawSphere(transform.position + Vector3.up * 1f, 0.1f);
                    break;
                case YieldState.Returning:
                    Gizmos.color = Color.green;
                    Gizmos.DrawLine(transform.position + Vector3.up * 0.5f,
                        _originalPosition + Vector3.up * 0.5f);
                    break;
            }
        }
#endif
    }
}
