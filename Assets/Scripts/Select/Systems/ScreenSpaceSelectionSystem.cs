using Select.ECS;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Select.Systems
{
    /// <summary>
    /// Screen-space selection: projects entity positions onto screen via Camera,
    /// checks against click point or box rectangle from <see cref="SelectionInput"/>.
    ///
    /// No companion GameObjects, no colliders, no Physics.
    /// Main thread — Camera access.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial class ScreenSpaceSelectionSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<SelectableConfig>();
        }

        protected override void OnUpdate()
        {
            if (SelectionInput.Action == SelectionInput.ActionType.None)
                return;

            Camera cam = Camera.main;
            if (cam == null)
            {
                SelectionInput.Clear();
                return;
            }

            switch (SelectionInput.Action)
            {
                case SelectionInput.ActionType.Click:
                    HandleClick(cam, SelectionInput.ScreenPointA, SelectionInput.Additive);
                    break;

                case SelectionInput.ActionType.Box:
                    HandleBox(cam, SelectionInput.ScreenPointA, SelectionInput.ScreenPointB,
                        SelectionInput.Additive);
                    break;
            }

            SelectionInput.Clear();
        }

        private void HandleClick(Camera cam, Vector2 clickPos, bool additive)
        {
            Entity bestEntity = Entity.Null;
            float bestDistSq = float.MaxValue;

            foreach (var (config, transform, isSelected, entity) in
                     SystemAPI.Query<RefRO<SelectableConfig>, RefRO<LocalTransform>,
                             EnabledRefRW<IsSelectedTag>>()
                         .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                         .WithEntityAccess())
            {
                float3 worldPos = transform.ValueRO.Position;
                Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

                // Behind camera.
                if (screenPos.z < 0f)
                    continue;

                float dx = screenPos.x - clickPos.x;
                float dy = screenPos.y - clickPos.y;
                float distSq = dx * dx + dy * dy;
                float radius = config.ValueRO.ScreenRadius;

                if (distSq < radius * radius && distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestEntity = entity;
                }
            }

            // Apply selection.
            if (!additive)
                DeselectAll();

            if (bestEntity != Entity.Null)
            {
                if (additive && SystemAPI.IsComponentEnabled<IsSelectedTag>(bestEntity))
                    SystemAPI.SetComponentEnabled<IsSelectedTag>(bestEntity, false); // toggle off
                else
                    SystemAPI.SetComponentEnabled<IsSelectedTag>(bestEntity, true);
            }
        }

        private void HandleBox(Camera cam, Vector2 pointA, Vector2 pointB, bool additive)
        {
            Rect boxRect = MakeRect(pointA, pointB);

            if (!additive)
                DeselectAll();

            foreach (var (config, transform, isSelected, entity) in
                     SystemAPI.Query<RefRO<SelectableConfig>, RefRO<LocalTransform>,
                             EnabledRefRW<IsSelectedTag>>()
                         .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                         .WithEntityAccess())
            {
                float3 worldPos = transform.ValueRO.Position;
                Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

                // Behind camera.
                if (screenPos.z < 0f)
                    continue;

                // Check if screen position is inside box.
                if (boxRect.Contains(new Vector2(screenPos.x, screenPos.y)))
                    isSelected.ValueRW = true;
            }
        }

        private void DeselectAll()
        {
            foreach (var isSelected in
                     SystemAPI.Query<EnabledRefRW<IsSelectedTag>>()
                         .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            {
                isSelected.ValueRW = false;
            }
        }

        private static Rect MakeRect(Vector2 a, Vector2 b)
        {
            float x = math.min(a.x, b.x);
            float y = math.min(a.y, b.y);
            float w = math.abs(a.x - b.x);
            float h = math.abs(a.y - b.y);
            return new Rect(x, y, w, h);
        }
    }
}