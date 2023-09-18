
//#define ENABLE_MESH_RENDERER_SUBMESH_DATA_SHARING

using System.Collections.Generic;
using System.Linq;
using Unity.Assertions;
using Unity.Collections;
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

            var sharedMaterials = new List<Material>();
            authoring.GetSharedMaterials(sharedMaterials);

            List<Entity> additionalEntities = null;

#if ENABLE_MESH_RENDERER_SUBMESH_DATA_SHARING
            MeshRendererBakingUtility.ConvertOnPrimaryEntity(this, authoring, mesh, sharedMaterials);
#else
            MeshRendererBakingUtility.ConvertOnPrimaryEntityForSingleMaterial(this, authoring, mesh, sharedMaterials, null, out additionalEntities);
#endif

            DependsOnLightBaking();

            if (additionalEntities == null || additionalEntities.Count == 0)
            {
                var mainEntity = GetEntity(TransformUsageFlags.Renderable);
                AddComponent(mainEntity, new MeshRendererBakingData { MeshRenderer = authoring });
            }
            else
            {
                foreach (var entity in additionalEntities)
                {
                    AddComponent(entity, new MeshRendererBakingData { MeshRenderer = authoring });
                }
            }
        }
    }

    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateBefore(typeof(MeshRendererBaking))]
    partial class AdditionalMeshRendererFilterBakingSystem : SystemBase
    {
        private EntityQuery m_AdditionalEntities;
        private ComponentTypeSet typesToFilterSet;

        protected override void OnCreate()
        {
            ComponentType[] typesToFilter = new[]
            {
                ComponentType.ReadOnly<PostTransformMatrix>(),
            };
            typesToFilterSet = new ComponentTypeSet(typesToFilter);

            m_AdditionalEntities = GetEntityQuery(new EntityQueryDesc
            {
                All = new []
                {
                    ComponentType.ReadOnly<AdditionalMeshRendererEntity>()
                },
                Any = typesToFilter,
                Options = EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities,
            });
        }

        protected override void OnUpdate()
        {
            EntityManager.RemoveComponent(m_AdditionalEntities, typesToFilterSet);
        }
    }

    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    partial class MeshRendererBaking : SystemBase
    {
        // Hold a persistent light map conversion context so previously encountered light maps
        // can be reused across multiple conversion batches, which is especially important
        // for incremental conversion (LiveConversion).
        private LightMapBakingContext m_LightMapBakingContext;

        /// <inheritdoc/>
        protected override void OnCreate()
        {
            m_LightMapBakingContext = new LightMapBakingContext();
        }

        /// <inheritdoc/>
        protected override void OnUpdate()
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

            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            foreach (var (renderMesh, authoring, entity) in SystemAPI.Query<RenderMesh, RefRO<MeshRendererBakingData>>()
                         .WithEntityAccess()
                         .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities))
            {
                if (renderMesh.materials == null)
                    continue;

                Renderer renderer = authoring.ValueRO.MeshRenderer;

                List<Material> newMaterials = new List<Material>(renderMesh.materials);

                for (int i = 0; i < newMaterials.Count; ++i)
                {
                    var lightmappedMaterial = context.ConfigureHybridLightMapping(
                        entity,
                        ecb,
                        renderer,
                        newMaterials[i]);

                    if (lightmappedMaterial != null)
                        newMaterials[i] = lightmappedMaterial;
                }

                RenderMesh newRenderMesh = renderMesh;
                newRenderMesh.materials = newMaterials;

                ecb.SetSharedComponentManaged(entity, newRenderMesh);
            }

            context.EndConversion();

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }
    }


    // RenderMeshPostprocessSystem combines RenderMesh components from all found entities
    // into a single RenderMeshArray component that is used for rendering.
    // The RenderMesh component is no longer used at runtime, and it is removed to improve
    // chunk utilization and batching.
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateAfter(typeof(MeshRendererBaking))]
    partial class RenderMeshPostProcessSystem : SystemBase
    {

#if ENABLE_MESH_RENDERER_SUBMESH_DATA_SHARING
        const bool EnableSubMeshDataSharing = true;
#else
        const bool EnableSubMeshDataSharing = false;
#endif

        struct RenderMeshConversionInfo
        {
            // Set if UseIndexRange is true
            public int MaterialMeshIndexRangeStart;
            public int MaterialMeshIndexRangeLength;

            // Set if UseIndexRange is false
            public int MaterialIndex;
            public int MeshIndex;

            public int RenderMeshSubMeshIndex; // Needed for skinning even when UseIndexRange is true
            public string ErrorMessage;
            public bool UseIndexRange;
        }

        EntityQuery m_BakedEntities;
        EntityQuery m_RenderMeshEntities;

        /// <inheritdoc/>
        protected override void OnCreate()
        {
            m_BakedEntities = new EntityQueryBuilder(Allocator.Temp)
                .WithAny<MeshRendererBakingData, SkinnedMeshRendererBakingData>()
                .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)
                .Build(this);
            m_RenderMeshEntities = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<RenderMesh, MaterialMeshInfo>()
                .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(this);

            RequireForUpdate(m_BakedEntities);
        }

        /// <inheritdoc/>
        protected override void OnUpdate()
        {
            RenderMesh[] renderMeshes;
            int[] renderMeshIndices;
            GetAllRenderMeshes(EntityManager, out renderMeshes, out renderMeshIndices);

            Material[] uniqueMaterials;
            Mesh[] uniqueMeshes;
            ExtractUniqueMaterialAndMeshes(renderMeshes, out uniqueMaterials, out uniqueMeshes);

            if (uniqueMeshes.Length == 0 && uniqueMaterials.Length == 0)
                return;

            Dictionary<Material, int> materialToArrayIndex = BuildMaterialToIndexRemapTable(uniqueMaterials);
            Dictionary<Mesh, int> meshToArrayIndex = BuildMeshToIndexRemapTable(uniqueMeshes);

            var matMeshIndices = new List<MaterialMeshIndex>(renderMeshes.Length);
            var renderMeshConversionInfoMap = new Dictionary<int, RenderMeshConversionInfo>(renderMeshes.Length);

            for (int i = 0; i < renderMeshes.Length; i++)
            {
                RenderMesh renderMesh = renderMeshes[i];

                int meshIndex = -1;
                if (renderMesh.mesh != null && meshToArrayIndex.ContainsKey(renderMesh.mesh))
                    meshIndex = meshToArrayIndex[renderMesh.mesh];

                int submeshCount = renderMesh.materials.Count;
                Assert.IsTrue(renderMesh.subMesh < submeshCount);

                // Use MaterialMeshIndex array if there are multiple sub-meshes.
                // Otherwise use the simple index version to keep the MaterialMeshIndex array smaller.
                if (EnableSubMeshDataSharing && submeshCount > 1)
                {
                    int matMeshIndexRangeStart = matMeshIndices.Count;

                    string errorMessage = "";
                    for (int subMeshIndex = 0; subMeshIndex < submeshCount; subMeshIndex++)
                    {
                        Material material = renderMesh.materials[subMeshIndex];

                        if (material == null)
                            errorMessage += $"Material ({subMeshIndex}) is null. ";

                        int materialIndex = -1;
                        if (material != null && materialToArrayIndex.ContainsKey(material))
                            materialIndex = materialToArrayIndex[material];

                        MaterialMeshIndex matMeshIndex = default;
                        matMeshIndex.MaterialIndex = materialIndex;
                        matMeshIndex.MeshIndex = meshIndex;
                        matMeshIndex.SubMeshIndex = subMeshIndex;

                        matMeshIndices.Add(matMeshIndex);
                    }

                    renderMeshConversionInfoMap.Add(renderMeshIndices[i], new RenderMeshConversionInfo
                    {
                        UseIndexRange = true,
                        MaterialMeshIndexRangeStart = matMeshIndexRangeStart,
                        MaterialMeshIndexRangeLength = submeshCount,
                        RenderMeshSubMeshIndex = renderMesh.subMesh, // Used for skinning even when using a MaterialMeshIndex range
                        ErrorMessage = errorMessage,
                    });
                }
                else
                {
                    Material material = renderMesh.material;

                    string errorMessage = "";
                    if (material == null)
                        errorMessage += $"Material ({renderMesh.subMesh}) is null. ";

                    int materialIndex = -1;
                    if (material != null && materialToArrayIndex.ContainsKey(material))
                        materialIndex = materialToArrayIndex[material];

                    renderMeshConversionInfoMap.Add(renderMeshIndices[i], new RenderMeshConversionInfo
                    {
                        UseIndexRange = false,
                        MeshIndex = meshIndex,
                        MaterialIndex = materialIndex,
                        RenderMeshSubMeshIndex = renderMesh.subMesh,
                        ErrorMessage = errorMessage,
                    });
                }
            }

            var renderMeshArray = new RenderMeshArray(uniqueMaterials, uniqueMeshes, matMeshIndices.ToArray());
            EntityManager.AddSharedComponentManaged(m_RenderMeshEntities, renderMeshArray);
            var entities = m_RenderMeshEntities.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; ++i)
            {
                var entity = entities[i];
                int renderMeshComponentIndex = EntityManager.GetSharedComponentIndexManaged<RenderMesh>(entity);

                RenderMeshConversionInfo conversionInfo = renderMeshConversionInfoMap[renderMeshComponentIndex];

                if (!string.IsNullOrEmpty(conversionInfo.ErrorMessage))
                    LogRenderMeshConversionWarningOnEntity(EntityManager, entity, conversionInfo.ErrorMessage);

                bool isSkinnedEntity = EntityManager.HasComponent<SkinnedMeshRendererBakingData>(entity);

                MaterialMeshInfo materialMeshInfo = default;

                if (!conversionInfo.UseIndexRange || isSkinnedEntity)
                {
                    MaterialMeshIndex matMeshIndex = default;

                    if (conversionInfo.UseIndexRange)
                    {
                        Assert.IsTrue(conversionInfo.RenderMeshSubMeshIndex < conversionInfo.MaterialMeshIndexRangeLength);
                        matMeshIndex = matMeshIndices[conversionInfo.MaterialMeshIndexRangeStart + conversionInfo.RenderMeshSubMeshIndex];
                    }
                    else
                    {
                        matMeshIndex.MaterialIndex = conversionInfo.MaterialIndex;
                        matMeshIndex.MeshIndex = conversionInfo.MeshIndex;
                        matMeshIndex.SubMeshIndex = conversionInfo.RenderMeshSubMeshIndex;
                    }

                    if (matMeshIndex.MaterialIndex != -1)
                        materialMeshInfo.MaterialArrayIndex = matMeshIndex.MaterialIndex;

                    if (matMeshIndex.MeshIndex != -1)
                        materialMeshInfo.MeshArrayIndex = matMeshIndex.MeshIndex;

                    materialMeshInfo.SubMesh = (sbyte)matMeshIndex.SubMeshIndex;
                }
                else
                {
                    materialMeshInfo = MaterialMeshInfo.FromMaterialMeshIndexRange(
                        conversionInfo.MaterialMeshIndexRangeStart,
                        conversionInfo.MaterialMeshIndexRangeLength);
                }

                EntityManager.SetComponentData(entity, materialMeshInfo);
            }
        }

        static void GetAllRenderMeshes(EntityManager entityManager, out RenderMesh[] renderMeshes, out int[] renderMeshIndices)
        {
            int countUpperBound = entityManager.GetSharedComponentCount();

            var renderMeshesList = new List<RenderMesh>(countUpperBound);
            var renderMeshIndicesList = new List<int>(countUpperBound);
            entityManager.GetAllUniqueSharedComponentsManaged(renderMeshesList, renderMeshIndicesList);

            // Remove null component automatically added by GetAllUniqueSharedComponentData
            renderMeshesList.RemoveAt(0);
            renderMeshIndicesList.RemoveAt(0);

            renderMeshes = renderMeshesList.ToArray();
            renderMeshIndices = renderMeshIndicesList.ToArray();
        }

        static void ExtractUniqueMaterialAndMeshes(RenderMesh[] renderMeshes, out Material[] uniqueMaterials, out Mesh[] uniqueMeshes)
        {
            var meshes = new Dictionary<Mesh, bool>(renderMeshes.Length);
            var materials = new Dictionary<Material, bool>(renderMeshes.Length);

            for (int i = 0; i < renderMeshes.Length; i++)
            {
                RenderMesh renderMesh = renderMeshes[i];

                // Those case should have been already handled by MeshRendererBaker
                Assert.IsTrue(renderMesh.mesh != null);
                Assert.IsTrue(renderMesh.materials != null && renderMesh.materials.Count > 0);

                if (renderMesh.materials != null)
                {
                    for (int submeshIndex = 0; submeshIndex < renderMesh.materials.Count; submeshIndex++)
                    {
                        Material material = renderMesh.materials[submeshIndex];
                        if (material != null)
                            materials[material] = true;
                    }
                }

                if (renderMesh.mesh != null)
                    meshes[renderMesh.mesh] = true;
            }

            uniqueMeshes = meshes.Keys.ToArray();
            uniqueMaterials = materials.Keys.ToArray();
        }

        static Dictionary<Mesh, int> BuildMeshToIndexRemapTable(Mesh[] meshes)
        {
            var remapTable = new Dictionary<Mesh, int>(meshes.Length);

            for (int i = 0; i < meshes.Length; ++i)
            {
                Mesh mesh = meshes[i];
                Assert.IsTrue(mesh != null);

                remapTable.Add(mesh, i);
            }

            return remapTable;
        }

        static Dictionary<Material, int> BuildMaterialToIndexRemapTable(Material[] materials)
        {
            var remapTable = new Dictionary<Material, int>(materials.Length);

            for (int i = 0; i < materials.Length; ++i)
            {
                Material material = materials[i];
                Assert.IsTrue(material != null);

                remapTable.Add(material, i);
            }

            return remapTable;
        }

        static void LogRenderMeshConversionWarningOnEntity(EntityManager entityManager, Entity entity, string errorMessage)
        {
            Renderer authoring = null;
            if (entityManager.HasComponent<MeshRendererBakingData>(entity))
                authoring = entityManager.GetComponentData<MeshRendererBakingData>(entity).MeshRenderer.Value;
            else if (entityManager.HasComponent<SkinnedMeshRendererBakingData>(entity))
                authoring = entityManager.GetComponentData<SkinnedMeshRendererBakingData>(entity).SkinnedMeshRenderer.Value;

            string entityDebugString = authoring is null
                ? entity.ToString()
                : authoring.ToString();

            // Use authoring as a context object in the warning message, so clicking the warning selects
            // the corresponding authoring GameObject.
            Debug.LogWarning(
                $"Entity \"{entityDebugString}\" has invalid Meshes or Materials, and will not render correctly at runtime. {errorMessage}",
                authoring);
        }
    }
}
