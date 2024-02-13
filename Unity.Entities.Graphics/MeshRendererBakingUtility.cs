using System;
using System.Collections.Generic;
using Unity.Assertions;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Unity.Rendering
{
    class MeshRendererBakingUtility
    {
        enum ConversionMode
        {
            Null,
            AttachToPrimaryEntity,
            AttachToPrimaryEntityForSingleMaterial,
            AttachToMultipleEntities
        }

        [TemporaryBakingType]
        [InternalBufferCapacity(4)]
        internal struct MaterialReferenceElement : IBufferElementData
        {
            public UnityObjectRef<Material> Material;
        }

        struct LODStateBakeData<T> where T : Component
        {
            MeshLODComponent m_LODComponent;
            bool m_DoLOD;
            public LODStateBakeData(Baker<T> baker, Renderer authoringSource)
            {
                var lodGroup = baker.GetComponentInParent<LODGroup>();
                m_LODComponent = new MeshLODComponent
                {
                    Group = baker.GetEntity(lodGroup, TransformUsageFlags.Renderable),
                    LODMask = FindInLODs(lodGroup, authoringSource)
                };
                m_DoLOD = m_LODComponent.Group != Entity.Null && m_LODComponent.LODMask != -1;
            }

            public void AppendLODFlags(ref RenderMeshUtility.EntitiesGraphicsComponentFlags flags)
            {
                if (m_DoLOD)
                    flags |= RenderMeshUtility.EntitiesGraphicsComponentFlags.LODGroup;
            }

            public void SetLODComponent(Baker<T> baker, Entity entity)
            {
                if (m_DoLOD)
                    baker.SetComponent(entity, m_LODComponent);
            }

            static int FindInLODs(LODGroup lodGroup, Renderer authoring)
            {
                if (lodGroup != null)
                {
                    var lodGroupLODs = lodGroup.GetLODs();

                    int lodGroupMask = 0;

                    // Find the renderer inside the LODGroup
                    for (int i = 0; i < lodGroupLODs.Length; ++i)
                    {
                        foreach (var renderer in lodGroupLODs[i].renderers)
                        {
                            if (renderer == authoring)
                            {
                                lodGroupMask |= (1 << i);
                            }
                        }
                    }
                    return lodGroupMask > 0 ? lodGroupMask : -1;
                }
                return -1;
            }
        }


#pragma warning disable CS0162

        internal static void ConvertToMultipleEntities<T>(Baker<T> baker,
            Renderer authoring,
            Mesh mesh,
            Transform root,
            out NativeArray<Entity> additionalEntities) where T : Component
        {
            Convert(baker, authoring, mesh, ConversionMode.AttachToMultipleEntities, root, out additionalEntities);
        }

        internal static void ConvertOnPrimaryEntityForSingleMaterial<T>(Baker<T> baker,
            Renderer authoring,
            Mesh mesh,
            out NativeArray<Entity> additionalEntities) where T : Component
        {
            Convert(baker, authoring, mesh, ConversionMode.AttachToPrimaryEntityForSingleMaterial, null, out additionalEntities);
        }

        internal static void ConvertOnPrimaryEntity<T>(Baker<T> baker,
            Renderer authoring,
            Mesh mesh) where T : Component
        {
            Convert(baker, authoring, mesh, ConversionMode.AttachToPrimaryEntity, null, out _);
        }

        private static void Convert<T>(Baker<T> baker,
            Renderer authoring,
            Mesh mesh,
            ConversionMode conversionMode,
            Transform root,
            out NativeArray<Entity> additionalEntities) where T : Component
        {
            Assert.IsTrue(conversionMode != ConversionMode.Null);
            additionalEntities = default;

            var materials = authoring.sharedMaterials;
            if (!ValidateMeshAndMaterials(authoring, mesh, materials))
                return;

            // RenderMeshDescription accesses the GameObject layer.
            // Declaring the dependency on the GameObject with GetLayer, so the baker rebakes if the layer changes
            baker.GetLayer(authoring);
            var desc = new RenderMeshDescription(authoring);

            // Always disable per-object motion vectors for static objects
            if (baker.IsStatic() && desc.FilterSettings.MotionMode == MotionVectorGenerationMode.Object)
                desc.FilterSettings.MotionMode = MotionVectorGenerationMode.Camera;

            // Add material references if there are more than one material on a single entity
            if (conversionMode == ConversionMode.AttachToPrimaryEntity && authoring.sharedMaterials.Length > 1)
            {
                var extraMaterials = baker.AddBuffer<MaterialReferenceElement>(baker.GetEntity(TransformUsageFlags.None));
                var sharedMaterials = authoring.sharedMaterials;
                for (var index = 1; index < sharedMaterials.Length; index++)
                    extraMaterials.Add(new MaterialReferenceElement { Material = sharedMaterials[index] });
            }

            var attachToPrimaryEntity = false;
            attachToPrimaryEntity |= conversionMode == ConversionMode.AttachToPrimaryEntity;
            attachToPrimaryEntity |= conversionMode == ConversionMode.AttachToPrimaryEntityForSingleMaterial && authoring.sharedMaterials.Length == 1;

            if (attachToPrimaryEntity)
            {
                ConvertToSingleEntity(
                    baker,
                    desc,
                    authoring,
                    mesh,
                    materials);
            }
            else
            {
                ConvertToMultipleEntities(
                    baker,
                    desc,
                    authoring,
                    mesh,
                    materials,
                    root,
                    out additionalEntities);
            }
        }

#pragma warning restore CS0162

        static void ConvertToSingleEntity<T>(
            Baker<T> baker,
            RenderMeshDescription renderMeshDescription,
            Renderer renderer, Mesh mesh, Material[] materials) where T : Component
        {
            var entity = baker.GetEntity(renderer, TransformUsageFlags.Renderable);
            var lodComponent = new LODStateBakeData<T>(baker, renderer);

            // Add all components up front using as few calls as possible.
            var componentFlag = RenderMeshUtility.EntitiesGraphicsComponentFlags.Baking;
            componentFlag.AppendMotionAndProbeFlags(renderMeshDescription, baker.IsStatic());
            componentFlag.AppendDepthSortedFlag(materials);
            lodComponent.AppendLODFlags(ref componentFlag);
            baker.AddComponent(entity, RenderMeshUtility.ComputeComponentTypes(componentFlag));

            // Add lightmap components if the renderer is lightmapped
            if (RenderMeshUtility.IsLightMapped(renderer.lightmapIndex))
                baker.AddComponent(entity, RenderMeshUtility.LightmapComponents);

            var subMeshIndexInfo = materials.Length == 1 ? new SubMeshIndexInfo32(0) : new SubMeshIndexInfo32(0, (byte)materials.Length);
            baker.SetSharedComponent(entity, renderMeshDescription.FilterSettings);
            baker.SetComponent(entity, new RenderMeshUnmanaged(mesh, renderer.sharedMaterial, subMeshIndexInfo));
            baker.SetComponent(entity, new RenderBounds { Value = mesh.bounds.ToAABB() });
            lodComponent.SetLODComponent(baker, entity);
        }

        internal static void ConvertToMultipleEntities<T>(
            Baker<T> baker,
            RenderMeshDescription renderMeshDescription,
            Renderer renderer, Mesh mesh, Material[] materials,
            UnityEngine.Transform root,
            out NativeArray<Entity> additionalEntities) where T : Component
        {
            var lodState = new LODStateBakeData<T>(baker, renderer);
            var renderBounds = new RenderBounds { Value = mesh.bounds.ToAABB() };
            additionalEntities = new NativeArray<Entity>(materials.Length, Allocator.Temp);


            if (root == null)
            {
                baker.CreateAdditionalEntities(additionalEntities, TransformUsageFlags.Renderable); // $"{baker.GetName()}-MeshRendererEntity");
                baker.AddComponent<AdditionalMeshRendererEntity>(additionalEntities);
            }
            else
            {
                baker.CreateAdditionalEntities(additionalEntities, TransformUsageFlags.ManualOverride); // $"{baker.GetName()}-MeshRendererEntity");
                baker.AddComponent(additionalEntities, new LocalToWorld {Value = root.localToWorldMatrix});
                baker.AddComponent(additionalEntities, LocalTransform.Identity);

                if (!baker.IsStatic())
                {
                    var parent = new Parent
                    {
                        Value = baker.GetEntity(root, TransformUsageFlags.Renderable)
                    };
                    baker.AddComponent<Parent>(additionalEntities);
                    foreach (var entity in additionalEntities)
                        baker.SetComponent(entity, parent);
                }
            }

            // Add all components
            var componentFlag = RenderMeshUtility.EntitiesGraphicsComponentFlags.Baking;
            componentFlag.AppendMotionAndProbeFlags(renderMeshDescription, baker.IsStatic());
            componentFlag.AppendDepthSortedFlag(renderer.sharedMaterials);
            lodState.AppendLODFlags(ref componentFlag);
            baker.AddComponent(additionalEntities, RenderMeshUtility.ComputeComponentTypes(componentFlag));
            baker.SetSharedComponent(additionalEntities, renderMeshDescription.FilterSettings);

            // Add lightmap components if the renderer is lightmapped
            if (RenderMeshUtility.IsLightMapped(renderer.lightmapIndex))
                baker.AddComponent(additionalEntities, RenderMeshUtility.LightmapComponents);

            for (ushort subMeshMaterialIndex = 0; subMeshMaterialIndex < materials.Length; subMeshMaterialIndex++)
            {
                var meshEntity = additionalEntities[subMeshMaterialIndex];
                baker.SetComponent(meshEntity, new RenderMeshUnmanaged(mesh, materials[subMeshMaterialIndex], new SubMeshIndexInfo32(subMeshMaterialIndex)));
                baker.SetComponent(meshEntity, renderBounds);
                lodState.SetLODComponent(baker, meshEntity);
            }
        }

        static bool ValidateMeshAndMaterials(Renderer authoring, Mesh mesh, Material[] materials)
        {
            if (mesh == null || materials == null || materials.Length == 0)
            {
                Debug.LogWarning(
                    $"Renderer on GameObject \"{authoring.name}\" was not converted. The assigned mesh is null or no materials are assigned.",
                    authoring);

                return false;
            }

            string errorMessage = "";

            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null)
                    errorMessage += $"Material ({i}) is null. ";
            }

            if (!string.IsNullOrEmpty(errorMessage))
            {
                Debug.LogWarning(
                    $"Renderer on GameObject \"{authoring.name}\" has invalid Materials and will not render correctly at runtime. {errorMessage}",
                    authoring);
            }

            return true;
        }
    }
}
