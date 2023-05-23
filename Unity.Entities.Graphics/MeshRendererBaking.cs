using System.Collections.Generic;
using System.Linq;
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

            // Takes a dependency on the materials
            var sharedMaterials = new List<Material>();
            authoring.GetSharedMaterials(sharedMaterials);

            MeshRendererBakingUtility.Convert(this, authoring, mesh, sharedMaterials, true, out var additionalEntities);

            DependsOnLightBaking();

            if (additionalEntities.Count == 0)
            {
                var mainEntity = GetEntity(TransformUsageFlags.Renderable);
                AddComponent(mainEntity, new MeshRendererBakingData {MeshRenderer = authoring});
            }

            foreach (var entity in additionalEntities)
            {
                AddComponent(entity, new MeshRendererBakingData{MeshRenderer = authoring});
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
                Renderer renderer = authoring.ValueRO.MeshRenderer;

                var sharedComponentRenderMesh = renderMesh;
                var lightmappedMaterial = context.ConfigureHybridLightMapping(
                    entity,
                    ecb,
                    renderer,
                    sharedComponentRenderMesh.material);

                if (lightmappedMaterial != null)
                {
                    sharedComponentRenderMesh.material = lightmappedMaterial;
                    ecb.SetSharedComponentManaged(entity, sharedComponentRenderMesh);
                }
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
                .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)
                .Build(this);

            RequireForUpdate(m_BakedEntities);
        }

        /// <inheritdoc/>
        protected override void OnUpdate()
        {
            int countUpperBound = EntityManager.GetSharedComponentCount();

            var renderMeshes = new List<RenderMesh>(countUpperBound);
            var meshes = new Dictionary<Mesh, bool>(countUpperBound);
            var materials = new Dictionary<Material, bool>(countUpperBound);

            EntityManager.GetAllUniqueSharedComponentsManaged(renderMeshes);
            renderMeshes.RemoveAt(0); // Remove null component automatically added by GetAllUniqueSharedComponentData

            foreach (var renderMesh in renderMeshes)
            {
                if (renderMesh.mesh != null)
                    meshes[renderMesh.mesh] = true;

                if (renderMesh.material != null)
                    materials[renderMesh.material] = true;
            }

            if (meshes.Count == 0 && materials.Count == 0)
                return;

            var renderMeshArray = new RenderMeshArray(materials.Keys.ToArray(), meshes.Keys.ToArray());
            var meshToIndex = renderMeshArray.GetMeshToIndexMapping();
            var materialToIndex = renderMeshArray.GetMaterialToIndexMapping();

            EntityManager.AddSharedComponentManaged(m_RenderMeshEntities, renderMeshArray);

            var entities = m_RenderMeshEntities.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; ++i)
            {
                var e = entities[i];

                var renderMesh = EntityManager.GetSharedComponentManaged<RenderMesh>(e);

                var mesh = renderMesh.mesh;
                var material = renderMesh.material;
                sbyte submesh = (sbyte) renderMesh.subMesh;

                MaterialMeshInfo materialMeshInfo = default;

                // If the Mesh or Material is null or can't be found, use a zero ID which means null.
                // This will result in proper error rendering.
                if (material is null || !materialToIndex.TryGetValue(material, out var materialIndex))
                    materialMeshInfo.MaterialID = BatchMaterialID.Null;
                else
                    materialMeshInfo.MaterialArrayIndex = materialIndex;

                if (mesh is null || !meshToIndex.TryGetValue(renderMesh.mesh, out var meshIndex))
                    materialMeshInfo.MeshID = BatchMeshID.Null;
                else
                    materialMeshInfo.MeshArrayIndex = meshIndex;

                materialMeshInfo.Submesh = submesh;

                // Explicitly check the raw integer vs the zero value, because using the
                // "MaterialID" property will assert if the value is an array index.
                if (materialMeshInfo.Material == 0 || materialMeshInfo.Mesh == 0)
                {
                    Renderer authoring = null;
                    if (EntityManager.HasComponent<MeshRendererBakingData>(e))
                        authoring = EntityManager.GetComponentData<MeshRendererBakingData>(e).MeshRenderer.Value;
                    else if (EntityManager.HasComponent<SkinnedMeshRendererBakingData>(e))
                        authoring = EntityManager.GetComponentData<SkinnedMeshRendererBakingData>(e).SkinnedMeshRenderer.Value;

                    string entityDebugString = authoring is null
                        ? e.ToString()
                        : authoring.ToString();

                    // Use authoring as a context object in the warning message, so clicking the warning selects
                    // the corresponding authoring GameObject.
                    Debug.LogWarning(
                        $"Entity \"{entityDebugString}\" has an invalid Mesh or Material, and will not render correctly at runtime. Mesh: {mesh}, Material: {material}, Submesh: {submesh}",
                        authoring);
                }

                EntityManager.SetComponentData(e, materialMeshInfo);
            }
        }
    }
}
