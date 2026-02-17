using UnityEngine;

namespace RTS.Pathfinding.Avoidance
{
    /// <summary>
    /// Unity-side bridge to RVO2 library's Simulator singleton.
    /// Initializes RVO, steps simulation each FixedUpdate.
    /// One per scene — attach to [Pathfinding] object.
    /// </summary>
    public sealed class RVOSimulatorBridge : MonoBehaviour
    {
        [Header("Agent Defaults")]
        [Tooltip("Neighbor search radius")]
        [SerializeField] private float _neighborDist = 5f;

        [Tooltip("Max neighbors considered per agent")]
        [SerializeField] [Range(4, 20)] private int _maxNeighbors = 10;

        [Tooltip("Time horizon for agent-agent avoidance (seconds)")]
        [SerializeField] private float _timeHorizon = 2f;

        [Tooltip("Time horizon for obstacle avoidance (seconds)")]
        [SerializeField] private float _timeHorizonObst = 1f;

        [Tooltip("Default agent radius")]
        [SerializeField] private float _defaultRadius = 0.5f;

        [Tooltip("Default max speed")]
        [SerializeField] private float _defaultMaxSpeed = 5f;

        [Header("Workers")]
        [Tooltip("Thread count for RVO computation. 0 = auto")]
        [SerializeField] [Range(0, 8)] private int _numWorkers = 2;

        public static RVOSimulatorBridge Instance { get; private set; }

        // Debug
        public int AgentCount => RVO.Simulator.Instance.getNumAgents();

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            RVO.Simulator.Instance.Clear();
            RVO.Simulator.Instance.setTimeStep(Time.fixedDeltaTime);
            RVO.Simulator.Instance.SetNumWorkers(_numWorkers);
            RVO.Simulator.Instance.setAgentDefaults(
                _neighborDist,
                _maxNeighbors,
                _timeHorizon,
                _timeHorizonObst,
                _defaultRadius,
                _defaultMaxSpeed,
                new RVO.Vector2(0f, 0f)
            );

            Debug.Log($"[RVO] Simulator initialized. Workers: {_numWorkers}, " +
                      $"TimeHorizon: {_timeHorizon}, NeighborDist: {_neighborDist}");
        }

        private void FixedUpdate()
        {
            RVO.Simulator.Instance.setTimeStep(Time.fixedDeltaTime);
            RVO.Simulator.Instance.doStep();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                RVO.Simulator.Instance.Clear();
                Instance = null;
            }
        }
    }
}