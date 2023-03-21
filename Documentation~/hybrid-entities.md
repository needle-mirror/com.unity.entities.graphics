# Hybrid entities

Hybrid entities is a new DOTS feature. This feature allows you to attach MonoBehaviour components to DOTS entities, without converting them to IComponentData. A conversion system calls AddHybridComponent to attach a managed component to DOTS entity.

The following graphics related hybrid components are supported by Entities Graphics:

- Light + HDAdditionalLightData (HDRP)
- Light + UniversalAdditionalLightData (URP)
- ReflectionProbe + HDAdditionalReflectionData (HDRP)
- TextMesh
- SpriteRenderer
- ParticleSystem
- VisualEffect
- DecalProjector (HDRP)
- DensityVolume (HDRP)
- PlanarReflectionProbe (HDRP)
- Volume
- Volume + Sphere/Box/Capsule/MeshCollider pair (local volumes)

Note that the conversion of Camera (+HDAdditionalCameraData, UniversalAdditionalCameraData) components is disabled by default, because the scene main camera can't be a hybrid entity. To enable this conversion, add **HYBRID_ENTITIES_CAMERA_CONVERSION** define to your project settings.

Unity updates the transform of a hybrid entity whenever it updates the DOTS LocalToWorld component. Parenting a hybrid entity to a standard DOTS entity is supported. Hybrid entities can be included in DOTS subscenes. The managed component is serialized in the DOTS subscene.

You can write DOTS ECS queries including both IComponentData and managed hybrid components. However, these queries cannot be Burst compiled and must run in the main thread because managed components aren't thread-safe. To do this, use `foreach()` without the [BurstCompile] flag instead of an [IJobEntity](https://docs.unity3d.com/Packages/com.unity.entities@latest?subfolder=/api/Unity.Entities.IJobEntity.html) with .Schedule().

An example of setting HDRP Light component intensity:

```C#
class AnimateHDRPIntensitySystem : SystemBase
{
    protected override void OnUpdate()
    {
        foreach(var hdLight in SystemAPI.Query<RefRW<HDLightAdditionalData>>())
        {
            hdLight.ValueRW.intensity = 1.5f;
        }
    }
}
```
