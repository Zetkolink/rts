using Pathfinding.Movement.ECS;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;

namespace Pathfinding.Systems
{
    /// <summary>
    /// Consumes <see cref="NeedsPathTag"/>, calls <see cref="NavMesh.CalculatePath"/>,
    /// fills the <see cref="PathWaypoint"/> buffer.
    /// Main thread only — NavMesh API is managed and not thread-safe.
    /// SystemBase (class) because NavMeshPath and Vector3[] are managed types.
    /// Budget-limited: processes at most <see cref="MaxPathsPerFrame"/> entities per frame.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateBefore(typeof(PathFollowSystem))]
    public partial class PathCalculationSystem : SystemBase
    {
        private const int MaxPathsPerFrame = 8;

        private NavMeshPath _sharedPath;
        private Vector3[] _cornerBuffer;

        protected override void OnCreate()
        {
            _sharedPath = new NavMeshPath();
            _cornerBuffer = new Vector3[64];

            RequireForUpdate<NeedsPathTag>();
        }

        protected override void OnUpdate()
        {
            int budget = MaxPathsPerFrame;

            foreach (var (transform, moveTarget, pathConfig, followState, waypoints, entity) in
                     SystemAPI.Query<
                             RefRO<LocalTransform>,
                             RefRO<MoveTarget>,
                             RefRO<PathConfig>,
                             RefRW<PathFollowState>,
                             DynamicBuffer<PathWaypoint>>()
                         .WithAll<NeedsPathTag>()
                         .WithEntityAccess())
            {
                if (budget-- <= 0)
                    break;

                // Consume the request.
                SystemAPI.SetComponentEnabled<NeedsPathTag>(entity, false);

                float3 startPos = transform.ValueRO.Position;
                float3 endPos = moveTarget.ValueRO.Destination;
                float sampleDist = pathConfig.ValueRO.SampleDistance;
                int areaMask = pathConfig.ValueRO.AreaMask;

                if (!TryCalculatePath(startPos, endPos, sampleDist, areaMask, waypoints, ref followState.ValueRW))
                {
                    SystemAPI.SetComponentEnabled<HasPathTag>(entity, false);
                    SystemAPI.SetComponentEnabled<IsMovingTag>(entity, false);
                    UnityEngine.Debug.Log($"[PathCalc] Path failed from {startPos} to {endPos}");
                    continue;
                }

                SystemAPI.SetComponentEnabled<HasPathTag>(entity, true);
                SystemAPI.SetComponentEnabled<IsMovingTag>(entity, true);
            }
        }

        private bool TryCalculatePath(
            float3 start, float3 end, float sampleDist, int areaMask,
            DynamicBuffer<PathWaypoint> waypoints, ref PathFollowState followState)
        {
            Vector3 startVec = start;
            Vector3 endVec = end;

            if (!NavMesh.SamplePosition(startVec, out NavMeshHit startHit, sampleDist, areaMask))
                return false;

            if (!NavMesh.SamplePosition(endVec, out NavMeshHit endHit, sampleDist, areaMask))
                return false;

            if (!NavMesh.CalculatePath(startHit.position, endHit.position, areaMask, _sharedPath))
                return false;

            if (_sharedPath.status == NavMeshPathStatus.PathInvalid)
                return false;

            int cornerCount = _sharedPath.GetCornersNonAlloc(_cornerBuffer);
            if (cornerCount < 2)
                return false;

            waypoints.Clear();
            for (int i = 0; i < cornerCount; i++)
            {
                waypoints.Add(new PathWaypoint
                {
                    Position = _cornerBuffer[i]
                });
            }

            followState.CurrentCorner = 1;
            return true;
        }
    }
}