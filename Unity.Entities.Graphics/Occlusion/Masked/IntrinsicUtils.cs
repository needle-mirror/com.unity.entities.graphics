#if ENABLE_UNITY_OCCLUSION && (HDRP_10_0_0_OR_NEWER || URP_10_0_0_OR_NEWER)

using System.Runtime.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;


namespace Unity.Rendering.Occlusion.Masked
{
    public static class IntrinsicUtils
    {

        // naive approach, works with C# reference implementation

        // read access
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int getIntLane(v128 vector, uint laneIdx)
        {
            //Debug.Assert(laneIdx >= 0 && laneIdx < 4);

            // eat the modulo cost to not let it overflow
            switch (laneIdx % 4)
            {
                default:    // DS: incorrect, but works with modulo and silences compiler (CS0161)
                case 0: { return vector.SInt0; }
                case 1: { return vector.SInt1; }
                case 2: { return vector.SInt2; }
                case 3: { return vector.SInt3; }
            }
        }

        // used for "write" access (returns copy, requires assignment afterwards)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 getCopyWithIntLane(v128 vector, uint laneIdx, int laneVal)
        {
            //Debug.Assert(laneIdx >= 0 && laneIdx < 4);

            // eat the modulo cost to not let it overflow
            switch (laneIdx % 4)
            {
                default:    // DS: incorrect fallthrough, but works with modulo and silences compiler (CS0161)
                case 0: { vector.SInt0 = laneVal; break; }
                case 1: { vector.SInt1 = laneVal; break; }
                case 2: { vector.SInt2 = laneVal; break; }
                case 3: { vector.SInt3 = laneVal; break; }
            }

            return vector;
        }

        // read access
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float getFloatLane(v128 vector, uint laneIdx)
        {
            //Debug.Assert(laneIdx >= 0 && laneIdx < 4);

            // eat the modulo cost to not let it overflow
            switch (laneIdx % 4)
            {
                default:    // DS: incorrect fallthrough, but works with modulo and silences compiler (CS0161)
                case 0: { return vector.Float0; }
                case 1: { return vector.Float1; }
                case 2: { return vector.Float2; }
                case 3: { return vector.Float3; }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 _mmw_fmadd_ps(v128 a, v128 b, v128 c) { return X86.Sse.add_ps(X86.Sse.mul_ps(a, b), c); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 _mmw_fmsub_ps(v128 a, v128 b, v128 c) { return X86.Sse.sub_ps(X86.Sse.mul_ps(a, b), c); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 _mmw_neg_ps(v128 a) { return X86.Sse.xor_ps((a), X86.Sse.set1_ps(-0f)); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 _mmw_neg_epi32(v128 a) { return X86.Sse2.sub_epi32(X86.Sse2.set1_epi32(0), (a)); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 _mmw_not_epi32(v128 a) { return X86.Sse2.xor_si128((a), X86.Sse2.set1_epi32(~0)); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 _mmw_abs_ps(v128 a) { return X86.Sse.and_ps((a), X86.Sse2.set1_epi32(0x7FFFFFFF)); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 _mmw_or_epi32(v128 a, v128 b) { return X86.Sse2.or_si128(a, b); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 _mmw_andnot_epi32(v128 a, v128 b) { return X86.Sse2.andnot_si128(a, b); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 _mmw_mullo_epi32(v128 a, v128 b) { return X86.Sse4_1.mullo_epi32(a, b); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 _mmw_min_epi32(v128 a, v128 b) { return X86.Sse4_1.min_epi32(a, b); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 _mmw_max_epi32(v128 a, v128 b) { return X86.Sse4_1.max_epi32(a, b); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 _mmw_abs_epi32(v128 a) { return X86.Ssse3.abs_epi32(a); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 _mmw_blendv_ps(v128 a, v128 b, v128 c) { return X86.Sse4_1.blendv_ps(a, b, c); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int _mmw_testz_epi32(v128 a, v128 b) { return X86.Sse4_1.testz_si128(a, b); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 _mmx_dp4_ps(v128 a, v128 b) { return X86.Sse4_1.dp_ps(a, b, 0xFF); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 _mmw_floor_ps(v128 a) { return X86.Sse4_1.round_ps(a, (int)X86.RoundingMode.FROUND_FLOOR_NOEXC); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 _mmw_ceil_ps(v128 a) { return X86.Sse4_1.round_ps(a, (int)X86.RoundingMode.FROUND_CEIL_NOEXC); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 _mmw_transpose_epi8(v128 a)
        {
            v128 shuff = X86.Sse2.setr_epi8(0x0, 0x4, 0x8, 0xC, 0x1, 0x5, 0x9, 0xD, 0x2, 0x6, 0xA, 0xE, 0x3, 0x7, 0xB, 0xF);
            return X86.Ssse3.shuffle_epi8(a, shuff);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static v128 _mmw_sllv_ones(v128 ishift)
        {
            v128 shift = X86.Sse4_1.min_epi32(ishift, X86.Sse2.set1_epi32(32));

            // Uses lookup tables and _mm_shuffle_epi8 to perform _mm_sllv_epi32(~0, shift)
            v128 byteShiftLUT;
            unchecked
            {
                byteShiftLUT = X86.Sse2.setr_epi8((sbyte)0xFF, (sbyte)0xFE, (sbyte)0xFC, (sbyte)0xF8, (sbyte)0xF0, (sbyte)0xE0, (sbyte)0xC0, (sbyte)0x80, 0, 0, 0, 0, 0, 0, 0, 0);
            }
            v128 byteShiftOffset = X86.Sse2.setr_epi8(0, 8, 16, 24, 0, 8, 16, 24, 0, 8, 16, 24, 0, 8, 16, 24);
            v128 byteShiftShuffle = X86.Sse2.setr_epi8(0x0, 0x0, 0x0, 0x0, 0x4, 0x4, 0x4, 0x4, 0x8, 0x8, 0x8, 0x8, 0xC, 0xC, 0xC, 0xC);

            v128 byteShift = X86.Ssse3.shuffle_epi8(shift, byteShiftShuffle);

            // DS: TODO: change once we get Burst fix for X86.Sse2.set1_epi8()
            const sbyte val = 8;
            byteShift = X86.Sse4_1.min_epi8(X86.Sse2.subs_epu8(byteShift, byteShiftOffset), new v128(val) /*X86.Sse2.set1_epi8(8)*/);

            v128 retMask = X86.Ssse3.shuffle_epi8(byteShiftLUT, byteShift);

            return retMask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong find_clear_lsb(ref uint mask)
        {
            ulong idx = (ulong)math.tzcnt(mask);
            mask &= mask - 1;
            return idx;
        }
    }
}
#endif
