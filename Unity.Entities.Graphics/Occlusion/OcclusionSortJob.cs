#if ENABLE_UNITY_OCCLUSION && (HDRP_10_0_0_OR_NEWER || URP_10_0_0_OR_NEWER)

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using System.Collections.Generic;
using Unity.Rendering.Occlusion.Masked;


namespace Unity.Rendering.Occlusion
{
    [BurstCompile]
    struct OcclusionSortMeshesJob : IJob
    {
        public NativeArray<ClippedOccluder> ClippedOccluders;


        struct Compare : IComparer<ClippedOccluder>
        {
            int IComparer<ClippedOccluder>.Compare(ClippedOccluder x, ClippedOccluder y)
            {
                return x.screenMin.z.CompareTo(y.screenMin.z);
            }
        }

        public void Execute()
        {
            if (ClippedOccluders.Length == 0)
                return;

            // TODO:  might want to do a proper parallel sort instead
            ClippedOccluders.Sort(new Compare());
        }
    }
}

#endif
