using System.Diagnostics;
using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Unity.Rendering
{
    /// <summary>
    /// A tag component that allows for granular per-instance culling control.
    /// </summary>
    public struct PerInstanceCullingTag : IComponentData {}

    struct RootLODWorldReferencePoint : IComponentData
    {
        public float3 Value;
    }

    struct RootLODRange : IComponentData
    {
        public LODRange LOD;
    }

    struct LODWorldReferencePoint : IComponentData
    {
        public float3 Value;
    }

    struct LODRange : IComponentData
    {
        public float MinDist;
        public float MaxDist;

        public LODRange(MeshLODGroupComponent lodGroup, int lodMask)
        {
            float minDist = float.MaxValue;
            float maxDist = 0.0F;
            if ((lodMask & 0x01) == 0x01)
            {
                minDist = 0.0f;
                maxDist = math.max(maxDist, lodGroup.LODDistances0.x);
            }
            if ((lodMask & 0x02) == 0x02)
            {
                minDist = math.min(minDist, lodGroup.LODDistances0.x);
                maxDist = math.max(maxDist, lodGroup.LODDistances0.y);
            }
            if ((lodMask & 0x04) == 0x04)
            {
                minDist = math.min(minDist, lodGroup.LODDistances0.y);
                maxDist = math.max(maxDist, lodGroup.LODDistances0.z);
            }
            if ((lodMask & 0x08) == 0x08)
            {
                minDist = math.min(minDist, lodGroup.LODDistances0.z);
                maxDist = math.max(maxDist, lodGroup.LODDistances0.w);
            }
            if ((lodMask & 0x10) == 0x10)
            {
                minDist = math.min(minDist, lodGroup.LODDistances0.w);
                maxDist = math.max(maxDist, lodGroup.LODDistances1.x);
            }
            if ((lodMask & 0x20) == 0x20)
            {
                minDist = math.min(minDist, lodGroup.LODDistances1.x);
                maxDist = math.max(maxDist, lodGroup.LODDistances1.y);
            }
            if ((lodMask & 0x40) == 0x40)
            {
                minDist = math.min(minDist, lodGroup.LODDistances1.y);
                maxDist = math.max(maxDist, lodGroup.LODDistances1.z);
            }
            if ((lodMask & 0x80) == 0x80)
            {
                minDist = math.min(minDist, lodGroup.LODDistances1.z);
                maxDist = math.max(maxDist, lodGroup.LODDistances1.w);
            }

            MinDist = minDist;
            MaxDist = maxDist;
        }
    }

    [UpdateInGroup(typeof(StructuralChangePresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.EntitySceneOptimizations | WorldSystemFilterFlags.Editor)]
    internal partial class AddLODRequirementComponents : SystemBase
    {
        EntityQuery m_MissingRootLODRange;
        EntityQuery m_MissingRootLODWorldReferencePoint;
        EntityQuery m_MissingLODRange;
        EntityQuery m_MissingLODWorldReferencePoint;
        EntityQuery m_MissingLODGroupWorldReferencePoint;

        /// <summary>
        /// Called when this system is created.
        /// </summary>
        protected override void OnCreate()
        {
            m_MissingRootLODRange = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadOnly<MeshLODComponent>()},
                None = new[] {ComponentType.ReadOnly<RootLODRange>()},
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
            });

            m_MissingRootLODWorldReferencePoint = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<MeshLODComponent>() },
                None = new[] { ComponentType.ReadOnly<RootLODWorldReferencePoint>() },
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
            });

            m_MissingLODRange = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<MeshLODComponent>() },
                None = new[] { ComponentType.ReadOnly<LODRange>() },
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
            });

            m_MissingLODWorldReferencePoint = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<MeshLODComponent>() },
                None = new[] { ComponentType.ReadOnly<LODWorldReferencePoint>() },
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
            });

            m_MissingLODGroupWorldReferencePoint = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<MeshLODGroupComponent>() },
                None = new[] { ComponentType.ReadOnly<LODGroupWorldReferencePoint>() },
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
            });
        }

        /// <summary>
        /// Called when this system is updated.
        /// </summary>
        protected override void  OnUpdate()
        {
            EntityManager.AddComponent(m_MissingRootLODRange, typeof(RootLODRange));
            EntityManager.AddComponent(m_MissingRootLODWorldReferencePoint, typeof(RootLODWorldReferencePoint));
            EntityManager.AddComponent(m_MissingLODRange, typeof(LODRange));
            EntityManager.AddComponent(m_MissingLODWorldReferencePoint, typeof(LODWorldReferencePoint));
            EntityManager.AddComponent(m_MissingLODGroupWorldReferencePoint, typeof(LODGroupWorldReferencePoint));
        }
    }

    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(UpdatePresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.EntitySceneOptimizations | WorldSystemFilterFlags.Editor)]
    internal partial class LODRequirementsUpdateSystem : SystemBase
    {
        EntityQuery m_UpdatedLODRanges;
        EntityQuery m_LODReferencePoints;
        EntityQuery m_LODGroupReferencePoints;

        ComponentLookup<MeshLODGroupComponent> MeshLODGroupComponent;
        ComponentTypeHandle<MeshLODComponent> MeshLODComponent;
        ComponentLookup<LocalToWorld> LocalToWorldLookup;
        ComponentTypeHandle<RootLODRange> RootLODRange;
        ComponentTypeHandle<LODRange> LODRange;

        [BurstCompile]
        struct UpdateLODRangesJob : IJobChunk
        {
            [ReadOnly] public ComponentLookup<MeshLODGroupComponent>    MeshLODGroupComponent;

            public ComponentTypeHandle<MeshLODComponent>                MeshLODComponent;
            [ReadOnly] public ComponentLookup<LocalToWorld>     LocalToWorldLookup;
            public ComponentTypeHandle<RootLODRange>                    RootLODRange;
            public ComponentTypeHandle<LODRange>                        LODRange;

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private static void CheckDeepHLODSupport(Entity entity)
            {
                if (entity != Entity.Null)
                    throw new System.NotImplementedException("Deep HLOD is not supported yet");
            }

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // This job is not written to support queries with enableable component types.
                Assert.IsFalse(useEnabledMask);

                var rootLODRange = chunk.GetNativeArray(ref RootLODRange);
                var lodRange = chunk.GetNativeArray(ref LODRange);
                var meshLods = chunk.GetNativeArray(ref MeshLODComponent);
                var instanceCount = chunk.Count;

                for (int i = 0; i < instanceCount; i++)
                {
                    var meshLod = meshLods[i];
                    var lodGroupEntity = meshLod.Group;
                    var lodMask = meshLod.LODMask;
                    var lodGroup = MeshLODGroupComponent[lodGroupEntity];

                    lodRange[i] = new LODRange(lodGroup, lodMask);

                }

                for (int i = 0; i < instanceCount; i++)
                {
                    var meshLod = meshLods[i];
                    var lodGroupEntity = meshLod.Group;
                    var lodGroup = MeshLODGroupComponent[lodGroupEntity];
                    var parentMask = lodGroup.ParentMask;
                    var parentGroupEntity = lodGroup.ParentGroup;

                    // Store LOD parent group in MeshLODComponent to avoid double indirection for every entity
                    meshLod.ParentGroup = parentGroupEntity;
                    meshLods[i] = meshLod;

                    RootLODRange rootLod;

                    if (parentGroupEntity == Entity.Null)
                    {
                        rootLod.LOD.MinDist = 0;
                        rootLod.LOD.MaxDist = 1048576.0f;
                    }
                    else
                    {
                        var parentLodGroup = MeshLODGroupComponent[parentGroupEntity];
                        rootLod.LOD = new LODRange(parentLodGroup, parentMask);
                        CheckDeepHLODSupport(parentLodGroup.ParentGroup);
                    }

                    rootLODRange[i] = rootLod;
                }
            }
        }

        [BurstCompile]
        struct UpdateLODGroupWorldReferencePointsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<MeshLODGroupComponent> MeshLODGroupComponent;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> LocalToWorld;
            public ComponentTypeHandle<LODGroupWorldReferencePoint> LODGroupWorldReferencePoint;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // This job is not written to support queries with enableable component types.
                Assert.IsFalse(useEnabledMask);

                var meshLODGroupComponent = chunk.GetNativeArray(ref MeshLODGroupComponent);
                var localToWorld = chunk.GetNativeArray(ref LocalToWorld);
                var lodGroupWorldReferencePoint = chunk.GetNativeArray(ref LODGroupWorldReferencePoint);
                var instanceCount = chunk.Count;

                for (int i = 0; i < instanceCount; i++)
                {
                    lodGroupWorldReferencePoint[i] = new LODGroupWorldReferencePoint { Value = math.transform(localToWorld[i].Value, meshLODGroupComponent[i].LocalReferencePoint) };
                }
            }
        }

        [BurstCompile]
        struct UpdateLODWorldReferencePointsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<MeshLODComponent> MeshLODComponent;
            [ReadOnly] public ComponentLookup<LODGroupWorldReferencePoint> LODGroupWorldReferencePoint;
            public ComponentTypeHandle<RootLODWorldReferencePoint> RootLODWorldReferencePoint;
            public ComponentTypeHandle<LODWorldReferencePoint> LODWorldReferencePoint;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // This job is not written to support queries with enableable component types.
                Assert.IsFalse(useEnabledMask);

                var rootLODWorldReferencePoint = chunk.GetNativeArray(ref RootLODWorldReferencePoint);
                var lodWorldReferencePoint = chunk.GetNativeArray(ref LODWorldReferencePoint);
                var meshLods = chunk.GetNativeArray(ref MeshLODComponent);
                var instanceCount = chunk.Count;

                for (int i = 0; i < instanceCount; i++)
                {
                    var meshLod = meshLods[i];
                    var lodGroupEntity = meshLod.Group;
                    var lodGroupWorldReferencePoint = LODGroupWorldReferencePoint[lodGroupEntity].Value;

                    lodWorldReferencePoint[i] = new LODWorldReferencePoint { Value = lodGroupWorldReferencePoint };
                }

                for (int i = 0; i < instanceCount; i++)
                {
                    var meshLod = meshLods[i];
                    var parentGroupEntity = meshLod.ParentGroup;

                    RootLODWorldReferencePoint rootPoint;

                    if (parentGroupEntity == Entity.Null)
                    {
                        rootPoint.Value = new float3(0, 0, 0);
                    }
                    else
                    {
                        var parentGroupWorldReferencePoint = LODGroupWorldReferencePoint[parentGroupEntity].Value;
                        rootPoint.Value = parentGroupWorldReferencePoint;
                    }

                    rootLODWorldReferencePoint[i] = rootPoint;
                }
            }
        }

        /// <inheritdoc/>
        protected override void OnCreate()
        {
            // Change filter: LODGroupConversion add MeshLODComponent for all LOD children. When the MeshLODComponent is added/changed, we recalculate LOD ranges.
            m_UpdatedLODRanges = GetEntityQuery(ComponentType.ReadOnly<LocalToWorld>(), typeof(MeshLODComponent), typeof(RootLODRange), typeof(LODRange));
            m_UpdatedLODRanges.SetChangedVersionFilter(ComponentType.ReadWrite<MeshLODComponent>());

            m_LODReferencePoints = GetEntityQuery(ComponentType.ReadOnly<LocalToWorld>(), ComponentType.ReadOnly<MeshLODComponent>(), typeof(RootLODWorldReferencePoint), typeof(LODWorldReferencePoint));

            // Change filter: LOD Group world reference points only change when MeshLODGroupComponent or LocalToWorld change
            m_LODGroupReferencePoints = GetEntityQuery(ComponentType.ReadOnly<MeshLODGroupComponent>(), ComponentType.ReadOnly<LocalToWorld>(), typeof(LODGroupWorldReferencePoint));
            m_LODGroupReferencePoints.SetChangedVersionFilter(new[] { ComponentType.ReadWrite<MeshLODGroupComponent>(), ComponentType.ReadWrite<LocalToWorld>() });

            MeshLODGroupComponent = GetComponentLookup<MeshLODGroupComponent>(true);
            MeshLODComponent = GetComponentTypeHandle<MeshLODComponent>();
            LocalToWorldLookup = GetComponentLookup<LocalToWorld>(true);
            RootLODRange = GetComponentTypeHandle<RootLODRange>();
            LODRange = GetComponentTypeHandle<LODRange>();
        }

        /// <inheritdoc/>
        protected override void OnUpdate()
        {
            MeshLODGroupComponent.Update(this);
            MeshLODComponent.Update(this);
            LocalToWorldLookup.Update(this);
            RootLODRange.Update(this);
            LODRange.Update(this);

            var updateLODRangesJob = new UpdateLODRangesJob
            {
                MeshLODGroupComponent = MeshLODGroupComponent,
                MeshLODComponent = MeshLODComponent,
                LocalToWorldLookup = LocalToWorldLookup,
                RootLODRange = RootLODRange,
                LODRange = LODRange
            };

            var updateGroupReferencePointJob = new UpdateLODGroupWorldReferencePointsJob
            {
                MeshLODGroupComponent = GetComponentTypeHandle<MeshLODGroupComponent>(true),
                LocalToWorld = GetComponentTypeHandle<LocalToWorld>(true),
                LODGroupWorldReferencePoint = GetComponentTypeHandle<LODGroupWorldReferencePoint>(),
            };

            var updateReferencePointJob = new UpdateLODWorldReferencePointsJob
            {
                //MeshLODGroupComponent = GetComponentLookup<MeshLODGroupComponent>(true),
                MeshLODComponent = GetComponentTypeHandle<MeshLODComponent>(true),
                LODGroupWorldReferencePoint = GetComponentLookup<LODGroupWorldReferencePoint>(true),
                RootLODWorldReferencePoint = GetComponentTypeHandle<RootLODWorldReferencePoint>(),
                LODWorldReferencePoint = GetComponentTypeHandle<LODWorldReferencePoint>(),
            };

            var depLODRanges = updateLODRangesJob.ScheduleParallel(m_UpdatedLODRanges, Dependency);
            var depGroupReferencePoints = updateGroupReferencePointJob.ScheduleParallel(m_LODGroupReferencePoints, Dependency);
            var depCombined = JobHandle.CombineDependencies(depLODRanges, depGroupReferencePoints);
            var depReferencePoints = updateReferencePointJob.ScheduleParallel(m_LODReferencePoints, depCombined);

            Dependency = JobHandle.CombineDependencies(depReferencePoints, depReferencePoints);
        }
    }
}
