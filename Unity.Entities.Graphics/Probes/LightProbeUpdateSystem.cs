using System.Collections.Generic;
using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace Unity.Rendering
{
    [UpdateInGroup(typeof(UpdatePresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    partial class LightProbeUpdateSystem : SystemBase
    {
        private EntityQuery m_ProbeGridQuery;
        private EntityQuery m_ProbeGridAnchorQuery;

        private readonly EntityQueryDesc m_ProbeGridQueryDesc = new()
        {
            All = new []
            {
                ComponentType.ReadWrite<BuiltinMaterialPropertyUnity_SHCoefficients>(),
                ComponentType.ReadOnly<WorldRenderBounds>(),
                ComponentType.ReadOnly<BlendProbeTag>()
            },
            None = new []
            {
                ComponentType.ReadOnly<OverrideLightProbeAnchorComponent>()
            }
        };

        private readonly EntityQueryDesc m_ProbeGridAnchorQueryDesc = new()
        {
            All = new []
            {
                ComponentType.ReadWrite<BuiltinMaterialPropertyUnity_SHCoefficients>(),
                ComponentType.ReadOnly<WorldRenderBounds>(),
                ComponentType.ReadOnly<BlendProbeTag>(),
                ComponentType.ReadOnly<OverrideLightProbeAnchorComponent>()
            }
        };

        private readonly ComponentType[] gridQueryFilter = {ComponentType.ReadOnly<WorldRenderBounds>(), ComponentType.ReadWrite<BlendProbeTag>()};
        private readonly ComponentType[] gridQueryAnchorFilter = { ComponentType.ReadOnly<OverrideLightProbeAnchorComponent>(), ComponentType.ReadWrite<BlendProbeTag>() };

        private ComponentType[] gridQueryFilterForAmbient = { ComponentType.ReadWrite<BlendProbeTag>() };

        /// <inheritdoc/>
        protected override void OnCreate()
        {
            m_ProbeGridQuery = GetEntityQuery(m_ProbeGridQueryDesc);
            m_ProbeGridQuery.SetChangedVersionFilter(gridQueryFilter);

            m_ProbeGridAnchorQuery = GetEntityQuery(m_ProbeGridAnchorQueryDesc);
            // Probes with anchors can't use filters because updating the position of the anchor
            // would not update the probes
        }

        internal static bool IsValidLightProbeGrid()
        {
            var probes = LightmapSettings.lightProbes;
            bool validGrid = probes != null && probes.count > 0;
            return validGrid;
        }

        /// <inheritdoc/>
        protected override void OnUpdate()
        {
            if (IsValidLightProbeGrid())
            {
                UpdateEntitiesFromGrid();
            }
        }

        private static void UpdateEntitiesFromAmbientProbe(
            LightProbeUpdateSystem system,
            EntityQuery query,
            ComponentType[] queryFilter,
            SphericalHarmonicsL2 ambientProbe,
            SphericalHarmonicsL2 lastProbe)
        {
            Profiler.BeginSample("UpdateEntitiesFromAmbientProbe");
            var updateAll = ambientProbe != lastProbe;
            if (updateAll)
            {
                query.ResetFilter();
            }

            var job = new UpdateSHValuesJob
            {
                Coefficients = new SHCoefficients(ambientProbe),
                SHType = system.GetComponentTypeHandle<BuiltinMaterialPropertyUnity_SHCoefficients>(),
            };

            system.Dependency = job.ScheduleParallel(query, system.Dependency);

            if (updateAll)
            {
                query.SetChangedVersionFilter(queryFilter);
            }
            Profiler.EndSample();
        }

        private List<Vector3> m_Positions = new List<Vector3>(512);
        private List<SphericalHarmonicsL2> m_LightProbes = new List<SphericalHarmonicsL2>(512);
        private List<Vector4> m_OcclusionProbes = new List<Vector4>(512);
        private void UpdateEntitiesFromGrid()
        {
            Profiler.BeginSample("UpdateEntitiesFromGrid");

            var SHType = GetComponentTypeHandle<BuiltinMaterialPropertyUnity_SHCoefficients>();
            var worldRenderBoundsType = GetComponentTypeHandle<WorldRenderBounds>();
            var overrideLightProbeAnchorType = GetComponentTypeHandle<OverrideLightProbeAnchorComponent>(true);

            var gridAnchorChunks = m_ProbeGridAnchorQuery.ToArchetypeChunkArray(Allocator.Temp);
            if (gridAnchorChunks.Length > 0)
            {
                //TODO: Bring this off the main thread when we have new c++ API
                Dependency.Complete();
            }
            foreach (var chunk in gridAnchorChunks)
            {
                var chunkSH = chunk.GetNativeArray(ref SHType);

                m_Positions.Clear();
                m_LightProbes.Clear();
                m_OcclusionProbes.Clear();

                var positions = chunk.GetNativeArray(ref overrideLightProbeAnchorType);
                for (var i = 0; i != positions.Length; i++)
                    m_Positions.Add(SystemAPI.GetComponent<LocalToWorld>(positions[i].entity).Position);

                LightProbes.CalculateInterpolatedLightAndOcclusionProbes(m_Positions, m_LightProbes, m_OcclusionProbes);

                for (var i = 0; i < m_Positions.Count; ++i)
                {
                    var shCoefficients = new SHCoefficients(m_LightProbes[i], m_OcclusionProbes[i]);
                    chunkSH[i] = new BuiltinMaterialPropertyUnity_SHCoefficients() {Value = shCoefficients};
                }
            }

            var gridChunks  = m_ProbeGridQuery.ToArchetypeChunkArray(Allocator.Temp);

            if (gridChunks.Length == 0 && gridAnchorChunks.Length == 0)
            {
                Profiler.EndSample();
                return;
            }

            //TODO: Bring this off the main thread when we have new c++ API
            Dependency.Complete();

            foreach (var chunk in gridChunks)
            {
                var chunkSH = chunk.GetNativeArray(ref SHType);

                m_Positions.Clear();
                m_LightProbes.Clear();
                m_OcclusionProbes.Clear();

                var bounds = chunk.GetNativeArray(ref worldRenderBoundsType);
                for (var i = 0; i != bounds.Length; i++)
                    m_Positions.Add(bounds[i].Value.Center);

                LightProbes.CalculateInterpolatedLightAndOcclusionProbes(m_Positions, m_LightProbes, m_OcclusionProbes);

                for (var i = 0; i < m_Positions.Count; ++i)
                {
                    var shCoefficients = new SHCoefficients(m_LightProbes[i], m_OcclusionProbes[i]);
                    chunkSH[i] = new BuiltinMaterialPropertyUnity_SHCoefficients() {Value = shCoefficients};
                }
            }

            Profiler.EndSample();
        }

        [BurstCompile]
        struct UpdateSHValuesJob : IJobChunk
        {
            public SHCoefficients Coefficients;
            public ComponentTypeHandle<BuiltinMaterialPropertyUnity_SHCoefficients> SHType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // This job is not written to support queries with enableable component types.
                Assert.IsFalse(useEnabledMask);

                var chunkSH = chunk.GetNativeArray(ref SHType);

                for (var i = 0; i < chunkSH.Length; i++)
                {
                    chunkSH[i] = new BuiltinMaterialPropertyUnity_SHCoefficients {Value = Coefficients};
                }
            }
        }
    }
}
