
//#define ENABLE_MESH_RENDERER_SUBMESH_DATA_SHARING

using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Core;
using Unity.Entities;
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
    partial struct RenderMeshPostProcessSystem : ISystem
    {
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
        ComponentTypeHandle<RenderMeshUnmanaged> m_RenderMeshUnmanagedHandle;
        ComponentTypeHandle<MaterialMeshInfo> m_MaterialMeshInfoHandle;
        BufferTypeHandle<MeshRendererBakingUtility.MaterialReferenceElement> m_MaterialReferenceHandle;

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

            m_RenderMeshUnmanagedHandle = state.GetComponentTypeHandle<RenderMeshUnmanaged>();
            m_MaterialMeshInfoHandle = state.GetComponentTypeHandle<MaterialMeshInfo>();
            m_MaterialReferenceHandle = state.GetBufferTypeHandle<MeshRendererBakingUtility.MaterialReferenceElement>(isReadOnly:true);

            state.RequireForUpdate(m_BakedEntities);
        }

        /// <inheritdoc/>
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            AddRenderMeshArrayToChunk(ref state, m_RenderMeshEntities);
        }

        unsafe void AddRenderMeshArrayToChunk(ref SystemState state, EntityQuery query)
        {
            using var meshToIndexMap = new NativeHashMap<UnityObjectRef<Mesh>, int>(Chunk.kMaximumEntitiesPerChunk, state.WorldUpdateAllocator);
            using var materialToIndexMap = new NativeHashMap<UnityObjectRef<Material>, int>(Chunk.kMaximumEntitiesPerChunk, state.WorldUpdateAllocator);
            using var uniqueMeshes = new NativeList<UnityObjectRef<Mesh>>(Chunk.kMaximumEntitiesPerChunk, state.WorldUpdateAllocator);
            using var uniqueMaterials = new NativeList<UnityObjectRef<Material>>(Chunk.kMaximumEntitiesPerChunk, state.WorldUpdateAllocator);

#if ENABLE_MESH_RENDERER_SUBMESH_DATA_SHARING
            using var materialMeshIndices = new NativeList<MaterialMeshIndex>(Chunk.kMaximumEntitiesPerChunk, state.WorldUpdateAllocator);
            using var subMeshKeyToMaterialInfo = new NativeHashMap<SubMeshKey, MaterialMeshInfo>(Chunk.kMaximumEntitiesPerChunk, state.WorldUpdateAllocator);
#else
            using var materialMeshIndices = new NativeList<MaterialMeshIndex>(0, state.WorldUpdateAllocator);
            using var subMeshKeyToMaterialInfo = new NativeHashMap<SubMeshKey, MaterialMeshInfo>(0, state.WorldUpdateAllocator);
#endif
            // We are creating a lookup table of meshes and materials for each scene section. So here we are
            // grabbing all different scene sections their associated chunks. We loop through the chunks and store all
            // the different materials and meshes in the unique maps. Finally, we loop through all the chunks again and store
            // the maps onto them in a managed shared component (RenderMeshArray).
            state.EntityManager.GetAllUniqueSharedComponents(out NativeList<SceneSection> sceneSections, state.WorldUpdateAllocator);
            foreach (var sceneSection in sceneSections)
            {
                uniqueMeshes.Clear();
                uniqueMaterials.Clear();
                meshToIndexMap.Clear();
                materialToIndexMap.Clear();
                materialMeshIndices.Clear();
                subMeshKeyToMaterialInfo.Clear();

                m_RenderMeshUnmanagedHandle.Update(ref state);
                m_MaterialMeshInfoHandle.Update(ref state);
                m_MaterialReferenceHandle.Update(ref state);

                query.SetSharedComponentFilter(sceneSection);

                using var chunks = query.ToArchetypeChunkArray(state.WorldUpdateAllocator);
                foreach (var chunk in chunks)
                {
                    var meshPtr = chunk.GetComponentDataPtrRW(ref m_RenderMeshUnmanagedHandle);
                    var materialPtr = chunk.GetComponentDataPtrRW(ref m_MaterialMeshInfoHandle);
                    var extraMaterials = chunk.GetBufferAccessorRO(ref m_MaterialReferenceHandle);

                    var entityCount = chunk.Count;
                    if (extraMaterials.Length == 0)
                    {
                        for (var i = 0; i < entityCount; ++i)
                        {
                            var materialIndex = GetUniqueIndex(uniqueMaterials, materialToIndexMap,
                                meshPtr[i].materialForSubMesh);
                            var meshIndex = GetUniqueIndex(uniqueMeshes, meshToIndexMap, meshPtr[i].mesh);

                            materialPtr[i] = MaterialMeshInfo.FromRenderMeshArrayIndices(materialIndex, meshIndex,
                                meshPtr[i].subMeshInfo.SubMesh);
                        }
                    }
                    else
                    {
                        for (var i = 0; i < entityCount; ++i)
                        {
                            var extraMaterialBuffer = extraMaterials[i];
                            var renderMesh = meshPtr[i];

                            var subMeshKey = new SubMeshKey
                            {
                                Mesh = renderMesh.mesh,
                                SubmeshInfo = renderMesh.subMeshInfo,
                                MaterialForSubmesh = renderMesh.materialForSubMesh,
                                ExtraMaterials = extraMaterialBuffer
                            };

                            if (!subMeshKeyToMaterialInfo.TryGetValue(subMeshKey, out var materialMeshInfo))
                            {
                                // Get mesh and material index
                                var meshIndex = GetUniqueIndex(uniqueMeshes, meshToIndexMap, renderMesh.mesh);
                                materialMeshInfo = MaterialMeshInfo.FromMaterialMeshIndexRange(materialMeshIndices.Length,
                                    extraMaterialBuffer.Length + 1);
                                subMeshKeyToMaterialInfo.Add(subMeshKey, materialMeshInfo);

                                materialMeshIndices.Add(new MaterialMeshIndex
                                {
                                    MeshIndex = meshIndex,
                                    MaterialIndex = GetUniqueIndex(uniqueMaterials, materialToIndexMap,
                                        renderMesh.materialForSubMesh),
                                    SubMeshIndex = 0
                                });

                                for (sbyte m = 0; m < extraMaterialBuffer.Length; m++)
                                {
                                    materialMeshIndices.Add(new MaterialMeshIndex
                                    {
                                        MeshIndex = meshIndex,
                                        MaterialIndex = GetUniqueIndex(uniqueMaterials, materialToIndexMap,
                                            extraMaterialBuffer[m].Material),
                                        SubMeshIndex = m + 1
                                    });
                                }
                            }

                            materialPtr[i] = materialMeshInfo;
                        }
                    }
                }

                if (uniqueMeshes.Length == 0 && uniqueMaterials.Length == 0)
                    continue;

                CallFromBurstRenderMeshArrayHelper.AddRenderMeshArrayTo(state.EntityManager, chunks,
                    uniqueMaterials.AsArray(),
                    uniqueMeshes.AsArray(),
                    materialMeshIndices.AsArray());
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int GetUniqueIndex<T>(NativeList<UnityObjectRef<T>> uniqueElems, NativeHashMap<UnityObjectRef<T>, int> elemToIndexMap, UnityObjectRef<T> elem) where T : UnityEngine.Object
        {
            var elemIndex = uniqueElems.Length;
            if (elemToIndexMap.TryAdd(elem, uniqueElems.Length))
                uniqueElems.Add(elem);
            else
                elemIndex = elemToIndexMap[elem];
            return elemIndex;
        }
    }
}
