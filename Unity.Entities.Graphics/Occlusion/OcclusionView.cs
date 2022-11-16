using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace Unity.Rendering.Occlusion
{
    public struct OcclusionViewSettings
    {
        public bool enabled;
        public uint width;
        public uint height;
    }

    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    public class OcclusionView : MonoBehaviour
    {
        public bool OcclusionEnabled = true;

        public uint OcclusionBufferWidth = DefaultBufferSize;

        public uint OcclusionBufferHeight = DefaultBufferSize;

        public static readonly uint DefaultBufferSize = 512;

#if ENABLE_UNITY_OCCLUSION && (HDRP_10_0_0_OR_NEWER || URP_10_0_0_OR_NEWER)

        void Update()
        {
            if (World.DefaultGameObjectInjectionWorld == null || !OcclusionEnabled)
                return;

            var entitiesGraphicsSystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<EntitiesGraphicsSystem>();

            if (entitiesGraphicsSystem != null)
            {
                var occlusion = entitiesGraphicsSystem.OcclusionCulling;
                if (occlusion != null)
                {
                    occlusion.UpdateSettings(this);
                }
            }
        }

        public void OnValidate()
        {
            Update();
#if UNITY_EDITOR
            OcclusionBrowseWindow.Refresh();
#endif
        }

        private void OnDestroy()
        {
            if (World.DefaultGameObjectInjectionWorld == null)
                return;

            var entitiesGraphicsSystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<EntitiesGraphicsSystem>();

            if (entitiesGraphicsSystem != null)
            {
                var occlusion = entitiesGraphicsSystem.OcclusionCulling;
                if (occlusion != null)
                {
                    occlusion.UpdateSettings(this);
                }
            }
        }
#endif
    }
}
