using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Rendering
{
    /// <summary>
    /// Provides utilities for comparing Unity assets using stable identifiers.
    /// </summary>
    internal static partial class AssetComparisonUtility
    {
        /// <summary>
        /// Represents a stable asset identifier using GUID and LocalFileID for cross-session deterministic sorting.
        /// </summary>
        internal struct AssetIdentifier : System.IEquatable<AssetIdentifier>, System.IComparable<AssetIdentifier>
        {
            public Hash128 Guid;
            public long LocalFileId;
            public bool IsValid;

            public int CompareTo(AssetIdentifier other)
            {
                if (!IsValid && !other.IsValid) return 0;
                if (!IsValid) return 1;
                if (!other.IsValid) return -1;

                var guidCompare = Guid.CompareTo(other.Guid);
                if (guidCompare != 0)
                    return guidCompare;
                return LocalFileId.CompareTo(other.LocalFileId);
            }

            public bool Equals(AssetIdentifier other)
            {
                return IsValid == other.IsValid &&
                       Guid == other.Guid &&
                       LocalFileId == other.LocalFileId;
            }
        }

        static Dictionary<int, AssetIdentifier> s_AssetIdentifierCache = new();

        /// <summary>
        /// Retrieves the asset identifier for the given asset reference, using a cached value if available.
        /// </summary>
        /// <remarks>
        /// Asset-backed objects use GUID and LocalFileID from AssetDatabase for stable cross-session ordering.
        /// Runtime materials without GUIDs (such as lightmapped materials created during baking) use a
        /// content-based hash instead. At runtime, returns an invalid identifier since GUID information
        /// is not available.
        /// </remarks>
        internal static AssetIdentifier GetAssetIdentifier<T>(UnityObjectRef<T> assetRef) where T : Object
        {
#if UNITY_EDITOR
            var instanceId = assetRef.Id.instanceId;

            if (s_AssetIdentifierCache.TryGetValue(instanceId, out var cached))
                return cached;

            var obj = assetRef.Value;
            var identifier = new AssetIdentifier { IsValid = false };

            if (obj != null)
            {
                if (UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId))
                {
                    identifier = new AssetIdentifier
                    {
                        Guid = new Hash128(guid),
                        LocalFileId = localId,
                        IsValid = true
                    };
                }
                else if (obj is Material runtimeMaterial)
                {
                    identifier = GetRuntimeMaterialIdentifier(runtimeMaterial);
                }
            }

            s_AssetIdentifierCache[instanceId] = identifier;
            return identifier;
#else
            return new AssetIdentifier { IsValid = false };
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Generates a stable identifier for runtime-created materials based on their content.
        /// </summary>
        /// <remarks>
        /// Creates a content-based hash for runtime materials without GUIDs (e.g., lightmapped materials from
        /// CreateLightMappedMaterial). Hashes shader GUID, texture content, and keywords for stable cross-session sorting.
        ///
        /// Important: Only supports materials with baked asset textures. Runtime-created textures (new Texture2D())
        /// have zero imageContentsHash and will cause hash collisions.
        ///
        /// Limitation: Does not hash float/color/vector properties. Lightmapped materials copy these from base
        /// materials which already have GUIDs.
        /// </remarks>
        static AssetIdentifier GetRuntimeMaterialIdentifier(Material material)
        {
            var hash = new xxHash3.StreamingState(false);

            if (material.shader != null)
            {
                if (UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material.shader, out string shaderGuid, out long shaderLocalId))
                {
                    hash.Update(new Hash128(shaderGuid));
                    hash.Update(shaderLocalId);
                }
            }

            var texturePropertyNames = material.GetTexturePropertyNames();
            System.Array.Sort(texturePropertyNames, System.StringComparer.Ordinal);
            foreach (var name in texturePropertyNames)
            {
                var texture = material.GetTexture(name);
                hash.Update(TypeHash.FNV1A64(name));
                if (texture != null)
                    hash.Update(texture.imageContentsHash);
            }

            var keywords = material.shaderKeywords;
            System.Array.Sort(keywords, System.StringComparer.Ordinal);
            foreach (var keyword in keywords)
                hash.Update(TypeHash.FNV1A64(keyword));

            return new AssetIdentifier
            {
                Guid = new Hash128(hash.DigestHash128()),
                LocalFileId = 0,
                IsValid = true
            };
        }
#endif

        /// <summary>
        /// Compares two UnityObjectRef instances using stable GUID-based identifiers.
        /// </summary>
        /// <remarks>
        /// Uses GUID and LocalFileID for deterministic cross-session ordering. Runtime materials without
        /// GUIDs use content-based hashing. Invalid identifiers sort after valid ones. At runtime, falls
        /// back to EntityId comparison since GUID information is not available.
        /// </remarks>
        internal static int CompareUnityObjectRef<T>(UnityObjectRef<T> a, UnityObjectRef<T> b) where T : Object
        {
#if UNITY_EDITOR
            var identifierA = GetAssetIdentifier(a);
            var identifierB = GetAssetIdentifier(b);
            return identifierA.CompareTo(identifierB);
#else
            return a.Id.instanceId.CompareTo(b.Id.instanceId);
#endif
        }
    }
}
