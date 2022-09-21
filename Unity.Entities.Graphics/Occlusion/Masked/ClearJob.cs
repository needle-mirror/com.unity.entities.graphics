#if ENABLE_UNITY_OCCLUSION && (HDRP_10_0_0_OR_NEWER || URP_10_0_0_OR_NEWER)

using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.Rendering.Occlusion.Masked
{
    [BurstCompile]
    public unsafe struct ClearJob: IJobFor
    {
        [ReadOnly, NativeDisableUnsafePtrRestriction] public Tile* Tiles;
        public void Execute(int i)
        {
            Tiles[i].zMin0 = X86.Sse.set1_ps(-1f);
            Tiles[i].zMin1 = X86.Sse2.setzero_si128();
            Tiles[i].mask = X86.Sse2.setzero_si128();
        }
    }
}

#endif // ENABLE_UNITY_OCCLUSION && (HDRP_10_0_0_OR_NEWER || URP_10_0_0_OR_NEWER)
