using Pathfinding.Movement.ECS;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Pathfinding.Systems
{
    /// <summary>
    /// Rotates <see cref="LocalTransform"/> toward <see cref="ComputedVelocity"/> direction.
    /// Uses RotateTowards with <see cref="MoveSpeed.RotationSpeed"/> degrees/sec.
    /// Only runs on moving entities with non-zero velocity.
    /// Burst-compiled, ScheduleParallel.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(MovementApplySystem))]
    public partial struct RotationApplySystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new RotationApplyJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            };
            job.ScheduleParallel();
        }

        [BurstCompile]
        [WithAll(typeof(IsMovingTag))]
        private partial struct RotationApplyJob : IJobEntity
        {
            public float DeltaTime;

            public void Execute(ref LocalTransform transform, in ComputedVelocity velocity, in MoveSpeed moveSpeed)
            {
                float3 dir = velocity.Value;
                dir.y = 0f;

                if (math.lengthsq(dir) < 0.001f)
                    return;

                quaternion target = quaternion.LookRotationSafe(dir, math.up());
                float maxRadians = math.radians(moveSpeed.RotationSpeed) * DeltaTime;
                transform.Rotation = RotateTowards(transform.Rotation, target, maxRadians);
            }

            private static quaternion RotateTowards(quaternion from, quaternion to, float maxRadians)
            {
                float dot = math.dot(from.value, to.value);

                // Already aligned or nearly so.
                if (dot >= 1f - math.EPSILON)
                    return to;

                // Ensure shortest path.
                if (dot < 0f)
                {
                    to.value = -to.value;
                    dot = -dot;
                }

                dot = math.clamp(dot, -1f, 1f);
                float angle = math.acos(dot) * 2f; // full angle between quaternions

                if (angle <= maxRadians)
                    return to;

                float t = maxRadians / angle;
                return math.slerp(from, to, t);
            }
        }
    }
}