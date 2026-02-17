namespace RTS.Pathfinding
{
    /// <summary>
    /// Defines unit size categories for multi-clearance pathfinding.
    /// Each class uses a separate nav grid with different erosion radius,
    /// ensuring large units don't path through narrow gaps and small units
    /// aren't over-restricted by large-unit erosion.
    /// </summary>
    public enum ClearanceClass
    {
        /// <summary>Infantry, scouts — small radius (~0.4). Minimal erosion.</summary>
        Small = 0,

        /// <summary>Vehicles, cavalry — medium radius (~0.8). Moderate erosion.</summary>
        Medium = 1,

        /// <summary>Tanks, siege — large radius (~1.5). Maximum erosion.</summary>
        Large = 2
    }
}
