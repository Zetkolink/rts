using Combat.ECS;
using Pathfinding.Movement.ECS;
using Select.ECS;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Command
{
    /// <summary>
    /// Resolves right-click input into commands for selected entities.
    ///
    /// Resolution priority:
    ///   1. Hit enemy unit → Attack (set AttackTarget + IsEngagedTag)
    ///   2. Hit ground → Move (grid formation, deferred for NavObstacle)
    ///
    /// Reads layer masks and tuning from <see cref="CommandConfig"/> singleton.
    /// Main thread — Input, Physics.Raycast, Camera access.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial class CommandResolverSystem : SystemBase
    {
        private EntityQuery _selectedQuery;

        protected override void OnCreate()
        {
            _selectedQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<IsSelectedTag, DeferredMove>()
                .Build(this);

            RequireForUpdate<IsSelectedTag>();
            RequireForUpdate<CommandConfig>();
        }

        protected override void OnUpdate()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.wasPressedThisFrame)
                return;

            Camera cam = Camera.main;
            if (cam == null)
                return;

            Vector2 screenPos = mouse.position.ReadValue();
            Ray ray = cam.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));

            var config = SystemAPI.GetSingleton<CommandConfig>();
            ResolveAndDispatch(ray, config);
        }

        // ───────────── Resolution ─────────────

        private void ResolveAndDispatch(Ray ray, CommandConfig config)
        {
            if (TryResolveAttack(ray, config))
                return;

            TryResolveMove(ray, config);
        }

        // ───────────── Attack ─────────────

        /// <summary>Screen-space radius in pixels for clicking on enemy unit.</summary>
        private const float AttackClickRadius = 35f;

        private bool TryResolveAttack(Ray ray, CommandConfig config)
        {
            Camera cam = Camera.main;
            if (cam == null)
                return false;

            Vector2 clickPos = Mouse.current.position.ReadValue();

            // Get our team from first selected entity.
            var selectedEntities = _selectedQuery.ToEntityArray(Allocator.Temp);
            if (selectedEntities.Length == 0)
            {
                selectedEntities.Dispose();
                return false;
            }

            byte myTeam = 255;
            if (SystemAPI.HasComponent<TeamTag>(selectedEntities[0]))
                myTeam = SystemAPI.GetComponent<TeamTag>(selectedEntities[0]).TeamId;

            // Find nearest enemy under cursor via screen-space projection.
            Entity bestTarget = Entity.Null;
            float bestDistSq = AttackClickRadius * AttackClickRadius;

            foreach (var (transform, team, selectConfig, entity) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<TeamTag>, RefRO<SelectableConfig>>()
                         .WithDisabled<IsDeadTag>()
                         .WithEntityAccess())
            {
                // Only enemies.
                if (team.ValueRO.TeamId == myTeam)
                    continue;

                float3 worldPos = transform.ValueRO.Position +
                                  new float3(0f, selectConfig.ValueRO.Height * 0.5f, 0f);
                Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

                if (screenPos.z < 0f)
                    continue;

                float dx = screenPos.x - clickPos.x;
                float dy = screenPos.y - clickPos.y;
                float distSq = dx * dx + dy * dy;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestTarget = entity;
                }
            }

            if (bestTarget == Entity.Null)
            {
                selectedEntities.Dispose();
                return false;
            }

            IssueAttackCommand(selectedEntities, bestTarget);
            selectedEntities.Dispose();
            return true;
        }

        private void IssueAttackCommand(NativeArray<Entity> selectedEntities, Entity target)
        {
            for (int i = 0; i < selectedEntities.Length; i++)
            {
                Entity entity = selectedEntities[i];

                // Stop movement.
                SystemAPI.SetComponent(entity, new DesiredVelocity { Value = float3.zero });
                SystemAPI.SetComponent(entity, new ComputedVelocity { Value = float3.zero });
                SystemAPI.SetComponent(entity, new NormalizedSpeed { Value = 0f });
                SystemAPI.SetComponentEnabled<IsMovingTag>(entity, false);
                SystemAPI.SetComponentEnabled<HasPathTag>(entity, false);

                // Cancel pending deferred move.
                SystemAPI.SetComponent(entity, new DeferredMove
                {
                    Destination = float3.zero,
                    FramesLeft = 0
                });

                // Engage target.
                if (SystemAPI.HasComponent<AttackTarget>(entity))
                {
                    SystemAPI.SetComponent(entity, new AttackTarget { Target = target });
                    SystemAPI.SetComponentEnabled<IsEngagedTag>(entity, true);
                }
            }

            Debug.Log($"[Command] Attack issued on {target} by {selectedEntities.Length} units");
        }

        // ───────────── Move ─────────────

        private bool TryResolveMove(Ray ray, CommandConfig config)
        {
            if (!Physics.Raycast(ray, out RaycastHit hit, config.MaxRayDistance, config.GroundLayerMask))
                return false;

            IssueMoveCommand((float3)hit.point, config);
            return true;
        }

        private void IssueMoveCommand(float3 center, CommandConfig config)
        {
            var entities = _selectedQuery.ToEntityArray(Allocator.Temp);
            int count = entities.Length;

            if (count == 0)
            {
                entities.Dispose();
                return;
            }

            float spacing = config.UnitSpacing;
            int columns = (int)math.ceil(math.sqrt(count));
            float halfWidth = (columns - 1) * spacing * 0.5f;

            for (int i = 0; i < count; i++)
            {
                Entity entity = entities[i];

                int col = i % columns;
                int row = i / columns;

                float3 offset = new float3(
                    col * spacing - halfWidth,
                    0f,
                    -row * spacing);

                // Stop current movement.
                SystemAPI.SetComponent(entity, new DesiredVelocity { Value = float3.zero });
                SystemAPI.SetComponent(entity, new ComputedVelocity { Value = float3.zero });
                SystemAPI.SetComponent(entity, new NormalizedSpeed { Value = 0f });
                SystemAPI.SetComponentEnabled<IsMovingTag>(entity, false);
                SystemAPI.SetComponentEnabled<HasPathTag>(entity, false);

                // Disengage on move order.
                if (SystemAPI.HasComponent<AttackTarget>(entity))
                    SystemAPI.SetComponentEnabled<IsEngagedTag>(entity, false);

                // Issue deferred move.
                SystemAPI.SetComponent(entity, new DeferredMove
                {
                    Destination = center + offset,
                    FramesLeft = config.DeferFrames
                });
            }

            entities.Dispose();
        }
    }
}