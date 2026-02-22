using Pathfinding.Avoidance.ECS;
using Pathfinding.Movement.ECS;
using Unity.Entities;
using UnityEngine;
using UnityEngine.AI;

namespace Pathfinding.Authoring
{
    /// <summary>
    /// Authoring component for a moveable unit.
    /// Place on a GameObject inside a SubScene.
    /// Baker converts all fields into ECS components — this MonoBehaviour does not exist at runtime.
    /// </summary>
    public sealed class UnitMovementAuthoring : MonoBehaviour
    {
        [Header("Movement")] [Tooltip("Units per second.")] [SerializeField]
        private float moveSpeed = 5f;

        [Tooltip("Degrees per second.")] [SerializeField]
        private float rotationSpeed = 720f;

        [Header("Path Following")] [Tooltip("Distance to consider an intermediate waypoint reached.")] [SerializeField]
        private float waypointTolerance = 0.15f;

        [Tooltip("Distance to consider the final destination reached.")] [SerializeField]
        private float arrivalTolerance = 0.1f;

        [Tooltip("Seconds between automatic path recalculations while moving.")] [SerializeField]
        private float repathInterval = 0.5f;

        [Header("NavMesh Query")] [Tooltip("NavMesh.SamplePosition search radius.")] [SerializeField]
        private float sampleDistance = 5f;

        [Tooltip("NavMesh area mask for pathfinding.")] [SerializeField]
        private int areaMask = NavMesh.AllAreas;

        [Header("RVO Avoidance")] [Tooltip("Agent collision radius for RVO.")] [SerializeField]
        private float rvoRadius = 0.5f;

        [Tooltip("Maximum speed reported to RVO (should match moveSpeed).")] [SerializeField]
        private float rvoMaxSpeed = 5f;

        [Tooltip("Radius within which other agents are considered neighbors.")] [SerializeField]
        private float neighborDist = 5f;

        [Tooltip("Maximum number of neighbors to consider for avoidance.")] [Range(1, 20)] [SerializeField]
        private int maxNeighbors = 10;

        [Tooltip("Time horizon for agent-agent avoidance (seconds).")] [SerializeField]
        private float timeHorizon = 2f;

        [Tooltip("Time horizon for obstacle avoidance (seconds).")] [SerializeField]
        private float timeHorizonObst = 1f;

        private sealed class Baker : Baker<UnitMovementAuthoring>
        {
            public override void Bake(UnitMovementAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                // ── Config ──
                AddComponent(entity, new MoveSpeed
                {
                    Speed = authoring.moveSpeed,
                    RotationSpeed = authoring.rotationSpeed
                });

                AddComponent(entity, new PathConfig
                {
                    WaypointTolerance = authoring.waypointTolerance,
                    ArrivalTolerance = authoring.arrivalTolerance,
                    RepathInterval = authoring.repathInterval,
                    SampleDistance = authoring.sampleDistance,
                    AreaMask = authoring.areaMask
                });

                AddComponent(entity, new AvoidanceConfig
                {
                    Radius = authoring.rvoRadius,
                    MaxSpeed = authoring.rvoMaxSpeed,
                    NeighborDist = authoring.neighborDist,
                    MaxNeighbors = authoring.maxNeighbors,
                    TimeHorizon = authoring.timeHorizon,
                    TimeHorizonObst = authoring.timeHorizonObst
                });

                // ── State (zeroed) ──
                AddComponent(entity, new MoveTarget { Destination = default });
                AddComponent(entity, new DeferredMove { Destination = default, FramesLeft = 0 });
                AddComponent(entity, new RepathTimer { Timer = 0f });
                AddComponent(entity, new PathFollowState { CurrentCorner = 0 });
                AddComponent(entity, new DesiredVelocity { Value = default });
                AddComponent(entity, new ComputedVelocity { Value = default });
                AddComponent(entity, new NormalizedSpeed { Value = 0f });

                // ── RVO handle (unregistered — RvoRegistrationSystem assigns at runtime) ──
                AddComponent(entity, RvoAgentRef.Unregistered);

                // ── Tags (all disabled at spawn) ──
                AddComponent<IsMovingTag>(entity);
                SetComponentEnabled<IsMovingTag>(entity, false);

                AddComponent<NeedsPathTag>(entity);
                SetComponentEnabled<NeedsPathTag>(entity, false);

                AddComponent<HasPathTag>(entity);
                SetComponentEnabled<HasPathTag>(entity, false);

                AddComponent<RvoImmovableTag>(entity);
                SetComponentEnabled<RvoImmovableTag>(entity, true); // idle at spawn = immovable

                // ── Waypoint buffer (empty) ──
                AddBuffer<PathWaypoint>(entity);
            }
        }
    }
}