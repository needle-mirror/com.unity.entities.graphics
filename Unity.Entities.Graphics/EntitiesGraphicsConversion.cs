using System.Collections.Generic;
using Unity.Transforms;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;
#if HDRP_10_0_0_OR_NEWER
using UnityEngine.Rendering.HighDefinition;
#endif
#if URP_10_0_0_OR_NEWER
using UnityEngine.Rendering.Universal;
#endif

namespace Unity.Rendering
{
#if !UNITY_DISABLE_MANAGED_COMPONENTS
#if !TINY_0_22_0_OR_NEWER
    class LightCompanionBaker : Baker<Light>
    {
        public override void Bake(Light authoring)
        {
            AddComponentObject(authoring);

#if UNITY_EDITOR
            // Explicitly store the LightBakingOutput using a component, so we can restore it
            // at runtime.
            var bakingOutput = authoring.bakingOutput;
            AddComponent(new LightBakingOutputData {Value = bakingOutput});
#endif
        }
    }

    class LightProbeProxyVolumeCompanionBaker : Baker<LightProbeProxyVolume>
    {
        public override void Bake(LightProbeProxyVolume authoring)
        {
            AddComponentObject(authoring);
        }
    }

    class ReflectionProbeCompanionBaker : Baker<ReflectionProbe>
    {
        public override void Bake(ReflectionProbe authoring)
        {
            AddComponentObject(authoring);
        }
    }

    class TextMeshCompanionBaker : Baker<TextMesh>
    {
        public override void Bake(TextMesh authoring)
        {
            var meshRenderer = GetComponent<MeshRenderer>();
            AddComponentObject(authoring);
            AddComponentObject(meshRenderer);
        }
    }

    class SpriteRendererCompanionBaker : Baker<SpriteRenderer>
    {
        public override void Bake(SpriteRenderer authoring)
        {
            AddComponentObject(authoring);
        }
    }

    class VisualEffectCompanionBaker : Baker<VisualEffect>
    {
        public override void Bake(VisualEffect authoring)
        {
            AddComponentObject(authoring);
        }
    }

    class ParticleSystemCompanionBaker : Baker<ParticleSystem>
    {
        public override void Bake(ParticleSystem authoring)
        {
            var particleSystemRenderer = GetComponent<ParticleSystemRenderer>();
            AddComponentObject(authoring);
            AddComponentObject(particleSystemRenderer);
        }
    }

    class AudioSourceCompanionBaker : Baker<AudioSource>
    {
        public override void Bake(AudioSource authoring)
        {
            AddComponentObject(authoring);
        }
    }

#if SRP_10_0_0_OR_NEWER
    class VolumeCompanionBaker : Baker<Volume>
    {
        public override void Bake(Volume authoring)
        {
            var sphereCollider = GetComponent<SphereCollider>();
            var boxCollider = GetComponent<BoxCollider>();
            var capsuleCollider = GetComponent<CapsuleCollider>();
            var meshCollider = GetComponent<MeshCollider>();

            AddComponentObject(authoring);

            if(sphereCollider != null)
                AddComponentObject(sphereCollider);

            if(boxCollider != null)
                AddComponentObject(boxCollider);

            if(capsuleCollider != null)
                AddComponentObject(capsuleCollider);

            if(meshCollider != null)
                AddComponentObject(meshCollider);
        }
    }
#endif

#if HDRP_10_0_0_OR_NEWER
    class HDAdditionalLightDataCompanionBaker : Baker<HDAdditionalLightData>
    {
        public override void Bake(HDAdditionalLightData authoring)
        {
            var light = GetComponent<Light>();
            // A disabled light component won't be added to the companion GameObject,
            // other components that require the light component should not be added either.
            if (light.enabled)
            {
#if UNITY_EDITOR
                var isBaking = light.lightmapBakeType == LightmapBakeType.Baked;
                if(!isBaking)
                    AddComponentObject(authoring);
#else
                AddComponentObject(authoring);
#endif
            }
        }
    }

    class HDAdditionalReflectionDataCompanionBaker : Baker<HDAdditionalReflectionData>
    {
        public override void Bake(HDAdditionalReflectionData authoring)
        {
            AddComponentObject(authoring);
        }
    }

    class DecalProjectorCompanionBaker : Baker<DecalProjector>
    {
        public override void Bake(DecalProjector authoring)
        {
            AddComponentObject(authoring);
        }
    }

    class LocalVolumetricFogCompanionBaker : Baker<LocalVolumetricFog>
    {
        public override void Bake(LocalVolumetricFog authoring)
        {
            AddComponentObject(authoring);
        }
    }

    class PlanarReflectionProbeCompanionBaker : Baker<PlanarReflectionProbe>
    {
        public override void Bake(PlanarReflectionProbe authoring)
        {
            AddComponentObject(authoring);
        }
    }
#if PROBEVOLUME_CONVERSION
    class ProbeVolumeCompanionBaker : Baker<ProbeVolume>
    {
        public override void Bake(ProbeVolume authoring)
        {
            AddComponentObject(authoring);
        }
    }
#endif
#endif

#if URP_10_0_0_OR_NEWER
    class UniversalAdditionalLightDataCompanionBaker : Baker<UniversalAdditionalLightData>
    {
        public override void Bake(UniversalAdditionalLightData authoring)
        {
            var light = GetComponent<Light>();

            // A disabled light component won't be added to the companion GameObject,
            // other components that require the light component should not be added either.
            if (light.enabled)
            {
#if UNITY_EDITOR
                var isBaking = light.lightmapBakeType == LightmapBakeType.Baked;
                if (!isBaking)
                    AddComponentObject(authoring);
#else
            AddComponentObject(authoring);
#endif
            }
        }
    }
#endif

#if HYBRID_ENTITIES_CAMERA_CONVERSION
    class CameraCompanionBaker : Baker<Camera>
    {
        public override void Bake(Camera authoring)
        {
            AddComponentObject(authoring);
        }
    }

#if HDRP_10_0_0_OR_NEWER
    class HDAdditionalCameraDataCompanionBaker : Baker<HDAdditionalCameraData>
    {
        public override void Bake(HDAdditionalCameraData authoring)
        {
            AddComponentObject(authoring);
        }
    }
#endif

#if URP_10_0_0_OR_NEWER
    class UniversalAdditionalCameraDataCompanionBaker : Baker<UniversalAdditionalCameraData>
    {
        public override void Bake(UniversalAdditionalCameraData authoring)
        {
            AddComponentObject(authoring);
        }
    }
#endif
#endif
#endif

#endif
}
