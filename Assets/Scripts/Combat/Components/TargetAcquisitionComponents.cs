using Unity.Entities;
using Unity.Mathematics;

namespace Combat.ECS
{
    /// <summary>
    /// Timer for staggered target acquisition. Each unit scans on its own schedule.
    /// </summary>
    public struct TargetScanTimer : IComponentData
    {
        /// <summary>Seconds between scans.</summary>
        public float Interval;

        /// <summary>Countdown. Scans when ≤ 0, resets to Interval.</summary>
        public float Timer;
    }

    /// <summary>
    /// Tracks recent velocity of this entity for lead prediction by shooters.
    /// Updated by <see cref="Combat.Systems.VelocityTrackingSystem"/> each frame.
    /// </summary>
    public struct TrackedVelocity : IComponentData
    {
        public float3 Value;
        public float3 PreviousPosition;
    }

    /// <summary>
    /// Lead prediction accuracy. 0 = no prediction (fire at current pos),
    /// 1 = perfect prediction. Can be modified by veterancy, suppression, etc.
    /// </summary>
    public struct PredictionAccuracy : IComponentData
    {
        public float Value;
    }
}