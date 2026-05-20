using System.Collections.Generic;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;

namespace Unity.Entities.Graphics.PerformanceTests
{
    [TestFixture]
    internal class MeshRendererBakingPerformanceTests
    {
        World m_World;
        World m_PreviousWorld;
        BakingSystem m_BakingSystem;
        BlobAssetStore m_BlobAssetStore;
        readonly List<Object> m_TestAssets = new();
        const int k_MaxIterations = 30;

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
        /// Measures MeshRenderer baking performance with unique meshes and materials.
        /// </summary>
        /// <remarks>
        /// Creates N GameObjects, each with a unique mesh and unique material, representing the worst-case
        /// scenario for asset sorting and GUID lookups. This test measures the overhead of GUID-based
        /// deterministic sorting compared to instanceId-based sorting.
        /// </remarks>
        [Test, Performance]
        public void MeshRendererBaking_UniqueMeshesAndMaterials_Performance([Values(10, 100, 1000)] int meshCount)
        {
            var gameObjects = new GameObject[meshCount];
            for (var i = 0; i < meshCount; i++)
            {
                gameObjects[i] = CreateMeshRendererGameObject($"Renderer{i}", $"Mesh{i}", $"Material{i}");
            }

            // Warmup bake
            Bake(gameObjects);

            // Measure performance over multiple iterations
            for (var i = 0; i < k_MaxIterations; i++)
            {
                using (Measure.ProfilerMarkers(new SampleGroup("MeshRenderer Baking", SampleUnit.Millisecond)))
                {
                    Bake(gameObjects);
                }
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

        void Bake(params GameObject[] gameObjects) => BakingUtility.BakeGameObjects(m_World, gameObjects, m_BakingSystem.BakingSettings);

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
