#if ENABLE_UNITY_OCCLUSION && (HDRP_10_0_0_OR_NEWER || URP_10_0_0_OR_NEWER)

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.Rendering;

namespace Unity.Rendering.Occlusion.Masked.Dots
{
    public struct OcclusionTest : IComponentData
    {
        public OcclusionTest(bool enabled)
        {
            this.enabled = enabled;
            screenMin = float.MaxValue;
            screenMax = -float.MaxValue;
        }

        // this flag is for toggling occlusion testing without having to add a component at runtime.
        public bool enabled;
        public float4 screenMin, screenMax;
    }

    public struct ChunkOcclusionTest : IComponentData
    {
        public float4 screenMin, screenMax;
    }
}
#endif
