#if URP_10_0_0_OR_NEWER
using Unity.Entities;
using Unity.Mathematics;

namespace Unity.Rendering
{
    [MaterialProperty("_SpecColor")]
    public struct URPMaterialPropertySpecColor : IComponentData
    {
        public float4 Value;
    }

    [UnityEngine.DisallowMultipleComponent]
    public class URPMaterialPropertySpecColorAuthoring : UnityEngine.MonoBehaviour
    {
        [Unity.Entities.RegisterBinding(typeof(URPMaterialPropertySpecColor), "Value.x", true)]
        [Unity.Entities.RegisterBinding(typeof(URPMaterialPropertySpecColor), "Value.y", true)]
        [Unity.Entities.RegisterBinding(typeof(URPMaterialPropertySpecColor), "Value.z", true)]
        [Unity.Entities.RegisterBinding(typeof(URPMaterialPropertySpecColor), "Value.w", true)]
        public Unity.Mathematics.float4 Value;

        class URPMaterialPropertySpecColorBaker : Unity.Entities.Baker<URPMaterialPropertySpecColorAuthoring>
        {
            public override void Bake(URPMaterialPropertySpecColorAuthoring authoring)
            {
                Unity.Rendering.URPMaterialPropertySpecColor component = default(Unity.Rendering.URPMaterialPropertySpecColor);
                component.Value = authoring.Value;
                AddComponent(component);
            }
        }
    }
}
#endif
