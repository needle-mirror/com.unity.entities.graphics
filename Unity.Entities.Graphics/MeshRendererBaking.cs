
//#define ENABLE_MESH_RENDERER_SUBMESH_DATA_SHARING

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Core;
using Unity.Entities;
using Unity.Profiling;
using Unity.Transforms;
using UnityEngine;

namespace Unity.Rendering
{
    [TemporaryBakingType]
    struct MeshRendererBakingData : IComponentData
    {
        public UnityObjectRef<Renderer> MeshRenderer;
    }

    [BakingType]
    struct AdditionalMeshRendererEntity : IComponentData { }

    class MeshRendererBaker : Baker<MeshRenderer>
    {
        public override void Bake(MeshRenderer authoring)
        {
            // TextMeshes don't need MeshFilters, early out
            var textMesh = GetComponent<TextMesh>();
            if (textMesh != null)
                return;

            // Takes a dependency on the mesh
            var meshFilter = GetComponent<MeshFilter>();
            var mesh = (meshFilter != null) ? GetComponent<MeshFilter>().sharedMesh : null;

            NativeArray<Entity> additionalEntities = default;

#if ENABLE_MESH_RENDERER_SUBMESH_DATA_SHARING
            MeshRendererBakingUtility.ConvertOnPrimaryEntity(this, authoring, mesh);
#else
            MeshRendererBakingUtility.ConvertOnPrimaryEntityForSingleMaterial(this, authoring, mesh, out additionalEntities);
#endif

            DependsOnLightBaking();

            if (additionalEntities.Length == 0)
            {
                var mainEntity = GetEntity(TransformUsageFlags.Renderable);
                AddComponent(mainEntity, new MeshRendererBakingData { MeshRenderer = authoring });
            }
            else
            {
                foreach (var entity in additionalEntities)
                    AddComponent(entity, new MeshRendererBakingData { MeshRenderer = authoring });
            }

            if (additionalEntities.IsCreated)
                additionalEntities.Dispose();
        }
    }

    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateBefore(typeof(MeshRendererBaking))]
    partial struct AdditionalMeshRendererFilterBakingSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.RemoveComponent<PostTransformMatrix>(
                SystemAPI.QueryBuilder()
                    .WithAll<AdditionalMeshRendererEntity, PostTransformMatrix>()
                    .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)
                    .Build()
            );
        }
    }

    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateBefore(typeof(MeshRendererBaking))]
    #pragma warning disable EA0007 // This disables sourcegen for the system.
                                   // As the system uses obsolete code, we need to use a pragma to disable it
                                   // but Entities SourceGen doesn't copy pragmas. So to disable the obsolete warning
                                   // we need to not use sourcegen, this ensures that.
    #pragma warning disable 618
    struct RenderMeshToRenderMeshUnmanagedBakingSystem : ISystem
    {
        EntityQuery m_RenderMeshQuery;
        public void OnCreate(ref SystemState state)
        {
            m_RenderMeshQuery = new EntityQueryBuilder(state.WorldUpdateAllocator)
                .WithAll<RenderMesh>()
                .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);
        }

        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.AddComponent<RenderMeshUnmanaged>(m_RenderMeshQuery);
            using var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
            using var renderMeshEntities = m_RenderMeshQuery.ToEntityArray(state.WorldUpdateAllocator);
            foreach (var entity in renderMeshEntities)
            {
                var renderMesh = state.EntityManager.GetSharedComponentManaged<RenderMesh>(entity);
                if (renderMesh.materials == null)
                {
                    state.EntityManager.SetComponentData(entity, new RenderMeshUnmanaged(renderMesh.mesh, null, new SubMeshIndexInfo32((ushort) renderMesh.subMesh)));
                    continue;
                }

                var materialCount = renderMesh.materials.Count;
                if (materialCount == 1)
                {
                    state.EntityManager.SetComponentData(entity, new RenderMeshUnmanaged(renderMesh.mesh, renderMesh.material, new SubMeshIndexInfo32((ushort) renderMesh.subMesh)));
                }
                else
                {
                    state.EntityManager.SetComponentData(entity, new RenderMeshUnmanaged(renderMesh.mesh, renderMesh.materials[0], new SubMeshIndexInfo32(0, (byte)materialCount)));

                    var additionalMaterials = ecb.AddBuffer<MeshRendererBakingUtility.MaterialReferenceElement>(entity);
                    for (int i = 1; i < materialCount; i++)
                        additionalMaterials.Add(new MeshRendererBakingUtility.MaterialReferenceElement { Material = renderMesh.materials[i] });
                }
            }
        }
    }
    #pragma warning restore 618
    #pragma warning restore EA0007

    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    partial struct MeshRendererBaking : ISystem
    {
        // Hold a persistent light map conversion context so previously encountered light maps
        // can be reused across multiple conversion batches, which is especially important
        // for incremental conversion (LiveConversion).
        static LightMapBakingContext m_LightMapBakingContext = new ();

        /// <inheritdoc/>
        public void OnUpdate(ref SystemState state)
        {
            // TODO: When to call m_LightMapConversionContext.Reset() ? When lightmaps are baked?
            var context = new RenderMeshBakingContext(m_LightMapBakingContext);

            if (m_LightMapBakingContext != null)
            {
                foreach (var authoring in SystemAPI.Query<RefRO<MeshRendererBakingData>>()
                             .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities))
                {
                    context.CollectLightMapUsage(authoring.ValueRO.MeshRenderer);
                }
            }

            context.ProcessLightMapsForConversion();

            using var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
            foreach (var (renderMesh, meshRenderRef, lightmapStRef, lightmapIndexRef, entity)
                     in SystemAPI.Query<
                             RefRW<RenderMeshUnmanaged>, RefRO<MeshRendererBakingData>,
                             RefRW<BuiltinMaterialPropertyUnity_LightmapST>, RefRW<BuiltinMaterialPropertyUnity_LightmapIndex>
                         >().WithEntityAccess()
                         .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)
                         .WithNone<MeshRendererBakingUtility.MaterialReferenceElement>())
            {
                ref var mainMaterialRef = ref renderMesh.ValueRW.materialForSubMesh;
                SetupLightmapping(context, ecb, entity, meshRenderRef, lightmapStRef, lightmapIndexRef, ref mainMaterialRef);
            }

            foreach (var (renderMesh, meshRenderRef, extraMaterials,lightmapStRef, lightmapIndexRef, entity)
                     in SystemAPI.Query<
                             RefRW<RenderMeshUnmanaged>, RefRO<MeshRendererBakingData>, DynamicBuffer<MeshRendererBakingUtility.MaterialReferenceElement>,
                             RefRW<BuiltinMaterialPropertyUnity_LightmapST>, RefRW<BuiltinMaterialPropertyUnity_LightmapIndex>
                         >().WithEntityAccess()
                         .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities))
            {
                ref var mainMaterialRef = ref renderMesh.ValueRW.materialForSubMesh;
                SetupLightmapping(context, ecb, entity, meshRenderRef, lightmapStRef, lightmapIndexRef, ref mainMaterialRef);

                for (int i = 0; i < extraMaterials.Length; i++)
                {
                    ref var extraMaterialRef = ref extraMaterials.ElementAt(i).Material;
                    SetupLightmapping(context, ecb, entity, meshRenderRef, lightmapStRef, lightmapIndexRef, ref extraMaterialRef);
                }
            }

            context.EndConversion();
            ecb.Playback(state.EntityManager);
        }

        static void SetupLightmapping(RenderMeshBakingContext context,
            EntityCommandBuffer ecb,
            Entity entity,
            RefRO<MeshRendererBakingData> meshRenderRef,
            RefRW<BuiltinMaterialPropertyUnity_LightmapST> lightmapStRef,
            RefRW<BuiltinMaterialPropertyUnity_LightmapIndex> lightmapIndexRef,
            ref UnityObjectRef<Material> materialRef)
        {
            // Check if valid material
            if (materialRef.Id.instanceId == default)
                return;

            // Get lightmap if any
            Renderer renderer = meshRenderRef.ValueRO.MeshRenderer;
            lightmapStRef.ValueRW.Value = renderer.lightmapScaleOffset;
            var (lightmappedMaterial, lightMaps) = context.GetHybridLightMapping(ref lightmapIndexRef.ValueRW, renderer.lightmapIndex, materialRef);

            // if so, add it to the entity
            if (lightmappedMaterial.Id.instanceId != 0)
            {
                ecb.SetSharedComponent(entity, lightMaps);
                materialRef = lightmappedMaterial;
            }
        }
    }



    // RenderMeshPostprocessSystem combines RenderMesh components from all found entities
    // into a single RenderMeshArray component that is used for rendering.
    // The RenderMesh component is no longer used at runtime, and it is removed to improve
    // chunk utilization and batching.
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateAfter(typeof(MeshRendererBaking))]
    [BurstCompile]
    partial struct RenderMeshPostProcessSystem : ISystem
    {
        static readonly ProfilerMarker k_RendererBakingMarker = new ("MeshRenderer Baking");

        struct UnityObjectRefComparer<T> : System.Collections.Generic.IComparer<UnityObjectRef<T>> where T : UnityEngine.Object
        {
            public int Compare(UnityObjectRef<T> a, UnityObjectRef<T> b)
            {
                return AssetComparisonUtility.CompareUnityObjectRef(a, b);
            }
        }

        struct SubMeshKeyComparer : System.Collections.Generic.IComparer<SubMeshKey>
        {
            public int Compare(SubMeshKey a, SubMeshKey b)
            {
                var meshCompare = AssetComparisonUtility.CompareUnityObjectRef(a.Mesh, b.Mesh);
                if (meshCompare != 0)
                    return meshCompare;

                var submeshCompare = a.SubmeshInfo.SubMesh.CompareTo(b.SubmeshInfo.SubMesh);
                if (submeshCompare != 0)
                    return submeshCompare;

                var materialCompare = AssetComparisonUtility.CompareUnityObjectRef(a.MaterialForSubmesh, b.MaterialForSubmesh);
                if (materialCompare != 0)
                    return materialCompare;

                var lengthCompare = a.ExtraMaterials.Length.CompareTo(b.ExtraMaterials.Length);
                if (lengthCompare != 0)
                    return lengthCompare;

                for (var i = 0; i < a.ExtraMaterials.Length; i++)
                {
                    var extraMatCompare = AssetComparisonUtility.CompareUnityObjectRef(a.ExtraMaterials[i].Material, b.ExtraMaterials[i].Material);
                    if (extraMatCompare != 0)
                        return extraMatCompare;
                }

                return 0;
            }
        }

        struct SubMeshKey : IEquatable<SubMeshKey>
        {
            public UnityObjectRef<Mesh> Mesh;
            public SubMeshIndexInfo32 SubmeshInfo;
            public UnityObjectRef<Material> MaterialForSubmesh;
            public DynamicBuffer<MeshRendererBakingUtility.MaterialReferenceElement> ExtraMaterials;

            public bool Equals(SubMeshKey other) => Mesh.Equals(other.Mesh)
                && SubmeshInfo.Equals(other.SubmeshInfo)
                && MaterialForSubmesh.Equals(other.MaterialForSubmesh)
                && SequenceEquals(ExtraMaterials, other.ExtraMaterials);

            public override int GetHashCode() => Mesh.GetHashCode()
                ^ SubmeshInfo.GetHashCode()
                ^ MaterialForSubmesh.GetHashCode()
                ^ GetSequenceHashCode(ExtraMaterials);

            static bool SequenceEquals(DynamicBuffer<MeshRendererBakingUtility.MaterialReferenceElement> a, DynamicBuffer<MeshRendererBakingUtility.MaterialReferenceElement> b)
            {
                if (a.Length != b.Length)
                    return false;

                for (int i = 0; i < a.Length; i++)
                {
                    if (!a[i].Material.Equals(b[i].Material))
                        return false;
                }

                return true;
            }

            static unsafe int GetSequenceHashCode(DynamicBuffer<MeshRendererBakingUtility.MaterialReferenceElement> buffer)
            {
                return (int)XXHash.Hash32((byte*)buffer.GetUnsafeReadOnlyPtr(), buffer.Length * UnsafeUtility.SizeOf<MeshRendererBakingUtility.MaterialReferenceElement>());
            }
        }

        EntityQuery m_BakedEntities;
        EntityQuery m_RenderMeshEntities;
        EntityQuery m_ExistingRenderMeshArrayQuery;
        ComponentTypeHandle<RenderMeshUnmanaged> m_RenderMeshUnmanagedHandle;
        ComponentTypeHandle<MaterialMeshInfo> m_MaterialMeshInfoHandle;
        BufferTypeHandle<MeshRendererBakingUtility.MaterialReferenceElement> m_MaterialReferenceHandle;

        NativeHashSet<UnityObjectRef<Mesh>> m_MeshSet;
        NativeHashSet<UnityObjectRef<Material>> m_MaterialSet;
        NativeList<UnityObjectRef<Mesh>> m_UniqueMeshes;
        NativeList<UnityObjectRef<Material>> m_UniqueMaterials;
        NativeList<SubMeshKey> m_UniqueSubMeshKeys;
        NativeList<MaterialMeshIndex> m_MaterialMeshIndices;
        NativeHashMap<SubMeshKey, MaterialMeshInfo> m_SubMeshKeyToMaterialInfo;

        /// <inheritdoc/>
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_BakedEntities = new EntityQueryBuilder(state.WorldUpdateAllocator)
                .WithAny<MeshRendererBakingData, SkinnedMeshRendererBakingData>()
                .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);

            m_RenderMeshEntities = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<RenderMeshUnmanaged, MaterialMeshInfo, SceneSection>()
                .WithOptions(EntityQueryOptions.IncludePrefab |
                             EntityQueryOptions.IncludeDisabledEntities |
                             EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(ref state);

            m_ExistingRenderMeshArrayQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<RenderMeshArray, SceneSection>()
                .Build(ref state);

            m_RenderMeshUnmanagedHandle = state.GetComponentTypeHandle<RenderMeshUnmanaged>();
            m_MaterialMeshInfoHandle = state.GetComponentTypeHandle<MaterialMeshInfo>();
            m_MaterialReferenceHandle = state.GetBufferTypeHandle<MeshRendererBakingUtility.MaterialReferenceElement>(isReadOnly:true);

            m_MeshSet = new NativeHashSet<UnityObjectRef<Mesh>>(Chunk.kMaximumEntitiesPerChunk, Allocator.Persistent);
            m_MaterialSet = new NativeHashSet<UnityObjectRef<Material>>(Chunk.kMaximumEntitiesPerChunk, Allocator.Persistent);
            m_UniqueMeshes = new NativeList<UnityObjectRef<Mesh>>(Chunk.kMaximumEntitiesPerChunk, Allocator.Persistent);
            m_UniqueMaterials = new NativeList<UnityObjectRef<Material>>(Chunk.kMaximumEntitiesPerChunk, Allocator.Persistent);
            m_UniqueSubMeshKeys = new NativeList<SubMeshKey>(16, Allocator.Persistent);
            m_MaterialMeshIndices = new NativeList<MaterialMeshIndex>(Chunk.kMaximumEntitiesPerChunk, Allocator.Persistent);
            m_SubMeshKeyToMaterialInfo = new NativeHashMap<SubMeshKey, MaterialMeshInfo>(Chunk.kMaximumEntitiesPerChunk, Allocator.Persistent);

            state.RequireForUpdate(m_BakedEntities);
        }

        /// <inheritdoc/>
        public void OnDestroy(ref SystemState state)
        {
            m_MeshSet.Dispose();
            m_MaterialSet.Dispose();
            m_UniqueMeshes.Dispose();
            m_UniqueMaterials.Dispose();
            m_UniqueSubMeshKeys.Dispose();
            m_MaterialMeshIndices.Dispose();
            m_SubMeshKeyToMaterialInfo.Dispose();
        }

        /// <inheritdoc/>
        public void OnUpdate(ref SystemState state)
        {
            using var _ = k_RendererBakingMarker.Auto();

            state.EntityManager.GetAllUniqueSharedComponents(out NativeList<SceneSection> sceneSections, state.WorldUpdateAllocator);
            foreach (var sceneSection in sceneSections)
            {
                // Update handles per scene section since processing each section can cause structural changes
                m_RenderMeshUnmanagedHandle.Update(ref state);
                m_MaterialMeshInfoHandle.Update(ref state);
                m_MaterialReferenceHandle.Update(ref state);

                m_UniqueMeshes.Clear();
                m_UniqueMaterials.Clear();
                m_MeshSet.Clear();
                m_MaterialSet.Clear();
                m_MaterialMeshIndices.Clear();
                m_SubMeshKeyToMaterialInfo.Clear();
                m_UniqueSubMeshKeys.Clear();

                m_RenderMeshEntities.SetSharedComponentFilter(sceneSection);
                using var chunks = m_RenderMeshEntities.ToArchetypeChunkArray(state.WorldUpdateAllocator);
                if (chunks.Length == 0)
                    continue;

                var componentIndex = GetRenderMeshArrayComponentIndex(ref state, sceneSection);
                ProcessRenderMeshArray(ref state, componentIndex, chunks);
            }
        }

        [BurstCompile]
        int GetRenderMeshArrayComponentIndex(ref SystemState state, SceneSection sceneSection)
        {
            m_ExistingRenderMeshArrayQuery.SetSharedComponentFilter(sceneSection);
            if (m_ExistingRenderMeshArrayQuery.IsEmptyIgnoreFilter)
                return -1;

            using var existingChunks = m_ExistingRenderMeshArrayQuery.ToArchetypeChunkArray(state.WorldUpdateAllocator);
            if (existingChunks.Length == 0)
                return -1;

            var renderMeshArrayTypeHandle = state.EntityManager.GetSharedComponentTypeHandle<RenderMeshArray>();
            return existingChunks[0].GetSharedComponentIndex(renderMeshArrayTypeHandle);
        }

        void ProcessRenderMeshArray(ref SystemState state, int arrayComponentIndex, NativeArray<ArchetypeChunk> chunks)
        {
            var mergedCount = 0;

            // Merge with existing array to preserve indices during incremental baking
            if (arrayComponentIndex >= 0)
            {
                MergeWithExistingRenderMeshArray(ref state, arrayComponentIndex);
                mergedCount = m_UniqueMeshes.Length + m_UniqueMaterials.Length;
            }

            CollectUniqueMeshesAndMaterials(chunks);

            if (m_UniqueMeshes.Length == 0 && m_UniqueMaterials.Length == 0)
                return;

            // Sort for deterministic ordering when no existing indices to preserve
            if (mergedCount == 0)
            {
                SortMeshesAndMaterials();
            }

            BuildAndApplyRenderMeshArray(ref state, chunks);
        }

        [BurstCompile]
        void BuildAndApplyRenderMeshArray(ref SystemState state, NativeArray<ArchetypeChunk> chunks)
        {
            using var meshToIndexMap = new NativeHashMap<UnityObjectRef<Mesh>, int>(m_UniqueMeshes.Length, Allocator.Temp);
            using var materialToIndexMap = new NativeHashMap<UnityObjectRef<Material>, int>(m_UniqueMaterials.Length, Allocator.Temp);

            BuildIndexMapsAndMaterialMeshInfo(meshToIndexMap, materialToIndexMap);
            WriteMaterialMeshInfoToEntities(chunks, meshToIndexMap, materialToIndexMap);

            CallFromBurstRenderMeshArrayHelper.AddRenderMeshArrayTo(state.EntityManager, chunks,
                m_UniqueMaterials.AsArray(),
                m_UniqueMeshes.AsArray(),
                m_MaterialMeshIndices.AsArray());
        }

        [BurstCompile]
        unsafe void CollectUniqueMeshesAndMaterials(NativeArray<ArchetypeChunk> chunks)
        {
            using var subMeshKeySet = new NativeHashSet<SubMeshKey>(16, Allocator.Temp);

            foreach (var chunk in chunks)
            {
                var entityCount = chunk.Count;

                var meshPtr = chunk.GetComponentDataPtrRO(ref m_RenderMeshUnmanagedHandle);
                var renderMeshArr = new Span<RenderMeshUnmanaged>(meshPtr, entityCount);

                var extraMaterials = chunk.GetBufferAccessorRO(ref m_MaterialReferenceHandle);

                for (var i = 0; i < entityCount; ++i)
                {
                    var renderMesh = renderMeshArr[i];
                    if (m_MeshSet.Add(renderMesh.mesh))
                        m_UniqueMeshes.Add(renderMesh.mesh);
                    if (m_MaterialSet.Add(renderMesh.materialForSubMesh))
                        m_UniqueMaterials.Add(renderMesh.materialForSubMesh);
                }

                if (extraMaterials.Length > 0)
                {
                    for (var i = 0; i < entityCount; ++i)
                    {
                        var renderMesh = renderMeshArr[i];
                        var extraMaterialBuffer = extraMaterials[i];

                        for (var m = 0; m < extraMaterialBuffer.Length; m++)
                        {
                            if (m_MaterialSet.Add(extraMaterialBuffer[m].Material))
                                m_UniqueMaterials.Add(extraMaterialBuffer[m].Material);
                        }

                        var subMeshKey = new SubMeshKey
                        {
                            Mesh = renderMesh.mesh,
                            SubmeshInfo = renderMesh.subMeshInfo,
                            MaterialForSubmesh = renderMesh.materialForSubMesh,
                            ExtraMaterials = extraMaterialBuffer
                        };

                        if (subMeshKeySet.Add(subMeshKey))
                            m_UniqueSubMeshKeys.Add(subMeshKey);
                    }
                }
            }
        }

        void MergeWithExistingRenderMeshArray(ref SystemState state, int existingArrayIndex)
        {
            var existingRenderMeshArray = state.EntityManager.GetSharedComponentManaged<RenderMeshArray>(existingArrayIndex);

            var hasMeshes = existingRenderMeshArray.MeshReferences != null && existingRenderMeshArray.MeshReferences.Length > 0;
            var hasMaterials = existingRenderMeshArray.MaterialReferences != null && existingRenderMeshArray.MaterialReferences.Length > 0;

            if (!hasMeshes && !hasMaterials)
                return;

            // Add existing items first so they keep their original list positions and indices
            if (hasMeshes)
            {
                foreach (var meshRef in existingRenderMeshArray.MeshReferences)
                {
                    if (m_MeshSet.Add(meshRef))
                        m_UniqueMeshes.Add(meshRef);
                }
            }

            if (hasMaterials)
            {
                foreach (var materialRef in existingRenderMeshArray.MaterialReferences)
                {
                    if (m_MaterialSet.Add(materialRef))
                        m_UniqueMaterials.Add(materialRef);
                }
            }
        }

        void SortMeshesAndMaterials()
        {
            m_UniqueMaterials.Sort(new UnityObjectRefComparer<Material>());
            m_UniqueMeshes.Sort(new UnityObjectRefComparer<Mesh>());
            if (m_UniqueSubMeshKeys.Length > 0)
                m_UniqueSubMeshKeys.Sort(new SubMeshKeyComparer());
        }

        void BuildIndexMapsAndMaterialMeshInfo(
            NativeHashMap<UnityObjectRef<Mesh>, int> meshToIndexMap,
            NativeHashMap<UnityObjectRef<Material>, int> materialToIndexMap)
        {
            // Build maps from list positions for fast lookup when writing MaterialMeshInfo
            for (var i = 0; i < m_UniqueMaterials.Length; i++)
                materialToIndexMap.Add(m_UniqueMaterials[i], i);
            for (var i = 0; i < m_UniqueMeshes.Length; i++)
                meshToIndexMap.Add(m_UniqueMeshes[i], i);

            foreach (var subMeshKey in m_UniqueSubMeshKeys)
            {
                var meshIndex = meshToIndexMap[subMeshKey.Mesh];
                var startIndex = m_MaterialMeshIndices.Length;
                var materialCount = subMeshKey.ExtraMaterials.Length + 1;

                var materialMeshInfo = MaterialMeshInfo.FromMaterialMeshIndexRange(startIndex, materialCount);
                m_SubMeshKeyToMaterialInfo.Add(subMeshKey, materialMeshInfo);

                m_MaterialMeshIndices.Add(new MaterialMeshIndex
                {
                    MeshIndex = meshIndex,
                    MaterialIndex = materialToIndexMap[subMeshKey.MaterialForSubmesh],
                    SubMeshIndex = 0
                });

                for (var m = 0; m < subMeshKey.ExtraMaterials.Length; m++)
                {
                    m_MaterialMeshIndices.Add(new MaterialMeshIndex
                    {
                        MeshIndex = meshIndex,
                        MaterialIndex = materialToIndexMap[subMeshKey.ExtraMaterials[m].Material],
                        SubMeshIndex = m + 1
                    });
                }
            }
        }

        unsafe void WriteMaterialMeshInfoToEntities(
            NativeArray<ArchetypeChunk> chunks,
            NativeHashMap<UnityObjectRef<Mesh>, int> meshToIndexMap,
            NativeHashMap<UnityObjectRef<Material>, int> materialToIndexMap)
        {
            foreach (var chunk in chunks)
            {
                var meshPtr = chunk.GetComponentDataPtrRO(ref m_RenderMeshUnmanagedHandle);
                var materialPtr = chunk.GetComponentDataPtrRW(ref m_MaterialMeshInfoHandle);
                var extraMaterials = chunk.GetBufferAccessorRO(ref m_MaterialReferenceHandle);

                var entityCount = chunk.Count;
                if (extraMaterials.Length == 0)
                {
                    for (var i = 0; i < entityCount; ++i)
                    {
                        var materialIndex = materialToIndexMap[meshPtr[i].materialForSubMesh];
                        var meshIndex = meshToIndexMap[meshPtr[i].mesh];

                        materialPtr[i] = MaterialMeshInfo.FromRenderMeshArrayIndices(materialIndex, meshIndex, meshPtr[i].subMeshInfo.SubMesh);
                    }
                }
                else
                {
                    for (var i = 0; i < entityCount; ++i)
                    {
                        var renderMesh = meshPtr[i];
                        var extraMaterialBuffer = extraMaterials[i];

                        var subMeshKey = new SubMeshKey
                        {
                            Mesh = renderMesh.mesh,
                            SubmeshInfo = renderMesh.subMeshInfo,
                            MaterialForSubmesh = renderMesh.materialForSubMesh,
                            ExtraMaterials = extraMaterialBuffer
                        };

                        materialPtr[i] = m_SubMeshKeyToMaterialInfo[subMeshKey];
                    }
                }
            }
        }

    }
}
