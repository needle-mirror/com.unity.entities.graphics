using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.Rendering
{
    public struct OverrideLightProbeAnchorComponent : IComponentData
    {
        public Entity entity;
    }

    public class OverrideLightProbeAnchorBaker : Baker<MeshRenderer>
    {
        public override void Bake(MeshRenderer authoring)
        {
            if (authoring.lightProbeUsage != LightProbeUsage.BlendProbes || authoring.probeAnchor == null)
                return;
            var e = GetEntity(TransformUsageFlags.None);
            AddComponent(e, new OverrideLightProbeAnchorComponent
            {
                entity = GetEntity(authoring.probeAnchor, TransformUsageFlags.Dynamic)
            });

        }
    }
}
