using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace Unity.Entities.Graphics.Tests
{
    internal class CompanionBakingTests
    {
        readonly List<GameObject> m_TestGameObjects = new();
        World m_TestWorld;
        World m_PreviousWorld;
        EntityManager m_Manager;

        static BakingSettings MakeDefaultSettings() => new()
        {
            BakingFlags = BakingUtility.BakingFlags.AssignName
        };

        [SetUp]
        public void SetUp()
        {
            m_PreviousWorld = World.DefaultGameObjectInjectionWorld;
            m_TestWorld = new World("TestWorld");
            World.DefaultGameObjectInjectionWorld = m_TestWorld;
            m_Manager = World.DefaultGameObjectInjectionWorld.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in m_TestGameObjects)
                Object.DestroyImmediate(go);
            m_TestGameObjects.Clear();

            World.DefaultGameObjectInjectionWorld = m_PreviousWorld;
            m_TestWorld.Dispose();
        }

        [Test]
        public void CompanionBaking_Baking_Light_Succeeds()
        {
            var wasSuccessful = CanTypeBeBaked<UnityEngine.Light>();
            Assert.That(wasSuccessful, Is.True);
        }

        [Test]
        public void CompanionBaking_Baking_LightProbeProxyVolume_Succeeds()
        {
            var wasSuccessful = CanTypeBeBaked<UnityEngine.LightProbeProxyVolume>();
            Assert.That(wasSuccessful, Is.True);
        }

        [Test]
        public void CompanionBaking_Baking_ReflectionProbe_Succeeds()
        {
            var wasSuccessful = CanTypeBeBaked<UnityEngine.ReflectionProbe>();
            Assert.That(wasSuccessful, Is.True);
        }

        [Test]
        public void CompanionBaking_Baking_TextMesh_Succeeds()
        {
            var wasSuccessful = CanTypeBeBaked<UnityEngine.TextMesh>();
            Assert.That(wasSuccessful, Is.True);
        }

        [Test]
        public void CompanionBaking_Baking_SpriteRenderer_Succeeds()
        {
            var wasSuccessful = CanTypeBeBaked<UnityEngine.SpriteRenderer>();
            Assert.That(wasSuccessful, Is.True);
        }

        [Test]
        public void CompanionBaking_Baking_VisualEffect_Succeeds()
        {
            var wasSuccessful = CanTypeBeBaked<UnityEngine.VFX.VisualEffect>();
            Assert.That(wasSuccessful, Is.True);
        }

        [Test]
        public void CompanionBaking_Baking_ParticleSystem_Succeeds()
        {
            var wasSuccessful = CanTypeBeBaked<UnityEngine.ParticleSystem>();
            Assert.That(wasSuccessful, Is.True);
        }

        [Test]
        public void CompanionBaking_Baking_AudioSource_Succeeds()
        {
            var wasSuccessful = CanTypeBeBaked<UnityEngine.AudioSource>();
            Assert.That(wasSuccessful, Is.True);
        }

#if SRP_10_0_0_OR_NEWER
        [Test]
        public void CompanionBaking_Baking_Volume_Succeeds()
        {
            var wasSuccessful = CanTypeBeBaked<UnityEngine.Rendering.Volume>();
            Assert.That(wasSuccessful, Is.True);
        }
#endif

#if URP_10_0_0_OR_NEWER
        [Test]
        public void CompanionBaking_Baking_UniversalAdditionalLightData_Succeeds()
        {
            var wasSuccessful = CanTypeBeBaked<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            Assert.That(wasSuccessful, Is.True);
        }

        [Test]
        public void CompanionBaking_Baking_DecalProjector_Succeeds()
        {
            var wasSuccessful = CanTypeBeBaked<UnityEngine.Rendering.Universal.DecalProjector>();
            Assert.That(wasSuccessful, Is.True);
        }
#endif

#if SRP_17_0_0_OR_NEWER
        [Test]
        public void CompanionBaking_Baking_AdaptiveProbeVolume_Succeeds()
        {
            var wasSuccessful = CanTypeBeBaked<UnityEngine.Rendering.ProbeVolume>();
            Assert.That(wasSuccessful, Is.True);
        }
        
        [Test]
        public void CompanionBaking_Baking_ProbeVolumePerSceneData_Succeeds()
        {
            var wasSuccessful = CanTypeBeBaked<UnityEngine.Rendering.ProbeVolumePerSceneData>();
            Assert.That(wasSuccessful, Is.True);
        }
#endif

        bool CanTypeBeBaked<T>() where T : UnityEngine.Component
        {
            var gameObject = CreateGameObject(typeof(T).Name);
            gameObject.AddComponent<T>();

            Entity entity = default;
            Assert.DoesNotThrow(() =>
            {
                entity = BakeCompanionComponent<T>(gameObject);
            });

            var component = m_Manager.GetComponentObject<T>(entity);
            return component != null;
        }

        GameObject CreateGameObject(string name)
        {
            var newGo = new GameObject(name);
            m_TestGameObjects.Add(newGo);
            return newGo;
        }

        Entity BakeCompanionComponent<T>(GameObject gameObject) where T : UnityEngine.Component
        {
            using var blobAssetStore = new BlobAssetStore(128);
            var bakingSettings = MakeDefaultSettings();
            bakingSettings.BlobAssetStore = blobAssetStore;
            BakingUtility.BakeGameObjects(World.DefaultGameObjectInjectionWorld, new[] {gameObject}, bakingSettings);
            var findEntityQuery = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { new ComponentType(typeof(T)) }
            });
            using var entities = findEntityQuery.ToEntityArray(Allocator.Temp);
            Assume.That(entities.Length, Is.EqualTo(1), $"Could not find an Entity with a component of type {typeof(T)}");
            return entities[0];
        }
    }
}
