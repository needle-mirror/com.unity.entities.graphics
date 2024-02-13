using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Unity.Rendering
{
    /// <summary>
    /// Represents a container for light maps.
    /// </summary>
    public struct LightMaps : ISharedComponentData, IEquatable<LightMaps>
    {
        /// <summary>
        /// An array of color maps.
        /// </summary>
        public UnityObjectRef<Texture2DArray> colorsRef;

        /// <summary>
        /// An array of color maps.
        /// </summary>
        public Texture2DArray colors
        {
            get => colorsRef;
            set => colorsRef = value;
        }

        /// <summary>
        /// An array of directional maps.
        /// </summary>
        public UnityObjectRef<Texture2DArray> directionsRef;

        /// <summary>
        /// An array of directional maps.
        /// </summary>
        public Texture2DArray directions
        {
            get => directionsRef;
            set => directionsRef = value;
        }

        /// <summary>
        /// An array of Shadow masks.
        /// </summary>
        public UnityObjectRef<Texture2DArray> shadowMasksRef;

        /// <summary>
        /// An array of Shadow masks.
        /// </summary>
        public Texture2DArray shadowMasks
        {
            get => shadowMasksRef;
            set => shadowMasksRef = value;
        }

        /// <summary>
        /// Indicates whether the container stores any directional maps.
        /// </summary>
        public bool hasDirections => directionsRef != null && directionsRef.Value != null && directionsRef.Value.depth > 0;

        /// <summary>
        /// Indicates whether the container stores any shadow masks.
        /// </summary>
        public bool hasShadowMask => shadowMasksRef != null && shadowMasksRef.Value != null && shadowMasksRef.Value.depth > 0;

        /// <summary>
        /// Indicates whether the container stores any color maps.
        /// </summary>
        public bool isValid => colorsRef != null;

        /// <inheritdoc/>
        public bool Equals(LightMaps other)
        {
            return
                colorsRef == other.colorsRef &&
                directionsRef == other.directionsRef &&
                shadowMasksRef == other.shadowMasksRef;
        }

        /// <summary>
        /// Calculates the hash code for this object.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            int hash = 0;
            if (!ReferenceEquals(colorsRef, null)) hash ^= colorsRef.GetHashCode();
            if (!ReferenceEquals(directionsRef, null)) hash ^= directionsRef.GetHashCode();
            if (!ReferenceEquals(shadowMasksRef, null)) hash ^= shadowMasksRef.GetHashCode();
            return hash;
        }

        /// <summary>
        /// Converts a provided list of Texture2Ds into a Texture2DArray.
        /// </summary>
        /// <param name="source">A list of Texture2Ds.</param>
        /// <returns>Returns a Texture2DArray that contains the list of Texture2Ds.</returns>
        private static Texture2DArray CopyToTextureArray(List<Texture2D> source)
        {
            if (source == null || !source.Any())
                return null;

            var data = source.First();
            if (data == null)
                return null;

            bool isSRGB = GraphicsFormatUtility.IsSRGBFormat(data.graphicsFormat);
            var result = new Texture2DArray(data.width, data.height, source.Count, source[0].format, true, !isSRGB);
            result.filterMode = FilterMode.Trilinear;
            result.wrapMode = TextureWrapMode.Clamp;
            result.anisoLevel = 3;

            for (var sliceIndex = 0; sliceIndex < source.Count; sliceIndex++)
            {
                var lightMap = source[sliceIndex];
                Graphics.CopyTexture(lightMap, 0, result, sliceIndex);
            }

            return result;
        }

        /// <summary>
        /// Constructs a LightMaps instance from a list of textures for colors, direction lights, and shadow masks.
        /// </summary>
        /// <param name="inColors">The list of Texture2D for colors.</param>
        /// <param name="inDirections">The list of Texture2D for direction lights.</param>
        /// <param name="inShadowMasks">The list of Texture2D for shadow masks.</param>
        /// <returns>Returns a new LightMaps object.</returns>
        public static LightMaps ConstructLightMaps(List<Texture2D> inColors, List<Texture2D> inDirections, List<Texture2D> inShadowMasks)
        {
            var result = new LightMaps
            {
                colorsRef = CopyToTextureArray(inColors),
                directionsRef = CopyToTextureArray(inDirections),
                shadowMasksRef = CopyToTextureArray(inShadowMasks)
            };
            return result;
        }
    }
}
