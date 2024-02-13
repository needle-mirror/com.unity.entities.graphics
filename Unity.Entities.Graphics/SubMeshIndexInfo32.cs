using System;
using System.Runtime.CompilerServices;
using Unity.Assertions;
using UnityEngine;

namespace Unity.Rendering
{
    internal struct SubMeshIndexInfo32
    {
        // Bit packing layout
        // ====================================
        // 20 bits : Range start index.
        // 7 bits : Range length.
        // 4 bits (unused) : Could be used for LOD in the future?
        // 1 bit : True when using material mesh index range, otherwise false.
        uint m_Value;

        /// <summary>
        /// Specifies drawing a single sub-mesh.
        /// </summary>
        /// <param name="subMeshIndex">  SubMesh index to draw </param>
        /// <remarks>
        /// <see cref="HasMaterialMeshIndexRange"/> is false when using this constructor.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubMeshIndexInfo32(ushort subMeshIndex) => m_Value = subMeshIndex;

        /// <summary>
        /// Specifies drawing a range of sub-meshes.
        /// </summary>
        /// <param name="rangeStartIndex"> SubMesh index to start at (Inclusive) </param>
        /// <param name="rangeLength"> Amount of SubMeshes to draw, e.g. (3,2) means drawing submesh 3 and 4. </param>
        /// <remarks>
        /// <see cref="HasMaterialMeshIndexRange"/> is true when using this constructor.
        /// </remarks>
        public SubMeshIndexInfo32(ushort rangeStartIndex, byte rangeLength)
        {
            Assert.IsTrue(rangeLength < (1 << 7), $"{nameof(rangeLength)} must be 7bits or less");

            var rangeStartIndexU32 = (uint)rangeStartIndex;
            var rangeLengthU32 = (uint)rangeLength;

            var rangeStartIndexMask = rangeStartIndexU32 & 0xfffff; // sets the first 20 bits
            var rangeLengthMask = (rangeLengthU32 & 0x7f) << 20; // sets the 7 bits at offset 20
            var infoMask = 0x80000000; // sets the 1 bit at offset 31

            m_Value = rangeStartIndexMask | rangeLengthMask | infoMask;
        }

        /// <summary>
        /// Retrieve the sub-mesh Index
        /// </summary>
        public ushort SubMesh
        {
            get => ExtractSubMeshIndex();
            set => m_Value = new SubMeshIndexInfo32(value).m_Value;
        }

        /// <summary>
        /// The MaterialMeshIndex range.
        /// start is submesh index to start at (inclusive),
        /// length is amount of submeshes.
        /// e.g. (3,2) means drawing submesh 3 and 4.
        /// </summary>
        /// <remarks>
        /// Only valid if <see cref="HasMaterialMeshIndexRange"/> is true.
        /// </remarks>
        public (ushort start, byte length) MaterialMeshIndexRange =>
        (
            ExtractMaterialMeshIndexRangeStart(),
            ExtractMaterialMeshIndexRangeLength()
        );

        /// <summary>
        /// The MaterialMeshIndex range. (as RangeInt)
        /// start is submesh index to start at (inclusive),
        /// length is amount of submeshes.
        /// e.g. (3,2) means drawing submesh 3 and 4.
        /// </summary>
        /// <remarks>
        /// Only valid if <see cref="HasMaterialMeshIndexRange"/> is true.
        ///
        /// </remarks>
        public RangeInt MaterialMeshIndexRangeAsInt => new()
        {
            start = ExtractMaterialMeshIndexRangeStart(),
            length = ExtractMaterialMeshIndexRangeLength(),
        };

        /// <summary>
        /// True if this SubMeshIndexInfo32 contains a range of submeshes
        /// </summary>
        public bool HasMaterialMeshIndexRange => HasMaterialMeshIndexRangeBit();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        ushort ExtractSubMeshIndex()
        {
            return (ushort)(m_Value & 0xff); // 0xff is 8 bits
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        ushort ExtractMaterialMeshIndexRangeStart()
        {
            Assert.IsTrue(HasMaterialMeshIndexRangeBit(), "MaterialMeshIndexRange is only valid when HasMaterialMeshIndexRange is true");
            return (ushort)(m_Value & 0xfffff); // 0xfffff is 20 bits
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        byte ExtractMaterialMeshIndexRangeLength()
        {
            Assert.IsTrue(HasMaterialMeshIndexRangeBit(), "MaterialMeshIndexRange is only valid when HasMaterialMeshIndexRange is true");
            return (byte)((m_Value >> 20) & 0x7f); // 0x7f is 7 bits, 20 is the offset
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool HasMaterialMeshIndexRangeBit()
        {
            return (m_Value & 0x80000000) != 0; // checks if the 31st bit is set
        }

        /// <summary>
        /// Determines whether two object instances are equal based on their hashes.
        /// </summary>
        /// <param name="other">The object to compare with the current object.</param>
        /// <returns>Returns true if the specified object is equal to the current object. Otherwise, returns false.</returns>
        public bool Equals(SubMeshIndexInfo32 other) => m_Value == other.m_Value;

        /// <summary>
        /// Determines whether two object instances are equal based on their hashes.
        /// </summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns>Returns true if the specified object is equal to the current object. Otherwise, returns false.</returns>
        public override bool Equals(object obj) => obj is SubMeshIndexInfo32 other && Equals(other);

        /// <summary>
        /// Calculates the hash code for this object.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() => (int)m_Value;

        /// <summary>
        /// The equality operator == returns true if its operands are equal, false otherwise.
        /// </summary>
        /// <param name="left">The left instance to compare.</param>
        /// <param name="right">The right instance to compare.</param>
        /// <returns>True if left and right instances are equal and false otherwise.</returns>
        public static bool operator ==(SubMeshIndexInfo32 left, SubMeshIndexInfo32 right) => left.Equals(right);

        /// <summary>
        /// The not equality operator != returns false if its operands are equal, true otherwise.
        /// </summary>
        /// <param name="left">The left instance to compare.</param>
        /// <param name="right">The right instance to compare.</param>
        /// <returns>False if left and right instances are equal and true otherwise.</returns>
        public static bool operator !=(SubMeshIndexInfo32 left, SubMeshIndexInfo32 right) => !left.Equals(right);

        /// <summary>
        /// Debug string representation of the SubMeshIndexInfo32.
        /// If <see cref="HasMaterialMeshIndexRange"/> is true, the string will contain the range.
        /// otherwise it will contain the submesh index.
        /// </summary>
        /// <returns>
        /// A string representation of the SubMeshIndexInfo32.
        /// </returns>
        public override string ToString() => HasMaterialMeshIndexRangeBit()
            ? $"MaterialMeshIndexRange: From: {MaterialMeshIndexRange.start}, To: {MaterialMeshIndexRange.start + MaterialMeshIndexRange.length}"
            : $"SubMesh: {SubMesh}";
    }
}
