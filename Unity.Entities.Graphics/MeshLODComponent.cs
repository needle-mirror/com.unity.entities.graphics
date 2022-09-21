using Unity.Entities;
using Unity.Mathematics;

namespace Unity.Rendering
{
    /// <summary>
    /// Represents an LOD group.
    /// </summary>
    /// <remarks>
    /// Each MeshLODGroupComponent contains multiple MeshLODComponents and can also have multiple child groups.
    /// </remarks>
    public struct MeshLODGroupComponent : IComponentData
    {
        /// <summary>
        /// The LOD parent group.
        /// </summary>
        public Entity    ParentGroup;

        /// <summary>
        /// The LOD mask.
        /// </summary>
        /// <remarks>
        /// Each bit matches with one of the 8 possible LOD levels.
        /// </remarks>
        public int       ParentMask;

        
        /// <summary>
        /// The low part of the LOD distance container.
        /// </summary>
        public float4    LODDistances0;

        /// <summary>
        /// LOD distance container, high part.
        /// </summary>
        public float4    LODDistances1;

        
        /// <summary>
        /// Local reference point.
        /// </summary>
        public float3    LocalReferencePoint;
    }

    
    /// <summary>
    /// An unmanaged component that represents a world reference point to use for LOD group.
    /// </summary>
    struct LODGroupWorldReferencePoint : IComponentData
    {
        /// <summary>
        /// The world-space x, y, and z position of the reference point.
        /// </summary>
        public float3 Value;
    }

    /// <summary>
    /// An unamanged component that represents a mesh LOD entity.
    /// </summary>
    public struct MeshLODComponent : IComponentData
    {
        /// <summary>
        /// The parent LOD group entity.
        /// </summary>
        public Entity   Group;

        
        /// <summary>
        /// The mesh LOD parent group.
        /// </summary>
        public Entity   ParentGroup;

        /// <summary>
        /// The LOD mask.
        /// </summary>
        public int      LODMask;
    }
}
