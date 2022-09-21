using Unity.Entities;
using UnityEngine;

namespace Unity.Rendering
{
    /// <summary>
    /// An unmanaged component that stores light baking data.
    /// </summary>
    /// <remarks>
    /// Entities Graphics uses this component to store light baking data at conversion time to restore
    /// at run time. This is because this doesn't happen automatically with hybrid entities.
    /// </remarks>
    public struct LightBakingOutputData : IComponentData
    {
        /// <summary>
        /// The output of light baking on the entity.
        /// </summary>
        public LightBakingOutput Value;
    }

    /// <summary>
    /// A tag component that HybridLightBakingDataSystem uses to assign a LightBakingOutput to the bakingOutput of the Light component.
    /// </summary>
    public struct LightBakingOutputDataRestoredTag : IComponentData
    {}

    /// <summary>
    /// Represents a light baking system that assigns a LightBakingOutput to the bakingOutput of the Light component.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class HybridLightBakingDataSystem : SystemBase
    {
        /// <inheritdoc/>
        protected override void OnUpdate()
        {
            Entities
                .WithStructuralChanges()
                .WithNone<LightBakingOutputDataRestoredTag>()
                .ForEach((Entity e, in LightBakingOutputData bakingOutput) =>
                {
                    var light = EntityManager.GetComponentObject<Light>(e);

                    if (light != null)
                        light.bakingOutput = bakingOutput.Value;

                    EntityManager.AddComponent<LightBakingOutputDataRestoredTag>(e);
                }).Run();
        }
    }
}
