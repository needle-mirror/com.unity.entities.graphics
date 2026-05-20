using NUnit.Framework;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Rendering;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Unity.Entities.Graphics.Tests
{
    internal class MeshRendererBakingTests
    {
        World m_World;
        World m_PreviousWorld;
        BakingSystem m_BakingSystem;
        BlobAssetStore m_BlobAssetStore;
        readonly List<Object> m_TestAssets = new();

        [SetUp]
        public void Setup()
        {
            m_PreviousWorld = World.DefaultGameObjectInjectionWorld;
            m_World = new World("Test World");
            World.DefaultGameObjectInjectionWorld = m_World;

            m_BakingSystem = m_World.GetOrCreateSystemManaged<BakingSystem>();

            m_BlobAssetStore = new BlobAssetStore(128);
            var bakingSettings = MakeDefaultSettings();
            bakingSettings.BlobAssetStore = m_BlobAssetStore;

            m_BakingSystem.BakingSettings = bakingSettings;
        }

        [TearDown]
        public void TearDown()
        {
            if (m_BlobAssetStore.IsCreated)
                m_BlobAssetStore.Dispose();

            foreach (var asset in m_TestAssets)
                Object.DestroyImmediate(asset);
            m_TestAssets.Clear();

            World.DefaultGameObjectInjectionWorld = m_PreviousWorld;
            if (m_World != null && m_World.IsCreated)
                m_World.Dispose();
        }

        /// <summary>
        /// Sanity check, to make sure the baking does occur when modifying the transform.
        /// </summary>
        [Test]
        public void MeshRendererBaking_Rebake_UpdatesTransform()
        {
            var go = CreateMeshRendererGameObject("MeshRenderer", "Mesh", "Material");

            Bake(go);
            var entityCountAfterFirstBake = GetMaterialMeshInfoCount();
            Assume.That(entityCountAfterFirstBake, Is.EqualTo(1), "First bake should create exactly one entity");

            using var infoQuery1 = m_BakingSystem.EntityManager.CreateEntityQuery(typeof(MaterialMeshInfo));
            using var entities = infoQuery1.ToEntityArray(Allocator.Temp);
            Assume.That(entities.Length, Is.EqualTo(1));
            var entity = entities[0];

            var positionAfterFirstBake = m_BakingSystem.EntityManager.GetComponentData<LocalToWorld>(entity).Position;
            Assume.That(positionAfterFirstBake, Is.EqualTo(float3.zero), "Initial position should be origin");

            m_BakingSystem.EntityManager.CompleteAllTrackedJobs();
            go.transform.position = new Vector3(5, 10, 15);

            Bake(go);
            var entityCountAfterSecondBake = GetMaterialMeshInfoCount();
            Assert.That(entityCountAfterSecondBake, Is.EqualTo(1), "Entity count should remain the same after rebake");
            
            using var infoQuery2 = m_BakingSystem.EntityManager.CreateEntityQuery(typeof(MaterialMeshInfo));
            using var entitiesAfterRebake = infoQuery2.ToEntityArray(Allocator.Temp);
            entity = entitiesAfterRebake[0];

            var positionAfterRebake = m_BakingSystem.EntityManager.GetComponentData<LocalToWorld>(entity).Position;
            Assert.That(positionAfterRebake, Is.EqualTo(new float3(5, 10, 15)), "Position should be updated to new transform position, proving rebake occurred");
        }

        [Test]
        public void MeshRendererBaking_SingleMaterial_HasDeterministicOrdering()
        {
            var go1 = CreateMeshRendererGameObject("MeshRenderer1", "Mesh1", "Material1");
            var go2 = CreateMeshRendererGameObject("MeshRenderer2", "Mesh2", "Material2");
            var go3 = CreateMeshRendererGameObject("MeshRenderer3", "Mesh3", "Material3");

            // First bake
            Bake(go1, go2, go3);
            var infos1 = GetAllMaterialMeshInfos();

            // Modify GameObject and rebake all
            m_BakingSystem.EntityManager.CompleteAllTrackedJobs();
            go1.transform.position = new Vector3(1, 0, 0);

            // Second bake
            Bake(go1, go2, go3);
            var infos2 = GetAllMaterialMeshInfos();

            Assume.That(infos2.Length, Is.EqualTo(infos1.Length), "Entity count changed between bakes");

            // Verify: Material and Mesh indices must remain stable across rebakes to avoid material shuffling.
            for (var i = 0; i < infos1.Length; i++)
            {
                Assert.That(infos2[i].Mesh, Is.EqualTo(infos1[i].Mesh), $"Entity {i}: Mesh changed between bakes. First: {infos1[i].Mesh}, Second: {infos2[i].Mesh}");
                Assert.That(infos2[i].Material, Is.EqualTo(infos1[i].Material), $"Entity {i}: Material changed between bakes. First: {infos1[i].Material}, Second: {infos2[i].Material}");
            }
        }

        [Test]
        public void MeshRendererBaking_MultiMaterial_HasDeterministicOrdering()
        {
            var go1 = CreateMeshRendererGameObject("MultiMaterial1", "Mesh1", "Mat1", "Mat2", "Mat3");
            var go2 = CreateMeshRendererGameObject("MultiMaterial2", "Mesh2", "Mat4", "Mat5");
            var go3 = CreateMeshRendererGameObject("MultiMaterial3", "Mesh3", "Mat6", "Mat7", "Mat8", "Mat9");

            // First bake
            Bake(go1, go2, go3);
            var infos1 = GetAllMaterialMeshInfos();

            // Modify GameObject and rebake all
            m_BakingSystem.EntityManager.CompleteAllTrackedJobs();
            go2.transform.position = new Vector3(1, 0, 0);

            // Second bake
            Bake(go1, go2, go3);
            var infos2 = GetAllMaterialMeshInfos();

            Assume.That(infos2.Length, Is.EqualTo(infos1.Length), "Entity count changed between bakes");

            // Verify: MaterialMeshInfo must remain stable for multi-material meshes across rebakes.
            for (var i = 0; i < infos1.Length; i++)
            {
                Assert.That(infos2[i].Mesh, Is.EqualTo(infos1[i].Mesh), $"Entity {i}: Mesh changed between bakes. First: {infos1[i].Mesh}, Second: {infos2[i].Mesh}");
                Assert.That(infos2[i].Material, Is.EqualTo(infos1[i].Material), $"Entity {i}: Material changed between bakes. First: {infos1[i].Material}, Second: {infos2[i].Material}");
            }
        }

        [Test]
        public void MeshRendererBaking_SharedMeshMaterial_DeduplicatesCorrectly()
        {
            // Create shared mesh and material for go1 and go4
            var sharedMesh = CreateTestMesh("MeshA");
            var sharedMaterial = CreateTestMaterial("MaterialX");

            var go1 = CreateMeshRendererGameObject("Renderer1", sharedMesh, sharedMaterial);
            var go2 = CreateMeshRendererGameObject("Renderer2", "MeshB", "MaterialY");
            var go3 = CreateMeshRendererGameObject("Renderer3", "MeshC", "MaterialZ");
            var go4 = CreateMeshRendererGameObject("Renderer4", sharedMesh, sharedMaterial); // Same mesh/material as go1

            // First bake
            Bake(go1, go2, go3, go4);
            var infos1 = GetAllMaterialMeshInfos();

            // Modify GameObject and rebake all
            m_BakingSystem.EntityManager.CompleteAllTrackedJobs();
            go3.transform.position = new Vector3(1, 0, 0);

            // Second bake
            Bake(go1, go2, go3, go4);
            var infos2 = GetAllMaterialMeshInfos();

            Assume.That(infos2.Length, Is.EqualTo(infos1.Length), "Entity count changed between bakes");
            Assume.That(infos1.Length, Is.GreaterThanOrEqualTo(4), "Should have at least 4 entities after baking");

            // Verify: Mesh and Material indices remain stable despite different mesh entityIds.
            for (var i = 0; i < infos1.Length; i++)
            {
                Assert.That(infos2[i].Mesh, Is.EqualTo(infos1[i].Mesh), $"Entity {i}: Mesh changed between bakes. First: {infos1[i].Mesh}, Second: {infos2[i].Mesh}");
                Assert.That(infos2[i].Material, Is.EqualTo(infos1[i].Material), $"Entity {i}: Material changed between bakes. First: {infos1[i].Material}, Second: {infos2[i].Material}");
            }

            // Verify: Entities sharing same mesh/material should have same indices
            Assert.That(infos1[0].Mesh, Is.EqualTo(infos1[3].Mesh), "Entities with same mesh should reference same mesh index after first bake");
            Assert.That(infos1[0].Material, Is.EqualTo(infos1[3].Material), "Entities with same material should reference same material index after first bake");
            Assert.That(infos2[0].Mesh, Is.EqualTo(infos2[3].Mesh), "Entities with same mesh should reference same mesh index after second bake");
            Assert.That(infos2[0].Material, Is.EqualTo(infos2[3].Material), "Entities with same material should reference same material index after second bake");
        }

        [Test]
        public void MeshRendererBaking_IncrementalBake_MaintainsCorrectIndicesForRebaked()
        {
            var cubeGo = CreateMeshRendererGameObject("CubeRenderer", "CubeMesh", "GreenMaterial");
            var sphereGo = CreateMeshRendererGameObject("SphereRenderer", "SphereMesh", "RedMaterial");

            Bake(cubeGo, sphereGo);
            var infosAfterFullBake = GetAllMaterialMeshInfos();

            Assume.That(infosAfterFullBake.Length, Is.EqualTo(2), "Should have 2 entities after full bake");
            Assume.That(infosAfterFullBake[0].Mesh, Is.Not.EqualTo(infosAfterFullBake[1].Mesh), "The two entities should have different mesh indices");
            Assume.That(infosAfterFullBake[0].Material, Is.Not.EqualTo(infosAfterFullBake[1].Material), "The two entities should have different material indices");

            var cubeMeshIndex = infosAfterFullBake[0].Mesh;
            var cubeMaterialIndex = infosAfterFullBake[0].Material;
            var sphereMeshIndex = infosAfterFullBake[1].Mesh;
            var sphereMaterialIndex = infosAfterFullBake[1].Material;

            m_BakingSystem.EntityManager.CompleteAllTrackedJobs();
            sphereGo.transform.position = new Vector3(1, 0, 0);

            Bake(cubeGo, sphereGo);
            var infosAfterRebake = GetAllMaterialMeshInfos();

            Assume.That(infosAfterRebake.Length, Is.EqualTo(2), "Should still have 2 entities after rebake");

            Assert.That(infosAfterRebake[0].Mesh, Is.EqualTo(cubeMeshIndex), "Cube mesh index should remain unchanged");
            Assert.That(infosAfterRebake[0].Material, Is.EqualTo(cubeMaterialIndex), "Cube material index should remain unchanged");

            Assert.That(infosAfterRebake[1].Mesh, Is.EqualTo(sphereMeshIndex), $"Sphere mesh index should remain {sphereMeshIndex} after rebake. Got {infosAfterRebake[1].Mesh}. Rebaked entity is using wrong mesh index.");
            Assert.That(infosAfterRebake[1].Material, Is.EqualTo(sphereMaterialIndex), $"Sphere material index should remain {sphereMaterialIndex} after rebake. Got {infosAfterRebake[1].Material}. Rebaked entity is using wrong material index.");
        }

        [Test]
        public void MeshRendererBaking_MultipleSceneSections_ProcessesAllSectionsCorrectly()
        {
            // Create entities across multiple scene sections
            var go1 = CreateMeshRendererGameObjectWithSection("Section0_Obj1", 0, "Mesh1", "Material1");
            var go2 = CreateMeshRendererGameObjectWithSection("Section0_Obj2", 0, "Mesh2", "Material2");
            var go3 = CreateMeshRendererGameObjectWithSection("Section1_Obj1", 1, "Mesh3", "Material3");
            var go4 = CreateMeshRendererGameObjectWithSection("Section1_Obj2", 1, "Mesh4", "Material4");
            var go5 = CreateMeshRendererGameObjectWithSection("Section2_Obj1", 2, "Mesh5", "Material5");

            // Bake all entities across 3 different scene sections
            Bake(go1, go2, go3, go4, go5);

            // Verify all entities were processed without crashes
            var allInfos = GetAllMaterialMeshInfos();
            Assert.That(allInfos.Length, Is.EqualTo(5), "All 5 entities across 3 scene sections should be processed");

            // Verify entities have valid material/mesh indices (negative values indicate array indices)
            for (var i = 0; i < allInfos.Length; i++)
            {
                Assert.That(allInfos[i].Mesh, Is.LessThan(0), $"Entity {i} should have valid mesh array index (negative value). Got {allInfos[i].Mesh}");
                Assert.That(allInfos[i].Material, Is.LessThan(0), $"Entity {i} should have valid material array index (negative value). Got {allInfos[i].Material}");

                // Convert to array index and verify it's valid
                var meshArrayIndex = MaterialMeshInfo.StaticIndexToArrayIndex(allInfos[i].Mesh);
                var materialArrayIndex = MaterialMeshInfo.StaticIndexToArrayIndex(allInfos[i].Material);
                Assert.That(meshArrayIndex, Is.GreaterThanOrEqualTo(0), $"Entity {i} mesh array index should be >= 0");
                Assert.That(materialArrayIndex, Is.GreaterThanOrEqualTo(0), $"Entity {i} material array index should be >= 0");
            }
        }

        GameObject CreateMeshRendererGameObject(string name, string meshName, params string[] materialNames)
        {
            var go = CreateGameObject(name);
            var mr = go.AddComponent<MeshRenderer>();
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = CreateTestMesh(meshName);

            var materials = new Material[materialNames.Length];
            for (var i = 0; i < materialNames.Length; i++)
                materials[i] = CreateTestMaterial(materialNames[i]);

            mr.sharedMaterials = materials;

            var sceneSectionComponent = go.AddComponent<SceneSectionComponent>();
            sceneSectionComponent.SectionIndex = 0;

            return go;
        }

        GameObject CreateMeshRendererGameObject(string name, Mesh mesh, params Material[] materials)
        {
            var go = CreateGameObject(name);
            var mr = go.AddComponent<MeshRenderer>();
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            mr.sharedMaterials = materials;

            var sceneSectionComponent = go.AddComponent<SceneSectionComponent>();
            sceneSectionComponent.SectionIndex = 0;

            return go;
        }

        GameObject CreateMeshRendererGameObjectWithSection(string name, int sectionIndex, string meshName, params string[] materialNames)
        {
            var go = CreateGameObject(name);
            var mr = go.AddComponent<MeshRenderer>();
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = CreateTestMesh(meshName);

            var materials = new Material[materialNames.Length];
            for (var i = 0; i < materialNames.Length; i++)
                materials[i] = CreateTestMaterial(materialNames[i]);

            mr.sharedMaterials = materials;

            var sceneSectionComponent = go.AddComponent<SceneSectionComponent>();
            sceneSectionComponent.SectionIndex = sectionIndex;

            return go;
        }

        void Bake(params GameObject[] gameObjects) => BakingUtility.BakeGameObjects(m_World, gameObjects, m_BakingSystem.BakingSettings);

        int GetMaterialMeshInfoCount()
        {
            using var query = m_BakingSystem.EntityManager.CreateEntityQuery(typeof(MaterialMeshInfo));
            return query.CalculateEntityCount();
        }

        NativeArray<MaterialMeshInfo> GetAllMaterialMeshInfos()
        {
            using var query = m_BakingSystem.EntityManager.CreateEntityQuery(typeof(MaterialMeshInfo));
            return query.ToComponentDataArray<MaterialMeshInfo>(Allocator.Temp);
        }

        Mesh CreateTestMesh(string name)
        {
            var mesh = new Mesh
            {
                name = name,
                vertices = new[] { Vector3.zero, Vector3.up, Vector3.right },
                triangles = new[] { 0, 1, 2 }
            };
            m_TestAssets.Add(mesh);
            return mesh;
        }

        Material CreateTestMaterial(string name)
        {
            var material = new Material(Shader.Find("Standard")) { name = name };
            m_TestAssets.Add(material);
            return material;
        }

        GameObject CreateGameObject(string name)
        {
            var go = new GameObject(name);
            m_TestAssets.Add(go);
            return go;
        }

        static BakingSettings MakeDefaultSettings() => new()
        {
            BakingFlags = BakingUtility.BakingFlags.AssignName | BakingUtility.BakingFlags.AddEntityGUID
        };
    }
}
