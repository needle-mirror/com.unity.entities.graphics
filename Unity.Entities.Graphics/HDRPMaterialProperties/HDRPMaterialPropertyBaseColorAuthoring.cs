#if HDRP_10_0_0_OR_NEWER
using Unity.Entities;
using Unity.Mathematics;

namespace Unity.Rendering
{
    [MaterialProperty("_BaseColor"            )]
    public struct HDRPMaterialPropertyBaseColor : IComponentData { public float4 Value; }

    [UnityEngine.DisallowMultipleComponent]
    public class HDRPMaterialPropertyBaseColorAuthoring : UnityEngine.MonoBehaviour
    {
        [RegisterBinding(typeof(HDRPMaterialPropertyBaseColor), "Value.x", true)]
        [RegisterBinding(typeof(HDRPMaterialPropertyBaseColor), "Value.y", true)]
        [RegisterBinding(typeof(HDRPMaterialPropertyBaseColor), "Value.z", true)]
        [RegisterBinding(typeof(HDRPMaterialPropertyBaseColor), "Value.w", true)]
        public float4 Value;

        class HDRPMaterialPropertyBaseColorBaker : Baker<HDRPMaterialPropertyBaseColorAuthoring>
        {
            public override void Bake(HDRPMaterialPropertyBaseColorAuthoring authoring)
            {
                var component = default(HDRPMaterialPropertyBaseColor);
                component.Value = authoring.Value;
                AddComponent(component);
            }
        }
    }
}
#endif
