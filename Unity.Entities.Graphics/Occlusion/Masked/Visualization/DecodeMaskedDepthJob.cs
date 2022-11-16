#if ENABLE_UNITY_OCCLUSION && (HDRP_10_0_0_OR_NEWER || URP_10_0_0_OR_NEWER)

using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.Rendering.Occlusion.Masked.Visualization
{
    /* Unpack the CPU-rasterized masked depth buffer into a human-readable depth buffer. */
    [BurstCompile]
    unsafe struct DecodeMaskedDepthJob : IJobParallelFor
    {
        [ReadOnly] public int NumPixelsX;
        [ReadOnly] public int NumPixelsY;
        [ReadOnly] public int NumTilesX;
        [ReadOnly, NativeDisableUnsafePtrRestriction] public Tile* Tiles;

        [WriteOnly] public NativeArray<float> DecodedZBuffer;

        public void Execute(int i)
        {
            int x = i % NumPixelsX;
            int y = NumPixelsY - i / NumPixelsX;

            // Compute 32xN tile index (SIMD value offset)
            int tx = x / BufferGroup.TileWidth;
            int ty = y / BufferGroup.TileHeight;
            int tileIdx = ty * NumTilesX + tx;

            // Compute 8x4 subtile index (SIMD lane offset)
            int stx = (x % BufferGroup.TileWidth) / BufferGroup.SubTileWidth;
            int sty = (y % BufferGroup.TileHeight) / BufferGroup.SubTileHeight;
            int subTileIdx = sty * 4 + stx;

            // Compute pixel index in subtile (bit index in 32-bit word)
            int px = (x % BufferGroup.SubTileWidth);
            int py = (y % BufferGroup.SubTileHeight);
            int bitIdx = py * 8 + px;

            int pixelLayer = (IntrinsicUtils.getIntLane(Tiles[tileIdx].mask, (uint) subTileIdx) >>
                              bitIdx) & 1;
            float pixelDepth = IntrinsicUtils.getFloatLane(
                pixelLayer == 0 ? Tiles[tileIdx].zMin0 : Tiles[tileIdx].zMin1,
                (uint) subTileIdx
            );

            DecodedZBuffer[i] = pixelDepth;
        }
    }
}

#endif // ENABLE_UNITY_OCCLUSION && (HDRP_10_0_0_OR_NEWER || URP_10_0_0_OR_NEWER)
