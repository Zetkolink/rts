using Select.ECS;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Select.Systems
{
    /// <summary>
    /// Draws selection indicator (green circle) under selected entities.
    /// DEBUG visualization using Debug.DrawLine. Replace with decal/projector for production.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class SelectionVisualSystem : SystemBase
    {
        private const int CircleSegments = 16;
        private static readonly Color SelectedColor = new Color(0.2f, 1f, 0.3f, 0.9f);

        protected override void OnUpdate()
        {
            foreach (var (transform, config) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<SelectableConfig>>()
                         .WithAll<IsSelectedTag>())
            {
                float3 pos = transform.ValueRO.Position;
                float radius = config.ValueRO.CollisionRadius * 1.5f;
                DrawCircle(pos + new float3(0f, 0.05f, 0f), radius, SelectedColor);
            }
        }

        private static void DrawCircle(float3 center, float radius, Color color)
        {
            float step = 2f * math.PI / CircleSegments;
            float3 prev = center + new float3(radius, 0f, 0f);

            for (int i = 1; i <= CircleSegments; i++)
            {
                float angle = step * i;
                float3 next = center + new float3(math.cos(angle) * radius, 0f, math.sin(angle) * radius);
                Debug.DrawLine(prev, next, color);
                prev = next;
            }
        }
    }
}