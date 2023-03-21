# Known issues

Entities Graphics is a bridge between Unity's entity component system and Unity's existing rendering architecture. In some cases, the workflows between these two architectures are incompatible. This page describes the incompatibility issues and provides a workaround if one exists.

## Lighting

The [subscene](https://docs.unity3d.com/Packages/com.unity.entities@latest?subfolder=/manual/conversion-subscenes.html) workflow in Unity's entity component system architecture is incompatible with some lighting features and workflows.

### Automatic lightmap generation

In GameObject-based Unity applications, you can enable the [auto-generate lightmaps](xref:UnityEngine.LightingSettings.autoGenerate) feature to automatically precompute lighting data for a scene when the scene data changes. This workflow is not possible for entity-based applications because they use subscenes. So, if you install Entities and Entities Graphics, Unity disables this option in the [Lighting window](xref:lighting-window) and you must instead click **Generate Lighting** to manually start the lightbaking process.	

### Baked lightmaps with shared entity scenes

In entities-based applications, you can use the same entity scene within multiple scenes. This helps you to reuse functionality and assets throughout your application. However, there is a lighting-related limitation to this workflow in that entity scenes can only store and use a single baked lightmap.

Unity stores the baked lightmap for an entity scene as part of the entity scene itself, and each entity scene can only store a single lightmap. This means if you bake lightmaps for a configuration of entity scenes, the lightmaps for each one will only ever represent the entities from the configuration of entity scenes at the time of the bake. If you unload an entity scene, the lightmaps of the other entity scenes will still show shadows for the entities in the unloaded entity scene. If you load a new entity scene, the lightmaps in the other entity scenes won't show shadows from entities in the new entity scene. For example:

1. You have two entity scenes. One that contains a plane and one that contains a cube.
2. You bake the lighting and the result, from the perspective of the plane entity scene, is a lightmap that shows a shadow from the cube cast onto the plane.
3. If you unload the entity scene that contains the cube, the baked lighting for the plane entity scene still shows the cube shadow.
4. If you load a new entity scene that contains a sphere, the baked lighting for the plane entity scene won't show a shadow for the sphere.

If you want to bake lightmaps and change which entity scenes are loaded at runtime, use one of the following methods to work around this limitation:

* Use real-time lighting.
* Arrange the content of each entity scene in a way that entities in one don't cast shadows into another. If this isn't possible, also consider combining entity scenes that must cast shadows into one another.

## Additional resources

* [Entities Graphics feature matrix](entities-graphics-versions.md)
