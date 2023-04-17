using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.Rendering
{
    /// <summary>
    /// Represents which materials and meshes to use to render an entity.
    /// </summary>
    /// <remarks>
    /// This struct supports both a serializable static encoding in which case Material and Mesh are
    /// array indices to some array (typically a RenderMeshArray), and direct use of
    /// runtime BatchRendererGroup BatchMaterialID / BatchMeshID values.
    /// </remarks>
    public struct MaterialMeshInfo : IComponentData
    {

        /// <summary>
        /// The material ID.
        /// </summary>
        public int Material;

        /// <summary>
        /// The mesh ID.
        /// </summary>
        public int Mesh;

        /// <summary>
        /// The sub-mesh ID.
        /// </summary>
        public sbyte Submesh;

        internal bool IsRuntimeMaterial => Material >= 0;
        internal bool IsRuntimeMesh => Mesh >= 0;


        /// <summary>
        /// Converts the given array index (typically the index inside RenderMeshArray) into
        /// a negative number that denotes that array position.
        /// </summary>
        /// <param name="index">The index to convert.</param>
        /// <returns>Returns the converted index.</returns>
        public static int ArrayIndexToStaticIndex(int index) => (index < 0)
            ? index
            : (-index - 1);

        /// <summary>
        /// Converts the given static index (a negative value) to a valid array index.
        /// </summary>
        /// <param name="staticIndex">The index to convert.</param>
        /// <returns>Returns the converted index.</returns>
        public static int StaticIndexToArrayIndex(int staticIndex) => math.abs(staticIndex) - 1;

        /// <summary>
        /// Creates an instance of MaterialMeshInfo from material and mesh/sub-mesh indices in the corresponding RenderMeshArray.
        /// </summary>
        /// <param name="materialIndexInRenderMeshArray">The material index in <see cref="RenderMeshArray.Materials"/>.</param>
        /// <param name="meshIndexInRenderMeshArray">The mesh index in <see cref="RenderMeshArray.Meshes"/>.</param>
        /// <param name="submeshIndex">An optional submesh ID.</param>
        /// <returns>Returns the MaterialMeshInfo instance that contains the material and mesh indices.</returns>
        public static MaterialMeshInfo FromRenderMeshArrayIndices(
            int materialIndexInRenderMeshArray,
            int meshIndexInRenderMeshArray,
            sbyte submeshIndex = 0)
        {
            return new MaterialMeshInfo(
                ArrayIndexToStaticIndex(materialIndexInRenderMeshArray),
                ArrayIndexToStaticIndex(meshIndexInRenderMeshArray),
                submeshIndex);
        }

        private MaterialMeshInfo(int materialIndex, int meshIndex, sbyte submeshIndex = 0)
        {
            Material = materialIndex;
            Mesh = meshIndex;
            Submesh = submeshIndex;
        }

        /// <summary>
        /// Creates an instance of MaterialMeshInfo from material and mesh/sub-mesh IDs registered with <see cref="EntitiesGraphicsSystem"/>
        /// </summary>
        /// <param name="materialID">The material ID from <see cref="EntitiesGraphicsSystem.RegisterMaterial"/>.</param>
        /// <param name="meshID">The mesh ID from <see cref="EntitiesGraphicsSystem.RegisterMesh"/>.</param>
        /// <param name="submeshIndex">An optional submesh ID.</param>
        public MaterialMeshInfo(BatchMaterialID materialID, BatchMeshID meshID, sbyte submeshIndex = 0)
            : this((int)materialID.value, (int)meshID.value, submeshIndex)
        {}

        /// <summary>
        /// The mesh ID property.
        /// </summary>
        public BatchMeshID MeshID
        {
            get
            {
                Debug.Assert(IsRuntimeMesh);
                return new BatchMeshID { value = (uint)Mesh };
            }

            set => Mesh = (int) value.value;
        }

        /// <summary>
        /// The material ID property.
        /// </summary>
        public BatchMaterialID MaterialID
        {
            get
            {
                Debug.Assert(IsRuntimeMaterial);
                return new BatchMaterialID() { value = (uint)Material };
            }

            set => Material = (int) value.value;
        }

        internal int MeshArrayIndex
        {
            get => IsRuntimeMesh ? -1 : StaticIndexToArrayIndex(Mesh);
            set => Mesh = ArrayIndexToStaticIndex(value);
        }

        internal int MaterialArrayIndex
        {
            get => IsRuntimeMaterial ? -1 : StaticIndexToArrayIndex(Material);
            set => Material = ArrayIndexToStaticIndex(value);
        }
    }

    internal struct AssetHash
    {
        public static void UpdateAsset(ref xxHash3.StreamingState hash, UnityEngine.Object asset)
        {
            // In the editor we can compute a stable serializable hash using an asset GUID
#if UNITY_EDITOR
            bool success = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId);
            hash.Update(success);
            if (!success)
            {
                hash.Update(asset.GetInstanceID());
                return;
            }
            var guidBytes = Encoding.UTF8.GetBytes(guid);

            hash.Update(guidBytes.Length);
            for (int j = 0; j < guidBytes.Length; ++j)
                hash.Update(guidBytes[j]);
            hash.Update(localId);
#else
            // In standalone, we have to resort to using the instance ID which is not serializable,
            // but should be usable in the context of this execution.
            hash.Update(asset.GetInstanceID());
#endif
        }
    }

    /// <summary>
    /// A shared component that contains meshes and materials.
    /// </summary>
    public struct RenderMeshArray : ISharedComponentData, IEquatable<RenderMeshArray>
    {
        [SerializeField] private Material[] m_Materials;
        [SerializeField] private Mesh[] m_Meshes;
        // Memoize the expensive 128-bit hash
        [SerializeField] private uint4 m_Hash128;

        /// <summary>
        /// Constructs an instance of RenderMeshArray from an array of materials and an array of meshes.
        /// </summary>
        /// <param name="materials">The array of materials to use in the RenderMeshArray.</param>
        /// <param name="meshes">The array of meshes to use in the RenderMeshArray.</param>
        public RenderMeshArray(Material[] materials, Mesh[] meshes)
        {
            m_Meshes = meshes;
            m_Materials = materials;
            m_Hash128 = uint4.zero;
            ResetHash128();
        }

        /// <summary>
        /// Accessor property for the meshes array.
        /// </summary>
        public Mesh[] Meshes
        {
            get => m_Meshes;
            set
            {
                m_Hash128 = uint4.zero;
                m_Meshes = value;
            }
        }

        /// <summary>
        /// Accessor property for the materials array.
        /// </summary>
        public Material[] Materials
        {
            get => m_Materials;
            set
            {
                m_Hash128 = uint4.zero;
                m_Materials = value;
            }
        }

        internal Mesh GetMeshWithStaticIndex(int staticMeshIndex)
        {
            Debug.Assert(staticMeshIndex <= 0, "Mesh index must be a static index (non-positive)");

            if (staticMeshIndex >= 0)
                return null;

            return m_Meshes[MaterialMeshInfo.StaticIndexToArrayIndex(staticMeshIndex)];
        }

        internal Material GetMaterialWithStaticIndex(int staticMaterialIndex)
        {
            Debug.Assert(staticMaterialIndex <= 0, "Material index must be a static index (non-positive)");

            if (staticMaterialIndex >= 0)
                return null;

            return m_Materials[MaterialMeshInfo.StaticIndexToArrayIndex(staticMaterialIndex)];
        }

        internal Dictionary<Mesh, int> GetMeshToIndexMapping()
        {
            var mapping = new Dictionary<Mesh, int>();

            if (m_Meshes == null)
                return mapping;

            int numMeshes = m_Meshes.Length;

            for (int i = 0; i < numMeshes; ++i)
                mapping[m_Meshes[i]] = MaterialMeshInfo.ArrayIndexToStaticIndex(i);

            return mapping;
        }

        internal Dictionary<Material, int> GetMaterialToIndexMapping()
        {
            var mapping = new Dictionary<Material, int>();

            if (m_Materials == null)
                return mapping;

            int numMaterials = m_Materials.Length;

            for (int i = 0; i < numMaterials; ++i)
                mapping[m_Materials[i]] = MaterialMeshInfo.ArrayIndexToStaticIndex(i);

            return mapping;
        }


        /// <summary>
        /// Returns a 128-bit hash that (almost) uniquely identifies the contents of the component.
        /// </summary>
        /// <remarks>
        /// This is useful to help make comparisons between RenderMeshArray instances less resource intensive.
        /// </remarks>
        /// <returns>Returns the 128-bit hash value.</returns>
        public uint4 GetHash128()
        {
            return m_Hash128;
        }

        /// <summary>
        /// Recalculates the 128-bit hash value of the component.
        /// </summary>
        public void ResetHash128()
        {
            m_Hash128 = ComputeHash128();
        }

        /// <summary>
        /// Calculates and returns the 128-bit hash value of the component contents.
        /// </summary>
        /// <remarks>
        /// This is equivalent to calling <see cref="ResetHash128"/> and then <see cref="GetHash128"/>.
        /// </remarks>
        /// <returns>Returns the calculated 128-bit hash value.</returns>
        public uint4 ComputeHash128()
        {
            var hash = new xxHash3.StreamingState(false);

            int numMeshes = m_Meshes?.Length ?? 0;
            int numMaterials = m_Materials?.Length ?? 0;

            hash.Update(numMeshes);
            hash.Update(numMaterials);

            for (int i = 0; i < numMeshes; ++i)
                AssetHash.UpdateAsset(ref hash, m_Meshes[i]);

            for (int i = 0; i < numMaterials; ++i)
                AssetHash.UpdateAsset(ref hash, m_Materials[i]);

            uint4 H = hash.DigestHash128();

            // Make sure the hash is never exactly zero, to keep zero as a null value
            if (math.all(H == uint4.zero))
                return new uint4(1, 0, 0, 0);

            return H;
        }

        /// <summary>
        /// Combines a list of RenderMeshes into one RenderMeshArray.
        /// </summary>
        /// <param name="renderMeshes">The list of RenderMesh instances to combine.</param>
        /// <returns>Returns a RenderMeshArray instance that contains containing all of the meshes and materials.</returns>
        public static RenderMeshArray CombineRenderMeshes(List<RenderMesh> renderMeshes)
        {
            var meshes = new Dictionary<Mesh, bool>(renderMeshes.Count);
            var materials = new Dictionary<Material, bool>(renderMeshes.Count);

            foreach (var rm in renderMeshes)
            {
                meshes[rm.mesh] = true;
                materials[rm.material] = true;
            }

            return new RenderMeshArray(materials.Keys.ToArray(), meshes.Keys.ToArray());
        }

        /// <summary>
        /// Combines a list of RenderMeshArrays into one RenderMeshArray.
        /// </summary>
        /// <param name="renderMeshArrays">The list of RenderMeshArray instances to combine.</param>
        /// <returns>Returns a RenderMeshArray instance that contains all of the meshes and materials.</returns>
        public static RenderMeshArray CombineRenderMeshArrays(List<RenderMeshArray> renderMeshArrays)
        {
            int totalMeshes = 0;
            int totalMaterials = 0;

            foreach (var rma in renderMeshArrays)
            {
                totalMeshes += rma.Meshes?.Length ?? 0;
                totalMaterials += rma.Meshes?.Length ?? 0;
            }

            var meshes = new Dictionary<Mesh, bool>(totalMeshes);
            var materials = new Dictionary<Material, bool>(totalMaterials);

            foreach (var rma in renderMeshArrays)
            {
                foreach (var mesh in rma.Meshes)
                    meshes[mesh] = true;

                foreach (var material in rma.Materials)
                    materials[material] = true;
            }

            return new RenderMeshArray(materials.Keys.ToArray(), meshes.Keys.ToArray());
        }

        /// <summary>
        /// Creates the new instance of the RenderMeshArray from given mesh and material lists, removing duplicate entries.
        /// </summary>
        /// <param name="materialsWithDuplicates">The list of the materials.</param>
        /// <param name="meshesWithDuplicates">The list of the meshes.</param>
        /// <returns>Returns a RenderMeshArray instance that contains all off the meshes and materials, and with no duplicates.</returns>
        public static RenderMeshArray CreateWithDeduplication(
            List<Material> materialsWithDuplicates, List<Mesh> meshesWithDuplicates)
        {
            var meshes = new Dictionary<Mesh, bool>(meshesWithDuplicates.Count);
            var materials = new Dictionary<Material, bool>(materialsWithDuplicates.Count);

            foreach (var mat in materialsWithDuplicates)
                materials[mat] = true;

            foreach (var mesh in meshesWithDuplicates)
                meshes[mesh] = true;

            return new RenderMeshArray(materials.Keys.ToArray(), meshes.Keys.ToArray());
        }


        /// <summary>
        /// Gets the material for given MaterialMeshInfo.
        /// </summary>
        /// <param name="materialMeshInfo">The MaterialMeshInfo to use.</param>
        /// <returns>Returns the associated material instance, or null if the material is runtime.</returns>
        public Material GetMaterial(MaterialMeshInfo materialMeshInfo)
        {
            if (materialMeshInfo.IsRuntimeMaterial)
                return null;
            else
                return Materials[materialMeshInfo.MaterialArrayIndex];
        }

        /// <summary>
        /// Gets the mesh for given MaterialMeshInfo.
        /// </summary>
        /// <param name="materialMeshInfo">The MaterialMeshInfo to use.</param>
        /// <returns>Returns the associated Mesh instance or null if the mesh is runtime.</returns>
        public Mesh GetMesh(MaterialMeshInfo materialMeshInfo)
        {
            if (materialMeshInfo.IsRuntimeMesh)
                return null;
            else
                return Meshes[materialMeshInfo.MeshArrayIndex];
        }

        /// <summary>
        /// Determines whether two object instances are equal based on their hashes.
        /// </summary>
        /// <param name="other">The object to compare with the current object.</param>
        /// <returns>Returns true if the specified object is equal to the current object. Otherwise, returns false.</returns>
        public bool Equals(RenderMeshArray other)
        {
            return math.all(GetHash128() == other.GetHash128());
        }

        /// <summary>
        /// Determines whether two object instances are equal based on their hashes.
        /// </summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns>Returns true if the specified object is equal to the current object. Otherwise, returns false.</returns>
        public override bool Equals(object obj)
        {
            return obj is RenderMeshArray other && Equals(other);
        }

        /// <summary>
        /// Calculates the hash code for this object.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            return (int) GetHash128().x;
        }

        /// <summary>
        /// The equality operator == returns true if its operands are equal, false otherwise.
        /// </summary>
        /// <param name="left">The left instance to compare.</param>
        /// <param name="right">The right instance to compare.</param>
        /// <returns>True if left and right instances are equal and false otherwise.</returns>
        public static bool operator ==(RenderMeshArray left, RenderMeshArray right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// The not equality operator != returns false if its operands are equal, true otherwise.
        /// </summary>
        /// <param name="left">The left instance to compare.</param>
        /// <param name="right">The right instance to compare.</param>
        /// <returns>False if left and right instances are equal and true otherwise.</returns>
        public static bool operator !=(RenderMeshArray left, RenderMeshArray right)
        {
            return !left.Equals(right);
        }
    }
}
