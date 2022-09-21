namespace Unity.Rendering
{
    
    /// <summary>
    /// Represents per-thread statistics that Entities Graphics collects during runtime.
    /// </summary>
    public struct EntitiesGraphicsPerThreadStats
    {
        // This struct needs to be padded to a cache line multiple to avoid false sharing
        // If you add or remove any members here you must add or remove padding to the struct

        
        /// <summary>
        /// The total number of chunks executed.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int ChunkTotal;

        
        /// <summary>
        /// The chunk count across all LOD levels.
        /// </summary>
        public int ChunkCountAnyLod;

        
        /// <summary>
        /// The number of chunks partially culled by the frustum.
        /// </summary>
        /// <remarks>
        /// Entities Graphics considers a chunk to be partially culled if the chunk contains some entities that are within the frustum and some entities that are outside the frustum.
        /// This stat is only available in the Editor.
        /// </remarks>
        public int ChunkCountInstancesProcessed;

        /// <summary>
        /// The number of chunks that contain entities which are all in the frustum.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int ChunkCountFullyIn;

        /// <summary>
        /// The total number of culling tests performed on partially culled chunks.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int InstanceTests;

        
        /// <summary>
        /// Total count of the LOD executed.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int LodTotal;

        
        /// <summary>
        /// Number of the culling chunks without LOD data.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int LodNoRequirements;

        
        /// <summary>
        /// Number of enabled or disabled LODs in this frame.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int LodChanged;

        
        /// <summary>
        /// Number of tested LOD chunks.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int LodChunksTested;

        
        /// <summary>
        /// The total number of entities that Entities Graphics renderer.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int RenderedEntityCount;

        /// <summary>
        /// The number of the draw commands.
        /// </summary>
        public int DrawCommandCount;

        /// <summary>
        /// The number of the ranges.
        /// </summary>
        public int DrawRangeCount;
    }

    /// <summary>
    /// Represents statistics that Entities Graphics collects during runtime.
    /// </summary>
    public struct EntitiesGraphicsStats
    {
        /// <summary>
        /// Total number of chunks
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int ChunkTotal;

        /// <summary>
        /// Total number of chunks if any of the LOD are enabled in ChunkInstanceLodEnabled entity.
        /// </summary>
        public int ChunkCountAnyLod;

        /// <summary>
        /// Total number of chunks processed which are partially intersected with the view frustum.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int ChunkCountInstancesProcessed;

        /// <summary>
        /// Total number of chunks processed which are fully inside of the view frustum.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int ChunkCountFullyIn;

        /// <summary>
        /// Total number of instance checks across all LODs.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int InstanceTests;

        /// <summary>
        /// Total number of LODs across all archetype entities chunks.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int LodTotal;

        /// <summary>
        /// Number of the culling chunks without LOD data.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int LodNoRequirements;

        /// <summary>
        /// Number of enabled or disabled LODs in this frame.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int LodChanged;

        /// <summary>
        /// Number of tested LOD chunks.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int LodChunksTested;

        /// <summary>
        /// Camera move distance since the last frame.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public float CameraMoveDistance;

        /// <summary>
        /// Total number of batches.
        /// </summary>
        public int BatchCount;

        /// <summary>
        /// Accumulated number of rendered entities across all threads.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int RenderedInstanceCount;

        /// <summary>
        /// Accumulated number of the draw commands.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int DrawCommandCount;

        /// <summary>
        /// Accumulated number of the draw ranges.
        /// </summary>
        /// <remarks>        
        /// This stat is only available in the Editor.
        /// </remarks>
        public int DrawRangeCount;

        /// <summary>
        /// The total number of bytes of the GPU memory used including upload and fence memory.
        /// </summary>
        public long BytesGPUMemoryUsed;

        
        /// <summary>
        /// The number of bytes of the GPU memory used by uploaded in the current frame.
        /// </summary>
        public long BytesGPUMemoryUploadedCurr;

        /// <summary>
        /// Maximum number of bytes of the GPU memory used for uploading.
        /// </summary>
        public long BytesGPUMemoryUploadedMax;
    }
}
