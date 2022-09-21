using Unity.Entities;
using Unity.Mathematics;

namespace Unity.Rendering
{
    
    /// <summary>
    /// An unmanaged component that represent the render bounds.
    /// </summary>
    public struct RenderBounds : IComponentData
    {
        /// <summary>
        /// The axis-aligned render bounds.
        /// </summary>
        public AABB Value;
    }

    
    /// <summary>
    /// An unmanaged component that represents the world render bounds.
    /// </summary>
    public struct WorldRenderBounds : IComponentData
    {
        /// <summary>
        /// The axis-aligned render bounds.
        /// </summary>
        public AABB Value;
    }

    /// <summary>
    /// An unmanaged component that represents the render bounds of a chunk.
    /// </summary>
    public struct ChunkWorldRenderBounds : IComponentData
    {
        /// <summary>
        /// The axis-aligned render bounds.
        /// </summary>
        public AABB Value;
    }
}
