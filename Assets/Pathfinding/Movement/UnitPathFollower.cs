using Pathfinding.Avoidance;
using UnityEngine;
using UnityEngine.AI;

namespace Pathfinding.Movement
{
    /// <summary>
    /// Queries <see cref="NavMesh"/> for waypoints and follows them
    /// via direct transform manipulation. No NavMeshAgent — no per-agent overhead,
    /// full control over steering and speed.
    ///
    /// When an <see cref="RvoAgent"/> is present, this component does NOT move the
    /// transform directly. Instead it sets <see cref="RvoAgent.DesiredVelocity"/>
    /// and the agent handles collision-free movement. Rotation uses the RVO
    /// computed velocity for natural facing during avoidance maneuvers.
    ///
    /// Without RVOAgent, falls back to direct transform movement.
    /// </summary>
    public sealed class UnitPathFollower : MonoBehaviour
    {
        [Header("Movement")] [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float rotationSpeed = 720f;

        [Header("Path Following")] [Tooltip("Distance to waypoint before advancing to the next one.")] [SerializeField]
        private float waypointTolerance = 0.15f;

        [Tooltip("Distance to final destination to consider arrival.")] [SerializeField]
        private float arrivalTolerance = 0.1f;

        [Header("NavMesh Query")] [Tooltip("Max distance to snap start/end positions onto NavMesh.")] [SerializeField]
        private float sampleDistance = 5f;

        [SerializeField] private int areaMask = NavMesh.AllAreas;

        // ---- Public read-only state (for animation, UI, etc.) ----

        public Vector3 Velocity { get; private set; }
        public float NormalizedSpeed { get; private set; }
        public bool IsMoving { get; private set; }
        public bool HasPath => _hasPath;

        private NavMeshPath _path;
        private readonly Vector3[] _corners = new Vector3[64];
        private int _cornerCount;
        private int _currentCorner;
        private bool _hasPath;

        private RvoAgent _rvoAgent;

        private void OnEnable()
        {
            _path = new NavMeshPath();
            _rvoAgent = GetComponent<RvoAgent>();
        }

        private void OnDisable()
        {
            Stop();
            _path = null;
            _rvoAgent = null;
        }

        // ---- Public API ----

        public bool SetDestination(Vector3 destination)
        {
            if (_path == null)
                return false;

            Stop();

            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit startHit, sampleDistance, areaMask))
                return false;

            if (!NavMesh.SamplePosition(destination, out NavMeshHit endHit, sampleDistance, areaMask))
                return false;

            if (!NavMesh.CalculatePath(startHit.position, endHit.position, areaMask, _path))
                return false;

            if (_path.status == NavMeshPathStatus.PathInvalid)
                return false;

            _cornerCount = _path.GetCornersNonAlloc(_corners);

            if (_cornerCount < 2)
                return false;

            _currentCorner = 1;
            _hasPath = true;
            IsMoving = true;

            return true;
        }

        void Stop()
        {
            _hasPath = false;
            _cornerCount = 0;
            _currentCorner = 0;
            IsMoving = false;
            Velocity = Vector3.zero;
            NormalizedSpeed = 0f;
            _rvoAgent.DesiredVelocity = Vector3.zero;
        }

        // ---- Movement ----

        private void Update()
        {
            if (!_hasPath)
            {
                _rvoAgent.DesiredVelocity = Vector3.zero;
                return;
            }

            Vector3 position = transform.position;
            Vector3 target = _corners[_currentCorner];

            Vector3 toTarget = target - position;
            toTarget.y = 0f;
            float distSqr = toTarget.sqrMagnitude;

            bool isFinalCorner = _currentCorner >= _cornerCount - 1;
            float tolerance = isFinalCorner ? arrivalTolerance : waypointTolerance;

            if (distSqr <= tolerance * tolerance)
            {
                if (isFinalCorner)
                {
                    Arrive();
                    return;
                }

                toTarget = AdvanceWaypoint();
            }

            SetDesiredVelocity(toTarget);
        }

        private void Rotate(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.001f)
                return;

            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }

        private void SetDesiredVelocity(Vector3 toTarget)
        {
            Vector3 desiredDirection = toTarget.normalized;
            Vector3 desiredVelocity = desiredDirection * moveSpeed;

            _rvoAgent.DesiredVelocity = desiredVelocity;
            Vector3 facing = _rvoAgent.ComputedVelocity;
            if (facing.sqrMagnitude > 0.01f)
                Rotate(facing);

            Velocity = _rvoAgent.ComputedVelocity;
            NormalizedSpeed = Velocity.magnitude / moveSpeed;
        }

        private Vector3 AdvanceWaypoint()
        {
            _currentCorner++;
            Vector3 target = _corners[_currentCorner];
            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;
            return toTarget;
        }

        private void Arrive()
        {
            Vector3 final3 = _corners[_cornerCount - 1];
            Vector3 pos = transform.position;
            pos.x = final3.x;
            pos.z = final3.z;
            transform.position = pos;

            Stop();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!_hasPath || _cornerCount == 0)
                return;

            Gizmos.color = Color.yellow;
            for (int i = 0; i < _cornerCount - 1; i++)
                Gizmos.DrawLine(_corners[i], _corners[i + 1]);

            Gizmos.color = Color.green;
            for (int i = 0; i < _cornerCount; i++)
                Gizmos.DrawSphere(_corners[i], 0.1f);

            if (_currentCorner < _cornerCount)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(_corners[_currentCorner], 0.15f);
            }
        }
#endif
    }
}