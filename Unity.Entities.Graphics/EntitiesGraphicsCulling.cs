// #define DISABLE_HYBRID_SPHERE_CULLING
// #define DISABLE_HYBRID_RECEIVER_CULLING
// #define DISABLE_INCLUDE_EXCLUDE_LIST_FILTERING
// #define DEBUG_VALIDATE_VISIBLE_COUNTS
// #define DEBUG_VALIDATE_COMBINED_SPLIT_RECEIVER_CULLING
// #define DEBUG_VALIDATE_SOA_SPHERE_TEST
// #define DEBUG_VALIDATE_EXTRA_SPLITS

#if UNITY_EDITOR
using UnityEditor;
#endif

using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Batch-oriented culling.
 *
 * This culling approach oriented from Megacity and works well for relatively
 * slow-moving cameras in a large, dense environment.
 *
 * The primary CPU costs involved in culling all the chunks of mesh instances
 * in megacity is touching the chunks of memory. A naive culling approach would
 * look like this:
 *
 *     for each chunk:
 *       select what instances should be enabled based on camera position (lod selection)
 *
 *     for each frustum:
 *       for each chunk:
 *         if the chunk is completely out of the frustum:
 *           discard
 *         else:
 *           for each instance in the chunk:
 *             if the instance is inside the frustum:
 *               write index of instance to output index buffer
 *
 * The approach implemented here does essentially this, but has been optimized
 * so that chunks need to be accessed as infrequently as possible:
 *
 * - Because the chunks are static, we can cache bounds information outside the chunks
 *
 * - Because the camera moves relatively slowly, we can compute a grace
 *   distance which the camera has to move (in any direction) before the LOD
 *   selection would compute a different result
 *
 * - Because only a some chunks straddle the frustum boundaries, we can treat
 *   them as "in" rather than "partial" to save touching their chunk memory
 */

namespace Unity.Rendering
{
    [BurstCompile]
    internal unsafe struct SelectLodEnabled : IJobChunk
    {
        [ReadOnly] public LODGroupExtensions.LODParams LODParams;
        [ReadOnly] public NativeList<byte> ForceLowLOD;
        [ReadOnly] public ComponentTypeHandle<RootLODRange> RootLODRanges;
        [ReadOnly] public ComponentTypeHandle<RootLODWorldReferencePoint> RootLODReferencePoints;
        [ReadOnly] public ComponentTypeHandle<LODRange> LODRanges;
        [ReadOnly] public ComponentTypeHandle<LODWorldReferencePoint> LODReferencePoints;
        public ushort CameraMoveDistanceFixed16;
        public float DistanceScale;
        public bool DistanceScaleChanged;

        public ComponentTypeHandle<EntitiesGraphicsChunkInfo> EntitiesGraphicsChunkInfo;
        [ReadOnly] public ComponentTypeHandle<ChunkHeader> ChunkHeader;

#if UNITY_EDITOR
        [NativeDisableUnsafePtrRestriction] public EntitiesGraphicsPerThreadStats* Stats;

#pragma warning disable 649
        [NativeSetThreadIndex] public int ThreadIndex;
#pragma warning restore 649

#endif

        public void Execute(in ArchetypeChunk archetypeChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            // This job is not written to support queries with enableable component types.
            Assert.IsFalse(useEnabledMask);

            var entitiesGraphicsChunkInfoArray = archetypeChunk.GetNativeArray(ref EntitiesGraphicsChunkInfo);
            var chunkHeaderArray = archetypeChunk.GetNativeArray(ref ChunkHeader);

#if UNITY_EDITOR
            ref var stats = ref Stats[ThreadIndex];
#endif

            for (int entityIndex = 0, chunkEntityCount = archetypeChunk.Count; entityIndex < chunkEntityCount; entityIndex++)
            {
                var entitiesGraphicsChunkInfo = entitiesGraphicsChunkInfoArray[entityIndex];
                if (!entitiesGraphicsChunkInfo.Valid)
                    continue;

                var chunkHeader = chunkHeaderArray[entityIndex];

#if UNITY_EDITOR
                stats.LodTotal++;
#endif
                var batchIndex = entitiesGraphicsChunkInfo.BatchIndex;
                var chunkInstanceCount = chunkHeader.ArchetypeChunk.Count;
                var isOrtho = LODParams.isOrtho;

                ref var chunkCullingData = ref entitiesGraphicsChunkInfo.CullingData;
                ChunkInstanceLodEnabled chunkEntityLodEnabled = chunkCullingData.InstanceLodEnableds;

#if UNITY_EDITOR
                ChunkInstanceLodEnabled oldEntityLodEnabled = chunkEntityLodEnabled;
#endif
                var forceLowLOD = ForceLowLOD[batchIndex];

                if (0 == (chunkCullingData.Flags & EntitiesGraphicsChunkCullingData.kFlagHasLodData))
                {
#if UNITY_EDITOR
                    stats.LodNoRequirements++;
#endif
                    chunkEntityLodEnabled.Enabled[0] = 0;
                    chunkEntityLodEnabled.Enabled[1] = 0;
                    chunkCullingData.ForceLowLODPrevious = forceLowLOD;

                    for (int i = 0; i < chunkInstanceCount; ++i)
                    {
                        int wordIndex = i >> 6;
                        int bitIndex = i & 63;
                        chunkEntityLodEnabled.Enabled[wordIndex] |= 1ul << bitIndex;
                    }
                }
                else
                {
                    int diff = (int)chunkCullingData.MovementGraceFixed16 - CameraMoveDistanceFixed16;
                    chunkCullingData.MovementGraceFixed16 = (ushort)math.max(0, diff);

                    var graceExpired = chunkCullingData.MovementGraceFixed16 == 0;
                    var forceLodChanged = forceLowLOD != chunkCullingData.ForceLowLODPrevious;

                    if (graceExpired || forceLodChanged || DistanceScaleChanged)
                    {
                        chunkEntityLodEnabled.Enabled[0] = 0;
                        chunkEntityLodEnabled.Enabled[1] = 0;

#if UNITY_EDITOR
                        stats.LodChunksTested++;
#endif
                        var chunk = chunkHeader.ArchetypeChunk;

                        var rootLODRanges = chunk.GetNativeArray(ref RootLODRanges);
                        var rootLODReferencePoints = chunk.GetNativeArray(ref RootLODReferencePoints);
                        var lodRanges = chunk.GetNativeArray(ref LODRanges);
                        var lodReferencePoints = chunk.GetNativeArray(ref LODReferencePoints);

                        float graceDistance = float.MaxValue;

                        for (int i = 0; i < chunkInstanceCount; i++)
                        {
                            var rootLODRange = rootLODRanges[i];
                            var rootLODReferencePoint = rootLODReferencePoints[i];

                            var rootLodDistance =
                                math.select(
                                    DistanceScale *
                                    math.length(LODParams.cameraPos - rootLODReferencePoint.Value),
                                    DistanceScale, isOrtho);

                            float rootMinDist = math.select(rootLODRange.LOD.MinDist, 0.0f, forceLowLOD == 1);
                            float rootMaxDist = rootLODRange.LOD.MaxDist;

                            graceDistance = math.min(math.abs(rootLodDistance - rootMinDist), graceDistance);
                            graceDistance = math.min(math.abs(rootLodDistance - rootMaxDist), graceDistance);

                            var rootLodIntersect = (rootLodDistance < rootMaxDist) && (rootLodDistance >= rootMinDist);

                            if (rootLodIntersect)
                            {
                                var lodRange = lodRanges[i];
                                var lodReferencePoint = lodReferencePoints[i];

                                var instanceDistance =
                                    math.select(
                                        DistanceScale *
                                        math.length(LODParams.cameraPos -
                                            lodReferencePoint.Value), DistanceScale,
                                        isOrtho);

                                var instanceLodIntersect =
                                    (instanceDistance < lodRange.MaxDist) &&
                                    (instanceDistance >= lodRange.MinDist);

                                graceDistance = math.min(math.abs(instanceDistance - lodRange.MinDist),
                                    graceDistance);
                                graceDistance = math.min(math.abs(instanceDistance - lodRange.MaxDist),
                                    graceDistance);

                                if (instanceLodIntersect)
                                {
                                    var index = i;
                                    var wordIndex = index >> 6;
                                    var bitIndex = index & 0x3f;
                                    var lodWord = chunkEntityLodEnabled.Enabled[wordIndex];

                                    lodWord |= 1UL << bitIndex;
                                    chunkEntityLodEnabled.Enabled[wordIndex] = lodWord;
                                }
                            }
                        }

                        chunkCullingData.MovementGraceFixed16 = Fixed16CamDistance.FromFloatFloor(graceDistance);
                        chunkCullingData.ForceLowLODPrevious = forceLowLOD;
                    }
                }


#if UNITY_EDITOR
                if (oldEntityLodEnabled.Enabled[0] != chunkEntityLodEnabled.Enabled[0] ||
                    oldEntityLodEnabled.Enabled[1] != chunkEntityLodEnabled.Enabled[1])
                {
                    stats.LodChanged++;
                }
#endif

                chunkCullingData.InstanceLodEnableds = chunkEntityLodEnabled;
                entitiesGraphicsChunkInfoArray[entityIndex] = entitiesGraphicsChunkInfo;
            }
        }
    }

    internal unsafe struct ChunkVisibility
    {
        public fixed ulong VisibleEntities[2];
        public fixed byte SplitMasks[128];

        public bool AnyVisible => (VisibleEntities[0] | VisibleEntities[1]) != 0;
    }

    internal unsafe struct ChunkVisibilityItem
    {
        public ArchetypeChunk Chunk;
        public ChunkVisibility* Visibility;
    }

    internal static class CullingExtensions
    {
        // We want to use UnsafeList to use RewindableAllocator, but PlanePacket APIs want NativeArrays
        internal static unsafe NativeArray<T> AsNativeArray<T>(this UnsafeList<T> list) where T : unmanaged
        {
            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(list.Ptr, list.Length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            return array;
        }

        internal static NativeArray<T> GetSubNativeArray<T>(this UnsafeList<T> list, int start, int length)
            where T : unmanaged =>
            list.AsNativeArray().GetSubArray(start, length);
    }

    internal unsafe struct CullingSplits
    {
        public UnsafeList<Plane> BackfacingReceiverPlanes;
        public UnsafeList<FrustumPlanes.PlanePacket4> SplitPlanePackets;
        public UnsafeList<FrustumPlanes.PlanePacket4> ReceiverPlanePackets;
        public UnsafeList<FrustumPlanes.PlanePacket4> CombinedSplitAndReceiverPlanePackets;
        public UnsafeList<CullingSplitData> Splits;
        public SOASphereTest SplitSOASphereTest;

        public float3 LightAxisX;
        public float3 LightAxisY;
        public bool SphereTestEnabled;

        public static CullingSplits Create(BatchCullingContext* cullingContext, ShadowProjection shadowProjection, AllocatorManager.AllocatorHandle allocator)
        {
            CullingSplits cullingSplits = default;

            var createJob = new CreateJob
            {
                cullingContext = cullingContext,
                shadowProjection = shadowProjection,
                allocator = allocator,
                Splits = &cullingSplits
            };
            createJob.Run();

            return cullingSplits;
        }

        [BurstCompile]
        private struct CreateJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            [ReadOnly] public BatchCullingContext* cullingContext;
            [ReadOnly] public ShadowProjection shadowProjection;
            [ReadOnly] public AllocatorManager.AllocatorHandle allocator;

            [NativeDisableUnsafePtrRestriction]
            public CullingSplits* Splits;

            public void Execute()
            {
                *Splits = new CullingSplits(ref *cullingContext, shadowProjection, allocator);
            }
        }

        private CullingSplits(ref BatchCullingContext cullingContext,
            ShadowProjection shadowProjection,
            AllocatorManager.AllocatorHandle allocator)
        {
            BackfacingReceiverPlanes = default;
            SplitPlanePackets = default;
            ReceiverPlanePackets = default;
            CombinedSplitAndReceiverPlanePackets = default;
            Splits = default;
            SplitSOASphereTest = default;

            LightAxisX = default;
            LightAxisY = default;
            SphereTestEnabled = false;

            // Initialize receiver planes first, so they are ready to be combined in
            // InitializeSplits
            InitializeReceiverPlanes(ref cullingContext, allocator);
            InitializeSplits(ref cullingContext, allocator);
            InitializeSphereTest(ref cullingContext, shadowProjection, allocator);
        }

        private void InitializeReceiverPlanes(ref BatchCullingContext cullingContext, AllocatorManager.AllocatorHandle allocator)
        {
#if DISABLE_HYBRID_RECEIVER_CULLING
            bool disableReceiverCulling = true;
#else
            bool disableReceiverCulling = false;
#endif
            // Receiver culling is only used for shadow maps
            if ((cullingContext.viewType != BatchCullingViewType.Light) ||
                (cullingContext.receiverPlaneCount == 0) ||
                disableReceiverCulling)
            {
                // Make an empty array so job system doesn't complain.
                ReceiverPlanePackets = new UnsafeList<FrustumPlanes.PlanePacket4>(0, allocator);
                return;
            }

            bool isOrthographic = cullingContext.projectionType == BatchCullingProjectionType.Orthographic;
            int numPlanes = 0;

            var planes = cullingContext.cullingPlanes.GetSubArray(
                cullingContext.receiverPlaneOffset,
                cullingContext.receiverPlaneCount);
            BackfacingReceiverPlanes = new UnsafeList<Plane>(planes.Length, allocator);
            BackfacingReceiverPlanes.Resize(planes.Length);

            float3 lightDir = ((float4)cullingContext.localToWorldMatrix.GetColumn(2)).xyz;
            Vector3 lightPos = cullingContext.localToWorldMatrix.GetPosition();

            for (int i = 0; i < planes.Length; ++i)
            {
                var p = planes[i];
                float3 n = p.normal;

                const float kEpsilon = (float)1e-12;

                // Compare with epsilon so that perpendicular planes are not counted
                // as back facing
                bool isBackfacing = isOrthographic
                    ? math.dot(n, lightDir) < -kEpsilon
                    : p.GetSide(lightPos);

                if (isBackfacing)
                {
                    BackfacingReceiverPlanes[numPlanes] = p;
                    ++numPlanes;
                }
            }

            ReceiverPlanePackets = FrustumPlanes.BuildSOAPlanePackets(
                BackfacingReceiverPlanes.GetSubNativeArray(0, numPlanes),
                allocator);
            BackfacingReceiverPlanes.Resize(numPlanes);
        }

#if DEBUG_VALIDATE_EXTRA_SPLITS
        private static int s_DebugExtraSplitsCounter = 0;
#endif

        private void InitializeSplits(ref BatchCullingContext cullingContext, AllocatorManager.AllocatorHandle allocator)
        {
            var cullingPlanes = cullingContext.cullingPlanes;
            var cullingSplits = cullingContext.cullingSplits;

            int numSplits = cullingSplits.Length;

#if DEBUG_VALIDATE_EXTRA_SPLITS
            // If extra splits validation is enabled, pad the split number so it's between 5 and 8 by copying existing
            // splits, to ensure that the code functions correctly with higher split counts.
            if (numSplits > 1 && numSplits < 5)
            {
                numSplits = 5 + s_DebugExtraSplitsCounter;
                s_DebugExtraSplitsCounter = (s_DebugExtraSplitsCounter + 1) % 4;
            }
#endif

            Debug.Assert(numSplits > 0, "No culling splits provided, expected at least 1");
            Debug.Assert(numSplits <= 8, "Split count too high, only up to 8 splits supported");

            int planePacketCount = 0;
            int combinedPlanePacketCount = 0;
            for (int i = 0; i < numSplits; ++i)
            {
                int splitIndex = i;
#if DEBUG_VALIDATE_EXTRA_SPLITS
                splitIndex %= cullingSplits.Length;
#endif

                planePacketCount += (cullingSplits[splitIndex].cullingPlaneCount + 3) / 4;
                combinedPlanePacketCount +=
                    ((cullingSplits[splitIndex].cullingPlaneCount + BackfacingReceiverPlanes.Length) + 3) / 4;
            }

            SplitPlanePackets = new UnsafeList<FrustumPlanes.PlanePacket4>(planePacketCount, allocator);
            CombinedSplitAndReceiverPlanePackets = new UnsafeList<FrustumPlanes.PlanePacket4>(combinedPlanePacketCount, allocator);
            Splits = new UnsafeList<CullingSplitData>(numSplits, allocator);

            var combinedPlanes = new UnsafeList<Plane>(combinedPlanePacketCount * 4, allocator);

            int planeIndex = 0;
            int combinedPlaneIndex = 0;

            for (int i = 0; i < numSplits; ++i)
            {
                int splitIndex = i;
#if DEBUG_VALIDATE_EXTRA_SPLITS
                splitIndex %= cullingSplits.Length;
#endif

                var s = cullingSplits[splitIndex];
                float3 p = s.sphereCenter;
                float r = s.sphereRadius;

                if (s.sphereRadius <= 0)
                    r = 0;

                var splitCullingPlanes = cullingPlanes.GetSubArray(s.cullingPlaneOffset, s.cullingPlaneCount);

                var planePackets = FrustumPlanes.BuildSOAPlanePackets(
                    splitCullingPlanes,
                    allocator);

                foreach (var pp in planePackets)
                    SplitPlanePackets.Add(pp);

                combinedPlanes.Resize(splitCullingPlanes.Length + BackfacingReceiverPlanes.Length);

                // Make combined packets that have both the split planes and the receiver planes so
                // they can be tested simultaneously
                UnsafeUtility.MemCpy(
                    combinedPlanes.Ptr,
                    splitCullingPlanes.GetUnsafeReadOnlyPtr(),
                    splitCullingPlanes.Length * UnsafeUtility.SizeOf<Plane>());
                UnsafeUtility.MemCpy(
                    combinedPlanes.Ptr + splitCullingPlanes.Length,
                    BackfacingReceiverPlanes.Ptr,
                    BackfacingReceiverPlanes.Length * UnsafeUtility.SizeOf<Plane>());

                var combined = FrustumPlanes.BuildSOAPlanePackets(
                    combinedPlanes.AsNativeArray(),
                    allocator);

                foreach (var pp in combined)
                    CombinedSplitAndReceiverPlanePackets.Add(pp);

                Splits.Add(new CullingSplitData
                {
                    CullingSphereCenter = p,
                    CullingSphereRadius = r,
                    PlanePacketOffset = planeIndex,
                    PlanePacketCount = planePackets.Length,
                    CombinedPlanePacketOffset = combinedPlaneIndex,
                    CombinedPlanePacketCount = combined.Length,
                });

                planeIndex += planePackets.Length;
                combinedPlaneIndex += combined.Length;
            }
        }

        private void InitializeSphereTest(ref BatchCullingContext cullingContext, ShadowProjection shadowProjection, AllocatorManager.AllocatorHandle allocator)
        {
            // Receiver sphere testing is only enabled if the cascade projection is stable
            bool projectionIsStable = shadowProjection == ShadowProjection.StableFit;
            bool allSplitsHaveValidReceiverSpheres = true;
            for (int i = 0; i < Splits.Length; ++i)
            {
                // This should also catch NaNs, which return false
                // for every comparison.
                if (!(Splits[i].CullingSphereRadius > 0))
                {
                    allSplitsHaveValidReceiverSpheres = false;
                    break;
                }
            }

            if (projectionIsStable && allSplitsHaveValidReceiverSpheres)
            {
                LightAxisX = new float4(cullingContext.localToWorldMatrix.GetColumn(0)).xyz;
                LightAxisY = new float4(cullingContext.localToWorldMatrix.GetColumn(1)).xyz;

                SplitSOASphereTest = new SOASphereTest(ref this, allocator);

                SphereTestEnabled = true;
            }
        }

        public float2 TransformToLightSpaceXY(float3 positionWS) => new float2(
            math.dot(positionWS, LightAxisX),
            math.dot(positionWS, LightAxisY));
    }

    internal unsafe struct CullingSplitData
    {
        public float3 CullingSphereCenter;
        public float CullingSphereRadius;
        public int PlanePacketOffset;
        public int PlanePacketCount;
        public int CombinedPlanePacketOffset;
        public int CombinedPlanePacketCount;
    }

    [BurstCompile]
    internal unsafe struct IncludeExcludeListFilter
    {
#if !DISABLE_INCLUDE_EXCLUDE_LIST_FILTERING
        public NativeParallelHashSet<int> IncludeEntityIndices;
        public NativeParallelHashSet<int> ExcludeEntityIndices;
        public bool IsIncludeEnabled;
        public bool IsExcludeEnabled;

        public bool IsEnabled => IsIncludeEnabled || IsExcludeEnabled;
        public bool IsIncludeEmpty => IncludeEntityIndices.IsEmpty;
        public bool IsExcludeEmpty => ExcludeEntityIndices.IsEmpty;

        public IncludeExcludeListFilter(
            EntityManager entityManager,
            NativeArray<int> includeEntityIndices,
            NativeArray<int> excludeEntityIndices,
            Allocator allocator)
        {
            IncludeEntityIndices = default;
            ExcludeEntityIndices = default;

            // Null NativeArray means that the list shoudln't be used for filtering
            IsIncludeEnabled = includeEntityIndices.IsCreated;
            IsExcludeEnabled = excludeEntityIndices.IsCreated;

            if (IsIncludeEnabled)
            {
                IncludeEntityIndices = new NativeParallelHashSet<int>(includeEntityIndices.Length, allocator);
                for (int i = 0; i < includeEntityIndices.Length; ++i)
                    IncludeEntityIndices.Add(includeEntityIndices[i]);
            }
            else
            {
                // NativeParallelHashSet must be non-null even if empty to be passed to jobs. Otherwise errors happen.
                IncludeEntityIndices = new NativeParallelHashSet<int>(0, allocator);
            }

            if (IsExcludeEnabled)
            {
                ExcludeEntityIndices = new NativeParallelHashSet<int>(excludeEntityIndices.Length, allocator);
                for (int i = 0; i < excludeEntityIndices.Length; ++i)
                    ExcludeEntityIndices.Add(excludeEntityIndices[i]);
            }
            else
            {
                // NativeParallelHashSet must be non-null even if empty to be passed to jobs. Otherwise errors happen.
                ExcludeEntityIndices = new NativeParallelHashSet<int>(0, allocator);
            }
        }

        public void Dispose()
        {
            if (IncludeEntityIndices.IsCreated)
                IncludeEntityIndices.Dispose();

            if (ExcludeEntityIndices.IsCreated)
                ExcludeEntityIndices.Dispose();
        }

        public JobHandle Dispose(JobHandle dependencies)
        {
            JobHandle disposeInclude = IncludeEntityIndices.IsCreated ? IncludeEntityIndices.Dispose(dependencies) : default;
            JobHandle disposeExclude = ExcludeEntityIndices.IsCreated ? ExcludeEntityIndices.Dispose(dependencies) : default;
            return JobHandle.CombineDependencies(disposeInclude, disposeExclude);
        }

        public bool EntityPassesFilter(int entityIndex)
        {
            if (IsIncludeEnabled)
            {
                if (!IncludeEntityIndices.Contains(entityIndex))
                    return false;
            }

            if (IsExcludeEnabled)
            {
                if (ExcludeEntityIndices.Contains(entityIndex))
                    return false;
            }

            return true;
        }
#else
        public bool IsIncludeEnabled => false;
        public bool IsExcludeEnabled => false;
        public bool IsEnabled => false;
        public bool IsIncludeEmpty => true;
        public bool IsExcludeEmpty => true;
        public bool EntityPassesFilter(int entityIndex) => true;
        public void Dispose() { }
        public JobHandle Dispose(JobHandle dependencies) => new JobHandle();
#endif
    }

    [BurstCompile]
    internal unsafe struct FrustumCullingJob : IJobChunk
    {
        public IndirectList<ChunkVisibilityItem> VisibilityItems;
        public ThreadLocalAllocator ThreadLocalAllocator;

        [ReadOnly] public CullingSplits Splits;

        [ReadOnly] public ComponentTypeHandle<WorldRenderBounds> BoundsComponent;
        [ReadOnly] public ComponentTypeHandle<EntitiesGraphicsChunkInfo> EntitiesGraphicsChunkInfo;
        [ReadOnly] public ComponentTypeHandle<ChunkWorldRenderBounds> ChunkWorldRenderBounds;

        [ReadOnly] public IncludeExcludeListFilter IncludeExcludeListFilter;
        [ReadOnly] public EntityTypeHandle EntityHandle;

        public BatchCullingViewType CullingViewType;

#pragma warning disable 649
        [NativeSetThreadIndex] public int ThreadIndex;
#pragma warning restore 649

#if UNITY_EDITOR
        [NativeDisableUnsafePtrRestriction] public EntitiesGraphicsPerThreadStats* Stats;
#endif

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            // This job is not written to support queries with enableable component types.
            Assert.IsFalse(useEnabledMask);

            var allocator = ThreadLocalAllocator.ThreadAllocator(ThreadIndex);
            var visibilityItemWriter = VisibilityItems.List->AsParallelWriter();
            ChunkVisibility chunkVisibility;

            bool isLightCulling = CullingViewType == BatchCullingViewType.Light;

            var entitiesGraphicsChunkInfo = chunk.GetChunkComponentData(ref EntitiesGraphicsChunkInfo);
            if (!entitiesGraphicsChunkInfo.Valid)
                return;

            var chunkBounds = chunk.GetChunkComponentData(ref ChunkWorldRenderBounds);

#if UNITY_EDITOR
            ref var stats = ref Stats[ThreadIndex];
            stats.ChunkTotal++;
#else
            var stats = new EntitiesGraphicsPerThreadStats{};
#endif

            ref var chunkCullingData = ref entitiesGraphicsChunkInfo.CullingData;
            var chunkEntityLodEnabled = chunkCullingData.InstanceLodEnableds;
            var anyLodEnabled = (chunkEntityLodEnabled.Enabled[0] | chunkEntityLodEnabled.Enabled[1]) != 0;

            chunkVisibility.VisibleEntities[0] = 0;
            chunkVisibility.VisibleEntities[1] = 0;

            var perInstanceCull = 0 != (chunkCullingData.Flags & EntitiesGraphicsChunkCullingData.kFlagInstanceCulling);

            if (anyLodEnabled)
            {
                stats.ChunkCountAnyLod++;
                if (isLightCulling)
                {
                    bool useSphereTest = Splits.SphereTestEnabled;
#if DISABLE_HYBRID_SPHERE_CULLING
                    useSphereTest = false;
#endif
                    FrustumCullWithReceiverAndSphereCulling(chunkBounds, chunk, chunkEntityLodEnabled,
                        perInstanceCull, &chunkVisibility, ref stats,
                        useSphereTest: useSphereTest);
                }
                else
                    FrustumCull(chunkBounds, chunk, chunkEntityLodEnabled, perInstanceCull, &chunkVisibility, ref stats);

                if (chunkVisibility.AnyVisible)
                {
                    var visibilityItem = new ChunkVisibilityItem
                    {
                        Chunk = chunk,
                        Visibility = allocator->Allocate(chunkVisibility, 1),
                    };
                    UnsafeUtility.MemCpy(visibilityItem.Visibility, &chunkVisibility, UnsafeUtility.SizeOf<ChunkVisibility>());
                    visibilityItemWriter.AddNoResize(visibilityItem);
                }
            }
        }

        private void FrustumCull(ChunkWorldRenderBounds chunkBounds,
            ArchetypeChunk chunk,
            ChunkInstanceLodEnabled chunkEntityLodEnabled,
            bool perInstanceCull,
            ChunkVisibility* chunkVisibility,
            ref EntitiesGraphicsPerThreadStats stats)
        {
            Debug.Assert(Splits.Splits.Length == 1);

            var chunkIn = perInstanceCull
                ? FrustumPlanes.Intersect2(Splits.SplitPlanePackets.AsNativeArray(), chunkBounds.Value)
                : FrustumPlanes.Intersect2NoPartial(Splits.SplitPlanePackets.AsNativeArray(), chunkBounds.Value);

            // Have to filter all entities separately if the filter is enabled
            if (IncludeExcludeListFilter.IsEnabled && chunkIn == FrustumPlanes.IntersectResult.In)
                chunkIn = FrustumPlanes.IntersectResult.Partial;

            if (chunkIn == FrustumPlanes.IntersectResult.Partial)
            {
#if UNITY_EDITOR
                int instanceTestCount = 0;
#endif
                var chunkInstanceBounds = chunk.GetNativeArray(ref BoundsComponent);
                var chunkEntities = chunk.GetNativeArray(EntityHandle);

                for (int j = 0; j < 2; j++)
                {
                    var lodWord = chunkEntityLodEnabled.Enabled[j];
                    ulong visibleWord = 0;

                    while (lodWord != 0)
                    {
                        var bitIndex = math.tzcnt(lodWord);
                        var finalIndex = (j << 6) + bitIndex;

                        int visible = FrustumPlanes.Intersect2NoPartial(Splits.SplitPlanePackets.AsNativeArray(), chunkInstanceBounds[finalIndex].Value) !=
                                      FrustumPlanes.IntersectResult.Out
                            ? 1
                            : 0;

                        if (IncludeExcludeListFilter.IsEnabled && visible != 0)
                        {
                            if (!IncludeExcludeListFilter.EntityPassesFilter(chunkEntities[finalIndex].Index))
                                visible = 0;
                        }

                        lodWord ^= 1ul << bitIndex;
                        visibleWord |= ((ulong)visible) << bitIndex;

#if UNITY_EDITOR
                        instanceTestCount++;
#endif
                    }

                    chunkVisibility->VisibleEntities[j] = visibleWord;
                }

#if UNITY_EDITOR
                stats.ChunkCountInstancesProcessed++;
                stats.InstanceTests += instanceTestCount;
#endif
            }
            else if (chunkIn == FrustumPlanes.IntersectResult.In)
            {
#if UNITY_EDITOR
                stats.ChunkCountFullyIn++;
#endif
                for (int j = 0; j < 2; j++)
                {
                    var lodWord = chunkEntityLodEnabled.Enabled[j];
                    chunkVisibility->VisibleEntities[j] = lodWord;
                }
            }
            else if (chunkIn == FrustumPlanes.IntersectResult.Out)
            {
                // No need to do anything
            }
        }

        private void FrustumCullWithReceiverAndSphereCulling(
            ChunkWorldRenderBounds chunkBounds,
            ArchetypeChunk chunk,
            ChunkInstanceLodEnabled chunkEntityLodEnabled,
            bool perInstanceCull,
            ChunkVisibility* chunkVisibility,
            ref EntitiesGraphicsPerThreadStats stats,
            bool useSphereTest)
        {
            int numEntities = chunk.Count;

            ref var receiverPlanes = ref Splits.ReceiverPlanePackets;

            bool haveReceiverPlanes = Splits.ReceiverPlanePackets.Length > 0;
            // Do chunk receiver test first, since it doesn't consider splits
            if (haveReceiverPlanes)
            {
                if (FrustumPlanes.Intersect2NoPartial(receiverPlanes.AsNativeArray(), chunkBounds.Value) ==
                    FrustumPlanes.IntersectResult.Out)
                    return;
            }

            // Initially set zero split mask for every entity in the chunk
            UnsafeUtility.MemSet(chunkVisibility->SplitMasks, 0, numEntities);

            ref var splits = ref Splits.Splits;

            var worldRenderBounds = chunk.GetNativeArray(ref BoundsComponent);

            // First, perform frustum and receiver plane culling for all splits
            for (int splitIndex = 0; splitIndex < splits.Length; ++splitIndex)
            {
                var s = splits[splitIndex];

                byte splitMask = (byte)(1 << splitIndex);

                var splitPlanes = Splits.SplitPlanePackets.GetSubNativeArray(
                    s.PlanePacketOffset,
                    s.PlanePacketCount);
                var combinedSplitPlanes = Splits.CombinedSplitAndReceiverPlanePackets.GetSubNativeArray(
                    s.CombinedPlanePacketOffset,
                    s.CombinedPlanePacketCount);

                float2 receiverSphereLightSpace = Splits.TransformToLightSpaceXY(s.CullingSphereCenter);

                // If the entire chunk fails the sphere test, no need to consider further
                if (useSphereTest && SphereTest(s, chunkBounds.Value, receiverSphereLightSpace) == SphereTestResult.CannotCastShadow)
                    continue;

                var chunkIn = perInstanceCull
                    ? FrustumPlanes.Intersect2(splitPlanes, chunkBounds.Value)
                    : FrustumPlanes.Intersect2NoPartial(splitPlanes, chunkBounds.Value);

                if (chunkIn == FrustumPlanes.IntersectResult.Partial)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        ulong lodWord = chunkEntityLodEnabled.Enabled[j];
                        ulong visibleWord = 0;

                        while (lodWord != 0)
                        {
                            var bitIndex = math.tzcnt(lodWord);
                            var entityIndex = (j << 6) + bitIndex;
                            ulong mask = 1ul << bitIndex;

                            var bounds = worldRenderBounds[entityIndex].Value;

                            int visible =
                                FrustumPlanes.Intersect2NoPartial(combinedSplitPlanes, bounds) != FrustumPlanes.IntersectResult.Out
                                    ? 1
                                    : 0;

#if DEBUG_VALIDATE_COMBINED_SPLIT_RECEIVER_CULLING
                            bool visibleFrustum = FrustumPlanes.Intersect2NoPartial(splitPlanes, bounds) != FrustumPlanes.IntersectResult.Out;
                            bool visibleReceiver = FrustumPlanes.Intersect2NoPartial(receiverPlanes.AsNativeArray(), bounds) != FrustumPlanes.IntersectResult.Out;
                            int visibleReference = (visibleFrustum && visibleReceiver) ? 1 : 0;
                            // Use Debug.Log instead of Debug.Assert so that Burst does not remove it
                            if (visible != visibleReference)
                                Debug.Log($"Combined Split+Receiver ({visible}) plane culling mismatch with separate Split ({visibleFrustum}) and Receiver ({visibleReceiver})");
#endif

                            lodWord ^= mask;
                            visibleWord |= ((ulong)visible) << bitIndex;

                            if (visible != 0)
                                chunkVisibility->SplitMasks[entityIndex] |= splitMask;
                        }

                        chunkVisibility->VisibleEntities[j] |= visibleWord;
                    }
                }
                else if (chunkIn == FrustumPlanes.IntersectResult.In)
                {
                    // VisibleEntities contains the union of all splits, so enable bits
                    // for this split
                    chunkVisibility->VisibleEntities[0] |= chunkEntityLodEnabled.Enabled[0];
                    chunkVisibility->VisibleEntities[1] |= chunkEntityLodEnabled.Enabled[1];

                    for (int i = 0; i < numEntities; ++i)
                        chunkVisibility->SplitMasks[i] |= splitMask;
                }
                else if (chunkIn == FrustumPlanes.IntersectResult.Out)
                {
                    // No need to do anything. Split mask bits for this split should already
                    // be cleared since they were initialized to zero.
                }
            }

            // If anything survived the culling, perform sphere testing for each split
            if (useSphereTest && chunkVisibility->AnyVisible)
            {
                for (int j = 0; j < 2; j++)
                {
                    ulong visibleWord = chunkVisibility->VisibleEntities[j];

                    while (visibleWord != 0)
                    {
                        int bitIndex = math.tzcnt(visibleWord);
                        int entityIndex = (j << 6) + bitIndex;
                        ulong mask = 1ul << bitIndex;

                        var bounds = worldRenderBounds[entityIndex].Value;

                        int planeSplitMask = chunkVisibility->SplitMasks[entityIndex];
                        int sphereSplitMask = Splits.SplitSOASphereTest.SOASphereTestSplitMask(ref Splits, bounds);

#if DEBUG_VALIDATE_SOA_SPHERE_TEST
                        int referenceSphereSplitMask = 0;
                        for (int splitIndex = 0; splitIndex < splits.Length; ++splitIndex)
                        {
                            var s = splits[splitIndex];
                            byte splitMask = (byte)(1 << splitIndex);
                            float2 receiverSphereLightSpace = Splits.TransformToLightSpaceXY(s.CullingSphereCenter);
                            if (SphereTest(s, bounds, receiverSphereLightSpace) == SphereTestResult.MightCastShadow)
                                referenceSphereSplitMask |= splitMask;
                        }
                        // Use Debug.Log instead of Debug.Assert so that Burst does not remove it
                        if (sphereSplitMask != referenceSphereSplitMask)
                            Debug.Log($"SoA sphere test ({sphereSplitMask:x2}) disagrees with reference sphere tests ({referenceSphereSplitMask:x2})");
#endif

                        byte newSplitMask = (byte)(planeSplitMask & sphereSplitMask);
                        chunkVisibility->SplitMasks[entityIndex] = newSplitMask;

                        if (newSplitMask == 0)
                            chunkVisibility->VisibleEntities[j] ^= mask;

                        visibleWord ^= mask;
                    }
                }
            }
        }

        private enum SphereTestResult
        {
            // The caster is guaranteed to not cast a visible shadow in the tested cascade
            CannotCastShadow,
            // The caster might cast a shadow in the tested cascade, and has to be rendered in the shadow map
            MightCastShadow,
        }

        private SphereTestResult SphereTest(CullingSplitData split, AABB aabb, float2 receiverSphereLightSpace)
        {
            // This test has been ported from the corresponding test done by Unity's
            // built in shadow culling.

            float casterRadius = math.length(aabb.Extents);
            float2 casterCenterLightSpaceXY = Splits.TransformToLightSpaceXY(aabb.Center);

            // A spherical caster casts a cylindrical shadow volume. In XY in light space this ends up being a circle/circle intersection test.
            // Thus we first check if the caster bounding circle is at least partially inside the cascade circle.
            float sqrDistBetweenCasterAndCascadeCenter = math.lengthsq(casterCenterLightSpaceXY - receiverSphereLightSpace);
            float combinedRadius = casterRadius + split.CullingSphereRadius;
            float sqrCombinedRadius = combinedRadius * combinedRadius;

            // If the 2D circles intersect, then the caster is potentially visible in the cascade.
            // If they don't intersect, then there is no way for the caster to cast a shadow that is
            // visible inside the circle.
            // Casters that intersect the circle but are behind the receiver sphere also don't cast shadows.
            // We don't consider that here, since those casters should be culled out by the receiver
            // plane culling.
            if (sqrDistBetweenCasterAndCascadeCenter <= sqrCombinedRadius)
                return SphereTestResult.MightCastShadow;
            else
                return SphereTestResult.CannotCastShadow;
        }
    }

    internal struct SOASphereTest
    {
        [NoAlias] public UnsafeList<float4> ReceiverCenterX;
        [NoAlias] public UnsafeList<float4> ReceiverCenterY;
        [NoAlias] public UnsafeList<float4> ReceiverRadius;

        public SOASphereTest(ref CullingSplits splits, AllocatorManager.AllocatorHandle allocator)
        {
            int numSplits = splits.Splits.Length;
            int numPackets = (numSplits + 3) / 4;

            Debug.Assert(numSplits > 0, "No valid culling splits for sphere testing");

            ReceiverCenterX = new UnsafeList<float4>(numPackets, allocator);
            ReceiverCenterY = new UnsafeList<float4>(numPackets, allocator);
            ReceiverRadius = new UnsafeList<float4>(numPackets, allocator);
            ReceiverCenterX.Resize(numPackets);
            ReceiverCenterY.Resize(numPackets);
            ReceiverRadius.Resize(numPackets);

            // Initialize the last packet with values that will always fail the sphere test
            int lastPacket = numPackets - 1;
            ReceiverCenterX[lastPacket] = new float4(float.PositiveInfinity);
            ReceiverCenterY[lastPacket] = new float4(float.PositiveInfinity);
            ReceiverRadius[lastPacket] = float4.zero;

            for (int i = 0; i < numSplits; ++i)
            {
                int packetIndex = i >> 2;
                int elementIndex = i & 3;

                float2 receiverCenter = splits.TransformToLightSpaceXY(splits.Splits[i].CullingSphereCenter);
                ReceiverCenterX.ElementAt(packetIndex)[elementIndex] = receiverCenter.x;
                ReceiverCenterY.ElementAt(packetIndex)[elementIndex] = receiverCenter.y;
                ReceiverRadius.ElementAt(packetIndex)[elementIndex] = splits.Splits[i].CullingSphereRadius;
            }
        }

        public int SOASphereTestSplitMask(ref CullingSplits splits, AABB aabb)
        {
            int numPackets = ReceiverRadius.Length;

            float4 casterRadius = new float4(math.length(aabb.Extents));
            float2 casterCenter = splits.TransformToLightSpaceXY(aabb.Center);
            float4 casterCenterX = casterCenter.xxxx;
            float4 casterCenterY = casterCenter.yyyy;

            int splitMask = 0;
            int splitMaskShift = 0;
            for (int i = 0; i < numPackets; ++i)
            {
                float4 dx = casterCenterX - ReceiverCenterX[i];
                float4 dy = casterCenterY - ReceiverCenterY[i];
                float4 sqrDistBetweenCasterAndCascadeCenter = dx * dx + dy * dy;
                float4 combinedRadius = casterRadius + ReceiverRadius[i];
                float4 sqrCombinedRadius = combinedRadius * combinedRadius;
                bool4 mightCastShadow = sqrDistBetweenCasterAndCascadeCenter <= sqrCombinedRadius;
                int splitMask4 = math.bitmask(mightCastShadow);
                // Packet 0 is for bits 0-3, packet 1 is for bits 4-7 etc.
                splitMask |= splitMask4 << splitMaskShift;
                splitMaskShift += 4;
            }

            return splitMask;
        }
    }
}
