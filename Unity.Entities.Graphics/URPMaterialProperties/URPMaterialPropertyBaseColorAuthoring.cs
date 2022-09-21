#if URP_10_0_0_OR_NEWER
using Unity.Entities;
using Unity.Mathematics;

namespace Unity.Rendering
{
    [MaterialProperty("_BaseColor")]
    public struct URPMaterialPropertyBaseColor : IComponentData
    {
        public float4 Value;
    }

    [UnityEngine.DisallowMultipleComponent]
    public class URPMaterialPropertyBaseColorAuthoring : UnityEngine.MonoBehaviour
    {
        [Unity.Entities.RegisterBinding(typeof(URPMaterialPropertyBaseColor), "Value.x", true)]
        [Unity.Entities.RegisterBinding(typeof(URPMaterialPropertyBaseColor), "Value.y", true)]
        [Unity.Entities.RegisterBinding(typeof(URPMaterialPropertyBaseColor), "Value.z", true)]
        [Unity.Entities.RegisterBinding(typeof(URPMaterialPropertyBaseColor), "Value.w", true)]
        public Unity.Mathematics.float4 Value;

        class URPMaterialPropertyBaseColorBaker : Unity.Entities.Baker<URPMaterialPropertyBaseColorAuthoring>
        {
            public override void Bake(URPMaterialPropertyBaseColorAuthoring authoring)
            {
                Unity.Rendering.URPMaterialPropertyBaseColor component = default(Unity.Rendering.URPMaterialPropertyBaseColor);
                component.Value = authoring.Value;
                AddComponent(component);
            }
        }
    }
}
#endif
