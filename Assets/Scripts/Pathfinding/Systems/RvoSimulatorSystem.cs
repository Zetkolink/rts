using Pathfinding.Avoidance.RVO;
using Unity.Entities;
using Unity.Transforms;
using RvoVector2 = Pathfinding.Avoidance.RVO.Vector2;

namespace Pathfinding.Systems
{
    /// <summary>
    /// Initializes RVO2 <see cref="Simulator"/> on create, sets timeStep and calls doStep() each frame.
    /// Replaces the old <c>RvoSimulatorBridge</c> MonoBehaviour.
    /// Must run after <see cref="RvoSyncOutSystem"/> and before <see cref="RvoSyncInSystem"/>.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(RvoSyncOutSystem))]
    public partial class RvoSimulatorSystem : SystemBase
    {
        private const float DefaultNeighborDist = 5f;
        private const int DefaultMaxNeighbors = 10;
        private const float DefaultTimeHorizon = 2f;
        private const float DefaultTimeHorizonObst = 1f;
        private const float DefaultRadius = 0.5f;
        private const float DefaultMaxSpeed = 5f;
        private const int NumWorkers = 0;

        public bool IsInitialized { get; private set; }

        protected override void OnCreate()
        {
            var sim = Simulator.Instance;
            sim.Clear();
            sim.setTimeStep(UnityEngine.Time.fixedDeltaTime);
            sim.SetNumWorkers(NumWorkers);
            sim.setAgentDefaults(
                DefaultNeighborDist, DefaultMaxNeighbors,
                DefaultTimeHorizon, DefaultTimeHorizonObst,
                DefaultRadius, DefaultMaxSpeed,
                new RvoVector2(0f, 0f));

            IsInitialized = true;
        }

        protected override void OnDestroy()
        {
            if (IsInitialized)
            {
                Simulator.Instance.Clear();
                IsInitialized = false;
            }
        }

        protected override void OnUpdate()
        {
            if (!IsInitialized)
                return;

            Simulator.Instance.setTimeStep(SystemAPI.Time.DeltaTime);
            Simulator.Instance.doStep();
        }
    }
}