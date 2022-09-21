#if URP_10_0_0_OR_NEWER
using Unity.Entities;
using Unity.Mathematics;

namespace Unity.Rendering
{
    [MaterialProperty("_EmissionColor")]
    public struct URPMaterialPropertyEmissionColor : IComponentData
    {
        public float4 Value;
    }

    [UnityEngine.DisallowMultipleComponent]
    public class URPMaterialPropertyEmissionColorAuthoring : UnityEngine.MonoBehaviour
    {
        [Unity.Entities.RegisterBinding(typeof(URPMaterialPropertyEmissionColor), "Value.x", true)]
        [Unity.Entities.RegisterBinding(typeof(URPMaterialPropertyEmissionColor), "Value.y", true)]
        [Unity.Entities.RegisterBinding(typeof(URPMaterialPropertyEmissionColor), "Value.z", true)]
        [Unity.Entities.RegisterBinding(typeof(URPMaterialPropertyEmissionColor), "Value.w", true)]
        public Unity.Mathematics.float4 Value;

        class URPMaterialPropertyEmissionColorBaker : Unity.Entities.Baker<URPMaterialPropertyEmissionColorAuthoring>
        {
            public override void Bake(URPMaterialPropertyEmissionColorAuthoring authoring)
            {
                Unity.Rendering.URPMaterialPropertyEmissionColor component = default(Unity.Rendering.URPMaterialPropertyEmissionColor);
                component.Value = authoring.Value;
                AddComponent(component);
            }
        }
    }
}
#endif
