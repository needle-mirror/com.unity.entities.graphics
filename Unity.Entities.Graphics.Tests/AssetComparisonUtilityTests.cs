using NUnit.Framework;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Rendering;
using UnityEngine;

namespace Unity.Entities.Graphics.Tests
{
    internal class AssetComparisonUtilityTests
    {
        readonly List<Object> m_TestAssets = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in m_TestAssets)
                Object.DestroyImmediate(asset);
            m_TestAssets.Clear();
        }

        Material CreateRuntimeMaterial(params string[] keywords)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            var material = new Material(shader);
            foreach (var keyword in keywords)
                material.EnableKeyword(keyword);
            m_TestAssets.Add(material);
            return material;
        }

        Texture2D CreateTexture(Color color)
        {
            var texture = new Texture2D(4, 4);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    texture.SetPixel(x, y, color);
            texture.Apply();
            texture.imageContentsHash = UnityEngine.Hash128.Compute(color.ToString());
            m_TestAssets.Add(texture);
            return texture;
        }

        void SortMaterials(UnityObjectRef<Material>[] refs)
        {
            System.Array.Sort(refs, (a, b) => AssetComparisonUtility.CompareUnityObjectRef(a, b));
        }

        [Test]
        public void RuntimeMaterial_ComparesConsistently()
        {
            var mat = CreateRuntimeMaterial();

            var refA = (UnityObjectRef<Material>)mat;
            var refB = (UnityObjectRef<Material>)mat;

            var result = AssetComparisonUtility.CompareUnityObjectRef(refA, refB);

            Assert.That(result, Is.EqualTo(0), "Same material should compare as equal");
        }

        [Test]
        public void RuntimeMaterials_WithDifferentKeywords_CompareAsNotEqual()
        {
            var mat1 = CreateRuntimeMaterial("LIGHTMAP_ON");
            var mat2 = CreateRuntimeMaterial("DIRLIGHTMAP_COMBINED");

            var ref1 = (UnityObjectRef<Material>)mat1;
            var ref2 = (UnityObjectRef<Material>)mat2;

            var result = AssetComparisonUtility.CompareUnityObjectRef(ref1, ref2);

            Assert.That(result, Is.Not.EqualTo(0), "Materials with different keywords should not compare as equal");
        }

        [Test]
        public void RuntimeMaterials_SortStably()
        {
            var mat1 = CreateRuntimeMaterial("LIGHTMAP_ON");
            var mat2 = CreateRuntimeMaterial("DIRLIGHTMAP_COMBINED");

            var refs = new UnityObjectRef<Material>[]
            {
                (UnityObjectRef<Material>)mat1,
                (UnityObjectRef<Material>)mat2
            };

            SortMaterials(refs);
            var firstMat = refs[0].Value;
            var secondMat = refs[1].Value;

            refs[0] = (UnityObjectRef<Material>)mat2;
            refs[1] = (UnityObjectRef<Material>)mat1;

            SortMaterials(refs);

            Assert.That(refs[0].Value, Is.EqualTo(firstMat));
            Assert.That(refs[1].Value, Is.EqualTo(secondMat));
        }

        [Test]
        public void RuntimeMaterials_WithDifferentTextures_CompareAsNotEqual()
        {
            var mat1 = CreateRuntimeMaterial();
            mat1.mainTexture = CreateTexture(Color.red);

            var mat2 = CreateRuntimeMaterial();
            mat2.mainTexture = CreateTexture(Color.blue);

            var ref1 = (UnityObjectRef<Material>)mat1;
            var ref2 = (UnityObjectRef<Material>)mat2;

            var result = AssetComparisonUtility.CompareUnityObjectRef(ref1, ref2);

            Assert.That(result, Is.Not.EqualTo(0), "Materials with different textures should not compare as equal");
        }

        [Test]
        public void RuntimeMaterials_WithDifferentTextures_SortStably()
        {
            var mat1 = CreateRuntimeMaterial();
            mat1.mainTexture = CreateTexture(Color.red);

            var mat2 = CreateRuntimeMaterial();
            mat2.mainTexture = CreateTexture(Color.blue);

            var refs = new UnityObjectRef<Material>[]
            {
                (UnityObjectRef<Material>)mat1,
                (UnityObjectRef<Material>)mat2
            };

            SortMaterials(refs);
            var firstMat = refs[0].Value;
            var secondMat = refs[1].Value;

            refs[0] = (UnityObjectRef<Material>)mat2;
            refs[1] = (UnityObjectRef<Material>)mat1;

            SortMaterials(refs);

            Assert.That(refs[0].Value, Is.EqualTo(firstMat));
            Assert.That(refs[1].Value, Is.EqualTo(secondMat));
        }

        [Test]
        public void URPMaterials_WithLightmapKeyword_CompareAsNotEqual()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Assert.Ignore("URP shader not available, skipping URP-specific test");
                return;
            }

            var mat1 = CreateRuntimeMaterial("LIGHTMAP_ON");
            mat1.SetTexture("_BaseMap", CreateTexture(Color.red));

            var mat2 = CreateRuntimeMaterial("LIGHTMAP_ON");
            mat2.SetTexture("_BaseMap", CreateTexture(Color.blue));

            var ref1 = (UnityObjectRef<Material>)mat1;
            var ref2 = (UnityObjectRef<Material>)mat2;

            var result = AssetComparisonUtility.CompareUnityObjectRef(ref1, ref2);

            Assert.That(result, Is.Not.EqualTo(0), "URP materials with LIGHTMAP_ON and different textures should not compare as equal");
        }

        [Test]
        public void URPMaterials_WithLightmapKeyword_SortStably()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Assert.Ignore("URP shader not available, skipping URP-specific test");
                return;
            }

            var mat1 = CreateRuntimeMaterial("LIGHTMAP_ON");
            mat1.SetTexture("_BaseMap", CreateTexture(Color.red));

            var mat2 = CreateRuntimeMaterial("LIGHTMAP_ON");
            mat2.SetTexture("_BaseMap", CreateTexture(Color.blue));

            var refs = new UnityObjectRef<Material>[]
            {
                (UnityObjectRef<Material>)mat1,
                (UnityObjectRef<Material>)mat2
            };

            SortMaterials(refs);
            var firstMat = refs[0].Value;
            var secondMat = refs[1].Value;

            refs[0] = (UnityObjectRef<Material>)mat2;
            refs[1] = (UnityObjectRef<Material>)mat1;

            SortMaterials(refs);

            Assert.That(refs[0].Value, Is.EqualTo(firstMat));
            Assert.That(refs[1].Value, Is.EqualTo(secondMat));
        }

    }
}
