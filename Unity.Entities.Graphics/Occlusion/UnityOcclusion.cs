#if ENABLE_UNITY_OCCLUSION && (HDRP_10_0_0_OR_NEWER || URP_10_0_0_OR_NEWER)
// #define WAIT_FOR_EACH_JOB // This is useful for profiling individual jobs, but should be commented out for performance

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using UnityEngine.Rendering;
using System.Collections.Generic;
using Unity.Burst.Intrinsics;
using Unity.Profiling;
using Unity.Rendering.Occlusion.Masked;
using Unity.Rendering.Occlusion.Masked.Dots;
using Unity.Rendering.Occlusion.Masked.Visualization;

namespace Unity.Rendering.Occlusion
{
    public unsafe class OcclusionCulling
    {
        public bool IsEnabled = true;
        public DebugSettings debugSettings = new DebugSettings();
        public Dictionary<ulong, BufferGroup> BufferGroups { get; } = new Dictionary<ulong, BufferGroup>();
        EntityQuery m_OcclusionMeshQuery;
        EntityQuery m_ReadonlyTestQuery;
        EntityQuery m_ReadonlyMeshQuery;

        static readonly ProfilerMarker s_Cull = new ProfilerMarker("Occlusion.Cull");
        static readonly ProfilerMarker s_SetResolution = new ProfilerMarker("Occlusion.Cull.SetResolution");
        static readonly ProfilerMarker s_Clear = new ProfilerMarker("Occlusion.Cull.Clear");
        static readonly ProfilerMarker s_MeshTransform = new ProfilerMarker("Occlusion.Cull.MeshTransform");
        static readonly ProfilerMarker s_SortMeshes = new ProfilerMarker("Occlusion.Cull.SortMeshes");
        static readonly ProfilerMarker s_ComputeBounds = new ProfilerMarker("Occlusion.Cull.ComputeBounds");
        static readonly ProfilerMarker s_Rasterize = new ProfilerMarker("Occlusion.Cull.Rasterize");
        static readonly ProfilerMarker s_Test = new ProfilerMarker("Occlusion.Cull.Test");

        public void Create(EntityManager entityManager)
        {
            debugSettings.Register();
            m_OcclusionMeshQuery = entityManager.CreateEntityQuery(
                typeof(OcclusionMesh),
                typeof(LocalToWorld)
            );
            m_ReadonlyTestQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<OcclusionTest>());
            m_ReadonlyMeshQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<OcclusionMesh>(),
                ComponentType.ReadOnly<LocalToWorld>()
            );


            m_OcclusionTestTransformGroup = entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<RenderBounds>(),
                    ComponentType.ReadOnly<LocalToWorld>(),
                    ComponentType.ReadOnly<OcclusionTest>(),
                    ComponentType.ChunkComponentReadOnly<ChunkOcclusionTest>(),
                },
            });

            m_OcclusionTestGroup = entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ChunkOcclusionTest>(),
                    ComponentType.ReadOnly<ChunkHeader>(),
                    ComponentType.ReadOnly<EntitiesGraphicsChunkInfo>()
                },
            });
        }

        public void Dispose()
        {
            debugSettings.Unregister();
            foreach (var bufferGroup in BufferGroups.Values)
            {
                bufferGroup.Dispose();
            }
        }

        JobHandle CullView(
            BufferGroup bufferGroup,
            int splitIndex,
            JobHandle incomingJob,
            EntityManager entityManager,
            IndirectList<ChunkVisibilityItem> visibilityItems,
            bool InvertOcclusion,
            BatchCullingViewType viewType
        )
        {
            s_Cull.Begin();

            /* Clear memory
               ------------ */
            s_Clear.Begin();
            /* We want to maximize occupancy. The workload of each job is tiny: It's three assembly instructions,
               which is why we want to have the large number of inner-loop batches that can spread across all
               available worker threads */
            var clearJob = new ClearJob
            {
                Tiles = (Tile*) bufferGroup.Tiles.GetUnsafePtr()
            }.ScheduleParallel(bufferGroup.Tiles.Length, 1024, new JobHandle());
#if WAIT_FOR_EACH_JOB
            clearJob.Complete();
#endif // WAIT_FOR_EACH_JOB
            s_Clear.End();

            var localToWorlds = m_OcclusionMeshQuery.ToComponentDataArray<LocalToWorld>(Allocator.TempJob);
            var meshes = m_OcclusionMeshQuery.ToComponentDataArray<OcclusionMesh>(Allocator.TempJob);

            /* Transform occluder meshes
               ------------------------- */
            s_MeshTransform.Begin();
            var transformJob = new MeshTransformJob()
            {
                ViewProjection = bufferGroup.CullingMatrix,
                ProjectionType = bufferGroup.ProjectionType,
                NearClip = bufferGroup.NearClip,
                FrustumPlanes = (v128*)bufferGroup.FrustumPlanes.GetUnsafePtr(),
                HalfWidth = bufferGroup.HalfWidth,
                HalfHeight = bufferGroup.HalfHeight,
                PixelCenterX = bufferGroup.PixelCenterX,
                PixelCenterY = bufferGroup.PixelCenterY,
                LocalToWorlds = localToWorlds,
                Meshes = meshes,
            }.ScheduleParallel(meshes.Length, 4, incomingJob);
#if WAIT_FOR_EACH_JOB
            transformJob.Complete();
#endif // WAIT_FOR_EACH_JOB
            localToWorlds.Dispose(transformJob);
            s_MeshTransform.End();

            /* Sort meshes by vertex count
               --------------------------- */
            // TODO: Look at perf. Evaluate whether running this job is even worth it. It only takes 0.02ms in Viking Village,
            // which is why I haven't looked at it yet.
            s_SortMeshes.Begin();
            var sortJob = new OcclusionSortMeshesJob
            {
                Meshes = meshes,
            }.Schedule(transformJob);
#if WAIT_FOR_EACH_JOB
            sortJob.Complete();
#endif // WAIT_FOR_EACH_JOB
            s_SortMeshes.End();

            /* Compute occludee bounds
               ----------------------- */
            s_ComputeBounds.Begin();
            var computeBoundsJob = new ComputeBoundsJob
            {
                ViewProjection = bufferGroup.CullingMatrix,
                NearClip = bufferGroup.NearClip,
                Bounds = entityManager.GetComponentTypeHandle<RenderBounds>(true),
                LocalToWorld = entityManager.GetComponentTypeHandle<LocalToWorld>(true),
                OcclusionTest = entityManager.GetComponentTypeHandle<OcclusionTest>(false),
                ChunkOcclusionTest = entityManager.GetComponentTypeHandle<ChunkOcclusionTest>(false),
                ProjectionType = bufferGroup.ProjectionType,
            }.ScheduleParallel(m_OcclusionTestTransformGroup, incomingJob);
#if WAIT_FOR_EACH_JOB
            computeBoundsJob.Complete();
#endif // WAIT_FOR_EACH_JOB
            s_ComputeBounds.End();

            /* Rasterize
               --------- */
            JobHandle rasterizeJob;
            if (meshes.Length > 0)
            {
                s_Rasterize.Begin();
                const int TilesPerBinX = 2;//16 tiles per X axis, values can be 1 2 4 8 16
                const int TilesPerBinY = 4;//128 tiles per X axis, values can be 1 2 4 8 16 32 64 128
                const int TilesPerBin = TilesPerBinX * TilesPerBinY;
                int numBins = bufferGroup.NumTilesX * bufferGroup.NumTilesY / TilesPerBin;
                rasterizeJob = new RasterizeJob
                {
                    Meshes = meshes,
                    ProjectionType = bufferGroup.ProjectionType,
                    NumBuffers = bufferGroup.NumBuffers,
                    HalfWidth = bufferGroup.HalfWidth,
                    HalfHeight = bufferGroup.HalfHeight,
                    PixelCenterX = bufferGroup.PixelCenterX,
                    PixelCenterY = bufferGroup.PixelCenterY,
                    PixelCenter = bufferGroup.PixelCenter,
                    HalfSize = bufferGroup.HalfSize,
                    ScreenSize = bufferGroup.ScreenSize,
                    NumPixelsX = bufferGroup.NumPixelsX,
                    NumPixelsY = bufferGroup.NumPixelsY,
                    NumTilesX = bufferGroup.NumTilesX,
                    NumTilesY = bufferGroup.NumTilesY,
                    NearClip = bufferGroup.NearClip,
                    FrustumPlanes = (v128*)bufferGroup.FrustumPlanes.GetUnsafePtr(),
                    FullScreenScissor = bufferGroup.FullScreenScissor,
                    TilesBasePtr = (Tile*) bufferGroup.Tiles.GetUnsafePtr(),
                    TilesPerBinX = TilesPerBinX,
                    TilesPerBinY = TilesPerBinY,
                }.ScheduleParallel(numBins, 1, JobHandle.CombineDependencies(clearJob, sortJob));
    #if WAIT_FOR_EACH_JOB
                rasterizeJob.Complete();
    #endif // WAIT_FOR_EACH_JOB
                s_Rasterize.End();
            }else
            {
                rasterizeJob = JobHandle.CombineDependencies(clearJob, sortJob);
            }

            /* Test
               ---- */
            s_Test.Begin();

            var testJob = new TestJob
            {
                VisibilityItems = visibilityItems,
                ChunkHeader = entityManager.GetComponentTypeHandle<ChunkHeader>(true),
                OcclusionTest = entityManager.GetComponentTypeHandle<OcclusionTest>(true),
                ChunkOcclusionTest = entityManager.GetComponentTypeHandle<ChunkOcclusionTest>(true),
                EntitiesGraphicsChunkInfo = entityManager.GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(false),
                ProjectionType = bufferGroup.ProjectionType,
                NumTilesX = bufferGroup.NumTilesX,
                HalfSize = bufferGroup.HalfSize,
                PixelCenter = bufferGroup.PixelCenter,
                ScreenSize = bufferGroup.ScreenSize,
                ViewType = viewType,
                SplitIndex = splitIndex,
                Tiles = (Tile*)bufferGroup.Tiles.GetUnsafePtr(),
#if UNITY_EDITOR
                DisplayOnlyOccluded = InvertOcclusion,
#endif
            }.ScheduleWithIndirectList(visibilityItems, 1, JobHandle.CombineDependencies(rasterizeJob, computeBoundsJob));
#if WAIT_FOR_EACH_JOB
            testJob.Complete();
#endif // WAIT_FOR_EACH_JOB
            s_Test.End();
            s_Cull.End();

            bufferGroup.RenderToTextures(m_ReadonlyTestQuery, m_ReadonlyMeshQuery, testJob, debugSettings.debugRenderMode);
            meshes.Dispose(rasterizeJob);
            return testJob;
        }

        internal JobHandle Cull(
            EntityManager entityManager,
            BatchCullingContext cullingContext,
            JobHandle cullingJobDependency,
            IndirectList<ChunkVisibilityItem> visibilityItems
#if UNITY_EDITOR
            , EntitiesGraphicsPerThreadStats* cullingStats
#endif
        )
        {
            if (World.DefaultGameObjectInjectionWorld == null)
            {
                return new JobHandle();
            }

            if (!IsEnabled)
                return new JobHandle();

#if WAIT_FOR_EACH_JOB
            cullingJobDependency.Complete();
#endif // WAIT_FOR_EACH_JOB

            JobHandle combinedHandle = new JobHandle();
            int instanceID = cullingContext.viewID.GetInstanceID();
            ulong? pinnedViewID = debugSettings.GetPinnedViewID();

            for (int i = 0; i < cullingContext.cullingSplits.Length; i++)
            {
                // Pack the instance ID and the split index into a 64-bit value
                ulong viewID = (uint)instanceID | ((ulong)i << 32);

                // Add a buffer-group for the current view if one doesn't already exist
                if (!BufferGroups.TryGetValue(viewID, out var bufferGroup))
                {
                    bufferGroup = new BufferGroup(cullingContext.viewType);
                    BufferGroups[viewID] = bufferGroup;
                    debugSettings.RefreshViews(BufferGroups);
                }

                if (pinnedViewID.HasValue && BufferGroups.TryGetValue(pinnedViewID.Value, out var pinnedBufferGroup))
                {
                    // ^ A view is pinned. Use that view's buffer group instead of the view that's currently being
                    // drawn. This allows us to experience culling from the pinned view's perspective.
                    bufferGroup = pinnedBufferGroup;
                }

                if (!debugSettings.freezeOcclusion && (!pinnedViewID.HasValue || pinnedViewID.Value == viewID))
                {
                    // Update the buffer-group's view-related parameters
                    s_SetResolution.Begin();
                    bufferGroup.SetResolutionAndClip(
                        m_MOCDepthSize,
                        m_MOCDepthSize,
                        cullingContext.projectionType,
                        cullingContext.cullingSplits[i].nearPlane
                    );
                    bufferGroup.CullingMatrix = cullingContext.cullingSplits[i].cullingMatrix;
                    s_SetResolution.End();
                }

                bool invertOcclusion = debugSettings.debugRenderMode == DebugRenderMode.Inverted;

                JobHandle viewJob = CullView(
                    bufferGroup,
                    i,
                    /* TODO: Remove this dependency to run views asynchronously. To enable this, we will need to move
                     the data out of occlusion test components. Since currently there is only up to one occlusion test
                     component on an entity, it can only be culled from one view at a time. */
                    JobHandle.CombineDependencies(cullingJobDependency, combinedHandle),
                    entityManager,
                    visibilityItems,
                    invertOcclusion,
                    cullingContext.viewType
                );
                combinedHandle = JobHandle.CombineDependencies(combinedHandle, viewJob);
            }

            return combinedHandle;
        }

        private EntityQuery m_OcclusionTestTransformGroup;
        private EntityQuery m_OcclusionTestGroup;

        static readonly int m_MOCDepthSize = 512;

    }
}

#endif
