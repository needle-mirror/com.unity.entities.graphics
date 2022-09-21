using Unity.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Deformations;
using Unity.Entities;
using Unity.Jobs;
using Unity.Profiling;

namespace Unity.Rendering
{
    [RequireMatchingQueriesForUpdate]
    partial class PushBlendWeightSystem : SystemBase
    {
        static readonly ProfilerMarker k_Marker = new ProfilerMarker("PrepareBlendWeightForGPU");

        EntityQuery m_BlendShapedEntityQuery;

        PushMeshDataSystem m_PushMeshDataSystem;

        protected override void OnCreate()
        {
            if (!UnityEngine.SystemInfo.supportsComputeShaders)
            {
                Enabled = false;
                return;
            }

            m_PushMeshDataSystem = World.GetOrCreateSystemManaged<PushMeshDataSystem>();
            Assert.IsNotNull(m_PushMeshDataSystem, $"{nameof(PushMeshDataSystem)} system was not found in the world!");

            m_BlendShapedEntityQuery = GetEntityQuery(
                ComponentType.ReadOnly<SharedMeshTracker>(),
                ComponentType.ReadOnly<BlendWeightBufferIndex>(),
                ComponentType.ReadOnly<DeformedEntity>()
            );
        }

        protected override void OnUpdate()
        {
            if (m_PushMeshDataSystem.BlendShapeWeightCount == 0)
                return;

            k_Marker.Begin();

            var deformedEntityToComputeIndex = new NativeMultiHashMap<Entity, int>(m_BlendShapedEntityQuery.CalculateEntityCount(), Allocator.TempJob);
            var deformedEntityToComputeIndexParallel = deformedEntityToComputeIndex.AsParallelWriter();
            Dependency = Entities
                .WithName("ConstructHashMap")
                .WithAll<SharedMeshTracker>()
                .ForEach((in BlendWeightBufferIndex index, in DeformedEntity deformedEntity) =>
                {
                    // Skip if we have an invalid index.
                    if (index.Value == BlendWeightBufferIndex.Null)
                        return;

                    deformedEntityToComputeIndexParallel.Add(deformedEntity.Value, index.Value);
                }).ScheduleParallel(Dependency);

            var blendShapeWeightsBuffer = m_PushMeshDataSystem.BlendShapeBufferManager.LockBlendWeightBufferForWrite(m_PushMeshDataSystem.BlendShapeWeightCount);
            Dependency = Entities
                .WithName("CopyBlendShapeWeightsToGPU")
                .WithNativeDisableContainerSafetyRestriction(blendShapeWeightsBuffer)
                .WithReadOnly(deformedEntityToComputeIndex)
                .ForEach((ref DynamicBuffer<BlendShapeWeight> weights, in Entity entity) =>
                {
                    // Not all deformed entities in the world will have a renderer attached to them.
                    if (!deformedEntityToComputeIndex.ContainsKey(entity))
                        return;

                    var length = weights.Length * UnsafeUtility.SizeOf<float>();
                    var indices = deformedEntityToComputeIndex.GetValuesForKey(entity);

                    foreach (var index in indices)
                    {
                        unsafe
                        {
                            UnsafeUtility.MemCpy(
                                (float*)blendShapeWeightsBuffer.GetUnsafePtr() + index,
                                weights.GetUnsafePtr(),
                                length
                            );
                        }
                    }
                }).ScheduleParallel(Dependency);

            Dependency = deformedEntityToComputeIndex.Dispose(Dependency);

            k_Marker.End();
        }
    }
}
