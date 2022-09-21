# Entities Graphics overview

Entities Graphics acts as a bridge between DOTS and Unity's existing rendering architecture. This allows you to use ECS entities instead of GameObjects for significantly improved runtime memory layout and performance in large scenes, while maintaining the compatibility and ease of use of Unity's existing workflows.

## Entities Graphics feature matrix

For more information about the render pipeline feature compatibility, see [Entities Graphics feature matrix](entities-graphics-versions.md).

## The GameObject conversion system

Entities Graphics includes systems that convert GameObjects into equivalent DOTS entities. You can use these systems to convert the GameObjects in the Unity Editor, or at runtime. 

To convert entites in the Unity Editor, put them in a SubScene. The Unity Editor performs the conversion offline, and saves the results to disk. To convert your GameObjects to entities at runtime at scene load, add a ConvertToEntity component to them. Using offline SubScene conversion results in significantly better scene loading performance.

Unity performs the following steps during conversion:

- The conversion system converts [MeshRenderer](https://docs.unity3d.com/Manual/class-MeshRenderer.html) and[ MeshFilter](https://docs.unity3d.com/Manual/class-MeshFilter.html) components into a DOTS RenderMesh component on the entity. Depending on the render pipeline your Project uses, the conversion system might also add other rendering-related components.
- The conversion system converts[ LODGroup](https://docs.unity3d.com/Manual/class-LODGroup.html) components in GameObject hierarchies to DOTS MeshLODGroupComponents. Each entity referred by the LODGroup component has a DOTS MeshLODComponent.
- The conversion system converts the Transform of each GameObject into a DOTS LocalToWorld component on the entity. Depending on the Transform's properties, the conversion system might also add DOTS Translation, Rotation, and NonUniformScale components.

## Runtime functionality

At runtime, Entities Graphics processes all entities that have LocalToWorld, RenderMesh, and RenderBounds DOTS components. Many HDRP and URP features require their own material property components. These components are added during the MeshRenderer conversion. Processed entities are added to batches. Unity renders the batches using the [SRP Batcher](https://blogs.unity3d.com/2019/02/28/srp-batcher-speed-up-your-rendering/).

There are two best practice ways to instantiate entities at runtime: Prefabs and the `RenderMeshUtility.AddComponents` API:

* Unity converts Prefabs to an optimal data layout during DOTS conversion. There are no additional structural changes during instantiation. Use prefabs if you want to instantiate complex objects.
* If you want to build the entity from a C# script, use the [RenderMeshUtility.AddComponents](runtime-entity-creation.md) API. This API automatically adds all the graphics components that the active render pipeline requires. Don't add graphics components manually. This is less efficient (adds structural changes) and is not forward compatible with future Entities Graphics package versions.