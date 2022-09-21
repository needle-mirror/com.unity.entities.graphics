#if ENABLE_UNITY_OCCLUSION && (HDRP_10_0_0_OR_NEWER || URP_10_0_0_OR_NEWER)

using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.Rendering.Occlusion.Masked
{
    [BurstCompile]
    public unsafe struct MergeJob : IJobFor
    {
        [ReadOnly] public int NumTilesPerBuffer;
        [ReadOnly] public int NumTilesPerJob;
        [ReadOnly] public int NumBuffers;

        [NativeDisableUnsafePtrRestriction] public Tile* TilesBasePtr;

        public void Execute(int i)
        {
            // The destination buffer is the zero-th buffer
            Tile* dstTiles = TilesBasePtr;
            // The source buffer is the (+ 1)th buffer
            for (int j = 1; j < NumBuffers; j++)
            {
                Tile* srcTiles = &TilesBasePtr[j * NumTilesPerBuffer];
                MergeTile(srcTiles, dstTiles, i * NumTilesPerJob, NumTilesPerJob);
            }

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        v128 _mmw_not_epi32(v128 a) { return X86.Sse2.xor_si128((a), X86.Sse2.set1_epi32(~0)); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int _mmw_testz_epi32(v128 a, v128 b) { return X86.Sse4_1.testz_si128(a, b); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        v128 _mmw_or_epi32(v128 a, v128 b) { return X86.Sse2.or_si128(a, b); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        v128 _mmw_blendv_ps(v128 a, v128 b, v128 c) { return X86.Sse4_1.blendv_ps(a, b, c); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        v128 _mmw_abs_ps(v128 a) { return X86.Sse.and_ps((a), X86.Sse2.set1_epi32(0x7FFFFFFF)); }

        void MergeTile(Tile* srcTiles, Tile* dstTiles, int startTileIdx, int numTiles)
        {
            for (int i = startTileIdx; i < startTileIdx + numTiles; i++)
            {
                v128* zMinB_0 = &(srcTiles[i].zMin0);
                v128* zMinB_1 = &(srcTiles[i].zMin1);

                // Clear z0 to beyond infinity to ensure we never merge with clear data
                v128 sign1 = X86.Sse2.srai_epi32(dstTiles[i].zMin0, 31);
                // Only merge tiles that have data in zMinB[0], use the sign bit to determine if they are still in a clear state
                sign1 = X86.Sse2.cmpeq_epi32(sign1, X86.Sse2.setzero_si128());

                // Set 32bit value to -1 if any pixels are set incide the coverage mask for a subtile
                v128 liveTile1 = X86.Sse2.cmpeq_epi32(dstTiles[i].mask, X86.Sse2.setzero_si128());
                // invert to have bits set for clear subtiles
                v128 t1inv = _mmw_not_epi32(liveTile1);
                // VPTEST sets the ZF flag if all the resulting bits are 0 (ie if all tiles are clear)
                if (_mmw_testz_epi32(sign1, sign1) != 0 && _mmw_testz_epi32(t1inv, t1inv) != 0)
                {
                    dstTiles[i].mask = srcTiles[i].mask;
                    dstTiles[i].zMin0 = *zMinB_0;
                    dstTiles[i].zMin1 = *zMinB_1;
                }
                else
                {
                    // Clear z0 to beyond infinity to ensure we never merge with clear data
                    v128 sign0 = X86.Sse2.srai_epi32(*zMinB_0, 31);
                    sign0 = X86.Sse2.cmpeq_epi32(sign0, X86.Sse2.setzero_si128());
                    // Only merge tiles that have data in zMinB[0], use the sign bit to determine if they are still in a clear state
                    if (_mmw_testz_epi32(sign0, sign0) == 0)
                    {
                        // build a mask for Zmin[0], full if the layer has been completed, or partial if tile is still partly filled.
                        // cant just use the completement of the mask, as tiles might not get updated by merge
                        v128 sign_1 = X86.Sse2.srai_epi32(*zMinB_1, 31);
                        v128 LayerMask0 = _mmw_not_epi32(sign_1);
                        v128 LayerMask1 = _mmw_not_epi32(srcTiles[i].mask);
                        v128 rastMask = _mmw_or_epi32(LayerMask0, LayerMask1);

                        UpdateTileAccurate(dstTiles, i, rastMask, *zMinB_0);
                    }

                    // Set 32bit value to -1 if any pixels are set incide the coverage mask for a subtile
                    v128 LiveTile = X86.Sse2.cmpeq_epi32(srcTiles[i].mask, X86.Sse2.setzero_si128());
                    // invert to have bits set for clear subtiles
                    v128 t0inv = _mmw_not_epi32(LiveTile);
                    // VPTEST sets the ZF flag if all the resulting bits are 0 (ie if all tiles are clear)
                    if (_mmw_testz_epi32(t0inv, t0inv) == 0)
                    {
                        UpdateTileAccurate(dstTiles, i, srcTiles[i].mask, *zMinB_1);
                    }
                }
            }
        }

        void UpdateTileAccurate(Tile* dstTiles, int tileIdx, v128 coverage, v128 zTriv)
        {
            v128 zMin0 = dstTiles[tileIdx].zMin0;
            v128 zMin1 = dstTiles[tileIdx].zMin1;
            v128 mask = dstTiles[tileIdx].mask;

            // Swizzle coverage mask to 8x4 subtiles
            v128 rastMask = coverage;

            // Perform individual depth tests with layer 0 & 1 and mask out all failing pixels
            v128 sdist0 = X86.Sse.sub_ps(zMin0, zTriv);
            v128 sdist1 = X86.Sse.sub_ps(zMin1, zTriv);
            v128 sign0 = X86.Sse2.srai_epi32(sdist0, 31);
            v128 sign1 = X86.Sse2.srai_epi32(sdist1, 31);
            v128 triMask = X86.Sse2.and_si128(rastMask, X86.Sse2.or_si128(X86.Sse2.andnot_si128(mask, sign0), X86.Sse2.and_si128(mask, sign1)));

            // Early out if no pixels survived the depth test (this test is more accurate than
            // the early culling test in TraverseScanline())
            v128 t0 = X86.Sse2.cmpeq_epi32(triMask, X86.Sse2.setzero_si128());
            v128 t0inv = _mmw_not_epi32(t0);

            if (_mmw_testz_epi32(t0inv, t0inv) != 0)
            {
                return;
            }

#if MOC_ENABLE_STATS
            STATS_ADD(ref mStats.mOccluders.mNumTilesUpdated, 1);
#endif

            v128 zTri = _mmw_blendv_ps(zTriv, zMin0, t0);

            // Test if incoming triangle completely overwrites layer 0 or 1
            v128 layerMask0 = X86.Sse2.andnot_si128(triMask, _mmw_not_epi32(mask));
            v128 layerMask1 = X86.Sse2.andnot_si128(triMask, mask);
            v128 lm0 = X86.Sse2.cmpeq_epi32(layerMask0, X86.Sse2.setzero_si128());
            v128 lm1 = X86.Sse2.cmpeq_epi32(layerMask1, X86.Sse2.setzero_si128());
            v128 z0 = _mmw_blendv_ps(zMin0, zTri, lm0);
            v128 z1 = _mmw_blendv_ps(zMin1, zTri, lm1);

            // Compute distances used for merging heuristic
            v128 d0 = _mmw_abs_ps(sdist0);
            v128 d1 = _mmw_abs_ps(sdist1);
            v128 d2 = _mmw_abs_ps(X86.Sse.sub_ps(z0, z1));

            // Find minimum distance
            v128 c01 = X86.Sse.sub_ps(d0, d1);
            v128 c02 = X86.Sse.sub_ps(d0, d2);
            v128 c12 = X86.Sse.sub_ps(d1, d2);
            // Two tests indicating which layer the incoming triangle will merge with or
            // overwrite. d0min indicates that the triangle will overwrite layer 0, and
            // d1min flags that the triangle will overwrite layer 1.
            v128 d0min = X86.Sse2.or_si128(X86.Sse2.and_si128(c01, c02), X86.Sse2.or_si128(lm0, t0));
            v128 d1min = X86.Sse2.andnot_si128(d0min, X86.Sse2.or_si128(c12, lm1));

            ///////////////////////////////////////////////////////////////////////////////
            // Update depth buffer entry. NOTE: we always merge into layer 0, so if the
            // triangle should be merged with layer 1, we first swap layer 0 & 1 and then
            // merge into layer 0.
            ///////////////////////////////////////////////////////////////////////////////

            // Update mask based on which layer the triangle overwrites or was merged into
            v128 inner = _mmw_blendv_ps(triMask, layerMask1, d0min);

            // Update the zMin[0] value. There are four outcomes: overwrite with layer 1,
            // merge with layer 1, merge with zTri or overwrite with layer 1 and then merge
            // with zTri.
            v128 e0 = _mmw_blendv_ps(z0, z1, d1min);
            v128 e1 = _mmw_blendv_ps(z1, zTri, X86.Sse2.or_si128(d1min, d0min));

            // Update the zMin[1] value. There are three outcomes: keep current value,
            // overwrite with zTri, or overwrite with z1
            v128 z1t = _mmw_blendv_ps(zTri, z1, d0min);

            dstTiles[tileIdx].zMin0 = X86.Sse.min_ps(e0, e1);
            dstTiles[tileIdx].zMin1 = _mmw_blendv_ps(z1t, z0, d1min);
            dstTiles[tileIdx].mask = _mmw_blendv_ps(inner, layerMask0, d1min);
        }
    }
}

#endif // ENABLE_UNITY_OCCLUSION && (HDRP_10_0_0_OR_NEWER || URP_10_0_0_OR_NEWER)
