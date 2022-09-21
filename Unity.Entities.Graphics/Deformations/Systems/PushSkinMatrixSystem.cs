using Unity.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Deformations;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace Unity.Rendering
{
    [RequireMatchingQueriesForUpdate]
    partial class PushSkinMatrixSystem : SystemBase
    {
        static readonly ProfilerMarker k_Marker = new ProfilerMarker("PrepareSkinMatrixForGPU");

        EntityQuery m_SkinningEntityQuery;

        PushMeshDataSystem m_PushMeshDataSystem;

        protected override void OnCreate()
        {
            if (!UnityEngine.SystemInfo.supportsComputeShaders)
            {
                Enabled = false;
                return;
            }

            m_PushMeshDataSystem = World.GetOrCreateSystemManaged<PushMeshDataSystem>();
            Assert.IsNotNull(m_PushMeshDataSystem, $"{typeof(PushMeshDataSystem)} system was not found in the world!");

            m_SkinningEntityQuery = GetEntityQuery(
                ComponentType.ReadOnly<SharedMeshTracker>(),
                ComponentType.ReadOnly<SkinMatrixBufferIndex>(),
                ComponentType.ReadOnly<DeformedEntity>()
            );
        }

        protected override void OnUpdate()
        {
            if (m_PushMeshDataSystem.SkinMatrixCount == 0)
                return;

            k_Marker.Begin();

            var deformedEntityToComputeIndex = new NativeMultiHashMap<Entity, int>(m_SkinningEntityQuery.CalculateEntityCount(), Allocator.TempJob);
            var deformedEntityToComputeIndexParallel = deformedEntityToComputeIndex.AsParallelWriter();

            Dependency = Entities
                .WithName("ConstructHashMap")
                .WithAll<SharedMeshTracker>()
                .ForEach((in SkinMatrixBufferIndex index, in DeformedEntity deformedEntity) =>
                {
                    // Skip if we have an invalid index.
                    if (index.Value == SkinMatrixBufferIndex.Null)
                        return;

                    deformedEntityToComputeIndexParallel.Add(deformedEntity.Value, index.Value);
                }).ScheduleParallel(Dependency);

            var skinMatricesBuffer = m_PushMeshDataSystem.SkinningBufferManager.LockSkinMatrixBufferForWrite(m_PushMeshDataSystem.SkinMatrixCount);
            Dependency = Entities
                .WithName("CopySkinMatricesToGPU")
                .WithNativeDisableContainerSafetyRestriction(skinMatricesBuffer)
                .WithReadOnly(deformedEntityToComputeIndex)
                .ForEach((ref DynamicBuffer<SkinMatrix> skinMatrices, in Entity entity) =>
                {
                    // Not all deformed entities in the world will have a renderer attached to them.
                    if (!deformedEntityToComputeIndex.ContainsKey(entity))
                        return;

                    long length = (long)skinMatrices.Length * UnsafeUtility.SizeOf<float3x4>();
                    var indices = deformedEntityToComputeIndex.GetValuesForKey(entity);

                    foreach (var index in indices)
                    {
                        unsafe
                        {
                            UnsafeUtility.MemCpy(
                                (float3x4*)skinMatricesBuffer.GetUnsafePtr() + index,
                                skinMatrices.GetUnsafePtr(),
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
