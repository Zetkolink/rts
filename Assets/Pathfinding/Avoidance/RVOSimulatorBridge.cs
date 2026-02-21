using UnityEngine;
using Pathfinding.Avoidance.RVO;

namespace Pathfinding.Avoidance
{
    /// <summary>
    /// Unity-side bridge to RVO2 library's Simulator singleton.
    /// Initializes RVO, steps simulation each FixedUpdate.
    /// One per scene — attach to [Pathfinding] object.
    /// </summary>
    public sealed class RvoSimulatorBridge : MonoBehaviour
    {
        [Header("Agent Defaults")] [SerializeField]
        private float neighborDist = 5f;

        [SerializeField] [Range(4, 20)] private int maxNeighbors = 10;
        [SerializeField] private float timeHorizon = 2f;
        [SerializeField] private float timeHorizonObst = 1f;
        [SerializeField] private float defaultRadius = 0.5f;
        [SerializeField] private float defaultMaxSpeed = 5f;

        [Header("Workers")] [SerializeField] [Range(0, 8)]
        private int numWorkers = 2;

        public static RvoSimulatorBridge Instance { get; private set; }

        public bool IsInitialized { get; private set; }
        public int AgentCount => IsInitialized ? Simulator.Instance.getNumAgents() : 0;

        private void OnEnable()
        {
            // Debug.Log("RVOSimulatorBridge OnEnable");
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Debug.unityLogger.Log("RVOSimulatorBridge OnEnable");
            Instance = this;
            InitializeSimulator();
        }

        private void OnDisable()
        {
            if (Instance != this) return;

            if (IsInitialized)
            {
                Simulator.Instance.Clear();
                IsInitialized = false;
            }

            Instance = null;
        }

        private void FixedUpdate()
        {
            if (!IsInitialized) return;

            Simulator.Instance.setTimeStep(Time.fixedDeltaTime);
            Simulator.Instance.doStep();
        }

        private void InitializeSimulator()
        {
            Simulator.Instance.Clear();
            Simulator.Instance.setTimeStep(Time.fixedDeltaTime);
            Simulator.Instance.SetNumWorkers(numWorkers);
            Simulator.Instance.setAgentDefaults(
                neighborDist,
                maxNeighbors,
                timeHorizon,
                timeHorizonObst,
                defaultRadius,
                defaultMaxSpeed,
                new RVO.Vector2(0f, 0f)
            );

            IsInitialized = true;
        }
    }
}