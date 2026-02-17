namespace RTS.Pathfinding
{
    /// <summary>
    /// Formation shapes for group movement commands.
    /// </summary>
    public enum FormationType
    {
        /// <summary>Rectangular grid. Good default for mixed groups.</summary>
        Box,

        /// <summary>Single row. Useful for holding a line.</summary>
        Line,

        /// <summary>V-shape pointing forward. Good for aggressive advance.</summary>
        Wedge,

        /// <summary>Single file. Auto-selected for narrow passages.</summary>
        Column
    }
}
