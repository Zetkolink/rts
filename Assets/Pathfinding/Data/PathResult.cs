using UnityEngine;

namespace RTS.Pathfinding
{
    public enum PathStatus
    {
        Found,
        NotFound,
        Partial,
        Invalid
    }

    public sealed class PathResult
    {
        public PathStatus Status;
        public Vector3[] Waypoints;
        public Vector2Int[] RawCells;
        public int IterationsUsed;
        public float PathLength;
        public int RequestId;
    }
}