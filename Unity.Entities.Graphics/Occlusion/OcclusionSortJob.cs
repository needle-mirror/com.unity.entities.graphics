#if ENABLE_UNITY_OCCLUSION && (HDRP_10_0_0_OR_NEWER || URP_10_0_0_OR_NEWER)

using System;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Rendering;
using Unity.Transforms;
using System.Collections.Generic;
using Unity.Rendering.Occlusion.Masked.Dots;


namespace Unity.Rendering.Occlusion
{
    [BurstCompile]
    unsafe struct OcclusionSortMeshesJob : IJob
    {
        public NativeArray<OcclusionMesh> Meshes;


        struct Compare : IComparer<OcclusionMesh>
        {
            int IComparer<OcclusionMesh>.Compare(OcclusionMesh x, OcclusionMesh y)
            {
                return x.screenMin.z.CompareTo(y.screenMin.z);
            }
        }

        public void Execute()
        {
            if (Meshes.Length == 0)
                return;

            // TODO:  might want to do a proper parallel sort instead
            Meshes.Sort(new Compare());
        }
    }
}

#endif
