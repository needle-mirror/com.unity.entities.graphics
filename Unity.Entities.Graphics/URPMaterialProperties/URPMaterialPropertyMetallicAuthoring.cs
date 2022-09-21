#if URP_10_0_0_OR_NEWER
using Unity.Entities;

namespace Unity.Rendering
{
    [MaterialProperty("_Metallic")]
    public struct URPMaterialPropertyMetallic : IComponentData
    {
        public float Value;
    }

    [UnityEngine.DisallowMultipleComponent]
    public class URPMaterialPropertyMetallicAuthoring : UnityEngine.MonoBehaviour
    {
        [Unity.Entities.RegisterBinding(typeof(URPMaterialPropertyMetallic), "Value")]
        public float Value;

        class URPMaterialPropertyMetallicBaker : Unity.Entities.Baker<URPMaterialPropertyMetallicAuthoring>
        {
            public override void Bake(URPMaterialPropertyMetallicAuthoring authoring)
            {
                Unity.Rendering.URPMaterialPropertyMetallic component = default(Unity.Rendering.URPMaterialPropertyMetallic);
                component.Value = authoring.Value;
                AddComponent(component);
            }
        }
    }
}
#endif
