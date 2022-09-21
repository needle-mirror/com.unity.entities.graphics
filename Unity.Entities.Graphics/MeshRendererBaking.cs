using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
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

            // Takes a dependency on the materials
            var sharedMaterials = new List<Material>();
            authoring.GetSharedMaterials(sharedMaterials);

            MeshRendererBakingUtility.Convert(this, authoring, mesh, sharedMaterials, true, out var additionalEntities);

            if(additionalEntities.Count == 0)
                AddComponent(new MeshRendererBakingData{ MeshRenderer = authoring });

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
#if !ENABLE_TRANSFORM_V1
                ComponentType.ReadOnly<PostTransformMatrix>(),
#else
                ComponentType.ReadOnly<Translation>(),
                ComponentType.ReadOnly<Rotation>(),
                ComponentType.ReadOnly<NonUniformScale>(),
#endif
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
                Entities.ForEach((in MeshRendererBakingData authoring) =>
                {
                    context.CollectLightMapUsage(authoring.MeshRenderer);
                }).WithoutBurst().WithStructuralChanges().Run();
            }

            context.ProcessLightMapsForConversion();

            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            Entities.ForEach((Entity entity, RenderMesh renderMesh, in MeshRendererBakingData authoring) =>
            {
                Renderer renderer = authoring.MeshRenderer;

                var lightmappedMaterial = context.ConfigureHybridLightMapping(
                    entity,
                    ecb,
                    renderer,
                    renderMesh.material);

                if (lightmappedMaterial != null)
                {
                    renderMesh.material = lightmappedMaterial;
                    ecb.SetSharedComponentManaged(entity, renderMesh);
                }

            }).WithEntityQueryOptions(EntityQueryOptions.IncludeDisabledEntities).WithoutBurst().WithStructuralChanges().Run();

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
        Material m_ErrorMaterial;

        EntityQuery m_BakedEntities;
        EntityQuery m_RenderMeshEntities;

        /// <inheritdoc/>
        protected override void OnCreate()
        {
            if (m_ErrorMaterial == null)
            {
                m_ErrorMaterial = EntitiesGraphicsUtils.LoadErrorMaterial();
            }

            m_BakedEntities = EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                Any = new []
                {
                    ComponentType.ReadOnly<MeshRendererBakingData>(),
                    ComponentType.ReadOnly<SkinnedMeshRendererBakingData>(),
                },
                Options = EntityQueryOptions.IncludePrefab
            });

            m_RenderMeshEntities = EntityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new []
                    {
                        ComponentType.ReadWrite<RenderMesh>(),
                        ComponentType.ReadWrite<MaterialMeshInfo>(),
                    },
                    Options = EntityQueryOptions.IncludePrefab
                });

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

                var material = renderMesh.material ?? m_ErrorMaterial;
                materials[material] = true;
            }

            if (meshes.Count == 0 && materials.Count == 0)
                return;

            var renderMeshArray = new RenderMeshArray(materials.Keys.ToArray(), meshes.Keys.ToArray());
            var meshToIndex = renderMeshArray.GetMeshToIndexMapping();
            var materialToIndex = renderMeshArray.GetMaterialToIndexMapping();

            EntityManager.SetSharedComponentManaged(m_RenderMeshEntities, renderMeshArray);

            var entities = m_RenderMeshEntities.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; ++i)
            {
                var e = entities[i];

                var renderMesh = EntityManager.GetSharedComponentManaged<RenderMesh>(e);
                var material = renderMesh.material ?? m_ErrorMaterial;

                if (!materialToIndex.TryGetValue(material, out var materialIndex))
                {
                    Debug.LogWarning($"Material {material} not found from RenderMeshArray");
                    materialIndex = 0;
                }

                if (!meshToIndex.TryGetValue(renderMesh.mesh, out var meshIndex))
                {
                    Debug.LogWarning($"Mesh {renderMesh.mesh} not found from RenderMeshArray");
                    meshIndex = 0;
                }

                sbyte submesh = (sbyte) renderMesh.subMesh;

                EntityManager.SetComponentData(e,
                    MaterialMeshInfo.FromRenderMeshArrayIndices(
                        materialIndex,
                        meshIndex,
                        submesh));
            }
        }
    }
}
