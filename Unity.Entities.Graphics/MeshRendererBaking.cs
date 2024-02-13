
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
using UnityEngine.Rendering;

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
                return (int)XXHash.Hash32((byte*)buffer.GetUnsafePtr(), buffer.Length * UnsafeUtility.SizeOf<MeshRendererBakingUtility.MaterialReferenceElement>());
            }
        }

        EntityQuery m_BakedEntities;
        EntityQuery m_RenderMeshEntities;

        /// <inheritdoc/>
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_BakedEntities = new EntityQueryBuilder(state.WorldUpdateAllocator)
                .WithAny<MeshRendererBakingData, SkinnedMeshRendererBakingData>()
                .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);
            m_RenderMeshEntities = new EntityQueryBuilder(state.WorldUpdateAllocator)
                .WithAll<RenderMeshUnmanaged, MaterialMeshInfo>()
                .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(ref state);

            state.RequireForUpdate(m_BakedEntities);
        }

        /// <inheritdoc/>
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var renderMeshCount = m_RenderMeshEntities.CalculateEntityCount();
            using var meshToIndexMap = new NativeHashMap<UnityObjectRef<Mesh>, int>(renderMeshCount, state.WorldUpdateAllocator);
            using var materialToIndexMap = new NativeHashMap<UnityObjectRef<Material>, int>(renderMeshCount, state.WorldUpdateAllocator);
            using var uniqueMeshes = new NativeList<UnityObjectRef<Mesh>>(renderMeshCount, state.WorldUpdateAllocator);
            using var uniqueMaterials = new NativeList<UnityObjectRef<Material>>(renderMeshCount, state.WorldUpdateAllocator);

            foreach (var (renderMeshRef, materialMeshInfoRef) in SystemAPI.Query<RefRO<RenderMeshUnmanaged>, RefRW<MaterialMeshInfo>>().WithNone<MeshRendererBakingUtility.MaterialReferenceElement>()
                .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState))
            {
                var renderMesh = renderMeshRef.ValueRO;

                // Get mesh and material index
                var materialIndex = GetUniqueIndex(uniqueMaterials, materialToIndexMap, renderMesh.materialForSubMesh);
                var meshIndex = GetUniqueIndex(uniqueMeshes, meshToIndexMap, renderMesh.mesh);

                materialMeshInfoRef.ValueRW = MaterialMeshInfo.FromRenderMeshArrayIndices(materialIndex, meshIndex, renderMesh.subMeshInfo.SubMesh);
            }


#if ENABLE_MESH_RENDERER_SUBMESH_DATA_SHARING
            using var materialMeshIndices = new NativeList<MaterialMeshIndex>(renderMeshCount, state.WorldUpdateAllocator);
            using var subMeshKeyToMaterialInfo = new NativeHashMap<SubMeshKey, MaterialMeshInfo>(renderMeshCount, state.WorldUpdateAllocator);

            foreach (var (renderMeshRef, materialMeshInfoRef, extraMaterials) in SystemAPI.Query<RefRO<RenderMeshUnmanaged>, RefRW<MaterialMeshInfo>, DynamicBuffer<MeshRendererBakingUtility.MaterialReferenceElement>>()
                .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState))
            {
                var renderMesh = renderMeshRef.ValueRO;

                var subMeshKey = new SubMeshKey
                {
                    Mesh = renderMesh.mesh,
                    SubmeshInfo = renderMesh.subMeshInfo,
                    MaterialForSubmesh = renderMesh.materialForSubMesh,
                    ExtraMaterials = extraMaterials
                };

                if (!subMeshKeyToMaterialInfo.TryGetValue(subMeshKey, out var materialMeshInfo))
                {
                    // Get mesh and material index
                    var meshIndex = GetUniqueIndex(uniqueMeshes, meshToIndexMap, renderMesh.mesh);
                    materialMeshInfo = MaterialMeshInfo.FromMaterialMeshIndexRange(materialMeshIndices.Length, extraMaterials.Length + 1);
                    subMeshKeyToMaterialInfo.Add(subMeshKey, materialMeshInfo);

                    materialMeshIndices.Add(new MaterialMeshIndex
                    {
                        MeshIndex = meshIndex,
                        MaterialIndex = GetUniqueIndex(uniqueMaterials, materialToIndexMap, renderMesh.materialForSubMesh),
                        SubMeshIndex = 0
                    });

                    for (sbyte i = 0; i < extraMaterials.Length; i++)
                    {
                        materialMeshIndices.Add(new MaterialMeshIndex
                        {
                            MeshIndex = meshIndex,
                            MaterialIndex = GetUniqueIndex(uniqueMaterials, materialToIndexMap, extraMaterials[i].Material),
                            SubMeshIndex = i + 1
                        });
                    }
                }

                materialMeshInfoRef.ValueRW = materialMeshInfo;
            }
#else
            using var materialMeshIndices = new NativeList<MaterialMeshIndex>(0, state.WorldUpdateAllocator);
#endif

            if (uniqueMeshes.Length == 0 && uniqueMaterials.Length == 0)
                return;

            CallFromBurstRenderMeshArrayHelper.AddRenderMeshArrayTo(state.EntityManager, m_RenderMeshEntities,
                uniqueMaterials.AsArray(),
                uniqueMeshes.AsArray(),
                materialMeshIndices.AsArray());
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
