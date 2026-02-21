using UnityEngine;
using Pathfinding.Avoidance.RVO;

namespace Pathfinding.Avoidance
{
    /// <summary>
    /// Per-unit bridge to RVO2 library.
    /// Registers/unregisters with <see cref="RVO.Simulator"/>,
    /// syncs Unity position ↔ RVO position, feeds preferred velocity,
    /// reads computed (collision-free) velocity and applies movement.
    ///
    /// <see cref="Pathfinding.Movement.UnitPathFollower"/> sets <see cref="DesiredVelocity"/>.
    /// This component handles the rest.
    ///
    /// Coordinate mapping: Unity XZ → RVO XY.
    /// </summary>
    public sealed class RvoAgent : MonoBehaviour
    {
        [Header("Agent")] [SerializeField] private float radius = 0.5f;
        [SerializeField] private float maxSpeed = 5f;

        [Header("Avoidance")] [SerializeField] private float neighborDist = 5f;
        [SerializeField] [Range(1, 20)] private int maxNeighbors = 10;
        [SerializeField] private float timeHorizon = 2f;
        [SerializeField] private float timeHorizonObst = 1f;

        /// <summary>
        /// Set by <see cref="Pathfinding.Movement.UnitPathFollower"/> each frame.
        /// Direction × speed the unit WANTS to go (Unity world-space, Y ignored).
        /// </summary>
        public Vector3 DesiredVelocity { get; set; }

        /// <summary>
        /// Velocity computed by RVO after collision avoidance (Unity world-space).
        /// Read by <see cref="Pathfinding.Movement.UnitPathFollower"/> for rotation facing and by external systems.
        /// </summary>
        public Vector3 ComputedVelocity { get; private set; }

        public bool IsRegistered => _agentId >= 0;

        private int _agentId = -1;

        // ───────────── Lifecycle ─────────────

        private void OnEnable()
        {
            Register();
        }

        private void OnDisable()
        {
            Unregister();
        }

        // ───────────── Sync (runs every Update) ─────────────

        /// <summary>
        /// Call order per frame:
        /// 1. UnitPathFollower.Update() → sets DesiredVelocity, does NOT move transform
        /// 2. RVOAgent.LateUpdate() → syncs position to RVO, sets prefVelocity,
        ///    reads last step's computed velocity, applies movement
        /// 3. RVOSimulatorBridge.FixedUpdate() → doStep() uses the prefVelocity we just set
        ///
        /// One physics-step delay on avoidance is imperceptible for RTS.
        /// </summary>
        private void LateUpdate()
        {
            if (_agentId < 0)
                return;

            var sim = RVO.Simulator.Instance;

            // 1. Read computed velocity from LAST doStep().
            RVO.Vector2 rvoVel = sim.getAgentVelocity(_agentId);
            ComputedVelocity = new Vector3(rvoVel.x(), 0f, rvoVel.y());

            // 2. Apply movement.
            transform.position += ComputedVelocity * Time.deltaTime;

            // 3. Sync current Unity position → RVO (after we moved).
            Vector3 pos = transform.position;
            sim.setAgentPosition(_agentId, new RVO.Vector2(pos.x, pos.z));

            // 4. Feed desired velocity for NEXT doStep().
            sim.setAgentPrefVelocity(_agentId,
                new RVO.Vector2(DesiredVelocity.x, DesiredVelocity.z));
        }

        // ───────────── Registration ─────────────

        private void Register()
        {
            if (_agentId >= 0)
                return;

            if (RvoSimulatorBridge.Instance == null || !RvoSimulatorBridge.Instance.IsInitialized)
            {
                Debug.LogWarning($"[RVOAgent] {gameObject.name}: RVOSimulatorBridge not ready. " +
                                 "Ensure it is active and runs before this component.");
                return;
            }

            Vector3 pos = transform.position;

            Debug.Log($"[RVOAgent] {gameObject.name} registering with RVO at position ({pos.x}, {pos.z})");
            _agentId = Simulator.Instance.addAgent(
                new RVO.Vector2(pos.x, pos.z),
                neighborDist,
                maxNeighbors,
                timeHorizon,
                timeHorizonObst,
                radius,
                maxSpeed,
                new RVO.Vector2(0f, 0f)
            );
        }

        private void Unregister()
        {
            if (_agentId < 0)
                return;

            _agentId = -1;
        }

        // ───────────── Runtime parameter updates ─────────────

        public void SetMaxSpeed(float speed)
        {
            maxSpeed = speed;
            if (_agentId >= 0)
                Simulator.Instance.setAgentMaxSpeed(_agentId, speed);
        }

        public void SetRadius(float r)
        {
            radius = r;
            if (_agentId >= 0)
                Simulator.Instance.setAgentRadius(_agentId, radius);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Avoidance radius (always visible)
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, radius);

            if (!Application.isPlaying || _agentId < 0)
                return;

            var sim = Simulator.Instance;
            Vector3 pos = transform.position;
            Vector3 up = Vector3.up * 0.2f;

            // Neighbor search radius (wire circle)
            Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
            Gizmos.DrawWireSphere(pos, sim.getAgentNeighborDist(_agentId));

            // Lines to each detected neighbor
            int neighborCount = sim.getAgentNumAgentNeighbors(_agentId);
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
            for (int i = 0; i < neighborCount; i++)
            {
                int neighborId = sim.getAgentAgentNeighbor(_agentId, i);
                RVO.Vector2 nPos = sim.getAgentPosition(neighborId);
                Vector3 neighborWorld = new Vector3(nPos.x(), 0f, nPos.y());
                neighborWorld.y = pos.y;
                Gizmos.DrawLine(pos + up, neighborWorld + up);

                // Neighbor's radius
                float nRadius = sim.getAgentRadius(neighborId);
                Gizmos.DrawWireSphere(neighborWorld, nRadius);
            }

            // ORCA constraint lines
            var orcaLines = sim.getAgentOrcaLines(_agentId);
            Gizmos.color = new Color(0.8f, 0.2f, 1f, 0.5f);
            for (int i = 0; i < orcaLines.Count; i++)
            {
                Line line = orcaLines[i];
                Vector3 point = pos + new Vector3(line.point.x(), 0f, line.point.y());
                Vector3 dir = new Vector3(line.direction.x(), 0f, line.direction.y());
                Gizmos.DrawRay(point + up, dir * 2f);
                Gizmos.DrawRay(point + up, -dir * 2f);
            }

            // Desired velocity (green)
            if (DesiredVelocity.sqrMagnitude > 0.01f)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawRay(pos + Vector3.up * 2f, DesiredVelocity.normalized * 1.5f);
                Gizmos.DrawSphere(pos + Vector3.up * 2f + DesiredVelocity.normalized * 1.5f, 0.06f);
            }

            // Computed velocity (cyan)
            if (ComputedVelocity.sqrMagnitude > 0.01f)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(pos + Vector3.up * 2.3f, ComputedVelocity.normalized * 1.5f);
                Gizmos.DrawSphere(pos + Vector3.up * 2.3f + ComputedVelocity.normalized * 1.5f, 0.06f);
            }

            // Info label
            // UnityEditor.Handles.color = Color.white;
            // UnityEditor.Handles.Label(pos + Vector3.up * 3f,
            //     $"RVO #{_agentId}\n" +
            //     $"Neighbors: {neighborCount}/{sim.getAgentMaxNeighbors(_agentId)}\n" +
            //     $"ORCA lines: {orcaLines.Count}\n" +
            //     $"Speed: {ComputedVelocity.magnitude:F1}/{sim.getAgentMaxSpeed(_agentId):F1}\n" +
            //     $"Radius: {sim.getAgentRadius(_agentId):F2}");
        }
#endif
    }
}