using Unity.Entities;

namespace Pathfinding.Avoidance.ECS
{
    // ════════════════════════════════════════════
    //  CONFIG — baked once, read-only at runtime
    // ════════════════════════════════════════════

    /// <summary>
    /// RVO2 agent parameters. Baked from authoring.
    /// Maps 1:1 to Simulator.addAgent() arguments.
    /// </summary>
    public struct AvoidanceConfig : IComponentData
    {
        public float Radius;
        public float MaxSpeed;
        public float NeighborDist;
        public int MaxNeighbors;
        public float TimeHorizon;
        public float TimeHorizonObst;
    }

    // ════════════════════════════════════════════
    //  STATE
    // ════════════════════════════════════════════

    /// <summary>
    /// Handle into the managed RVO2 Simulator.
    /// AgentId = -1 means not registered (cleaned up or pre-registration).
    /// Written by RvoRegistrationSystem, read by all Rvo* sync systems.
    /// </summary>
    public struct RvoAgentRef : IComponentData
    {
        public int AgentId;

        public bool IsRegistered => AgentId >= 0;

        public static RvoAgentRef Unregistered => new RvoAgentRef { AgentId = -1 };
    }

    // ════════════════════════════════════════════
    //  TAGS
    // ════════════════════════════════════════════

    /// <summary>
    /// When enabled, RVO agent maxSpeed is set to 0 — the unit is treated as a static obstacle
    /// by other agents. Enabled on arrival, disabled when a new move order is given.
    /// </summary>
    public struct RvoImmovableTag : IComponentData, IEnableableComponent
    {
    }
}