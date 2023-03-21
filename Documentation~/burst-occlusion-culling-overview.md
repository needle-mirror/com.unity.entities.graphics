# Burst occlusion culling overview

The Burst occlusion culling system disables rendering for entities that are hidden behind other entities. This reduces the amount of data that Unity uploads to the GPU every frame and the amount of unnecessary work that the GPU must do.

## How Burst occlusion culling works

From the point of view of the cameras, lights, and reflection probes you specify, the Burst occlusion culling system determines which entities are completely hidden and don't need to be sent to the GPU for rendering. To do this, the system splits entities into occluders and occludees. The system gets all occluders and calculates which occludees are hidden by them.

For performance reasons, the culling system doesn't use the same meshes in its culling calculations that the rendering system uses to draw entities. Instead, each occluder entity needs an additional lower-resolution mesh for the culling system to use instead. This occlusion mesh must be completely inscribed within the original mesh to avoid artifacts such as visible popping which is where objects appear and disappear visibly on-screen.

Entities that use the Mesh Renderer component with **Dynamic Occlusion** set at [author time](https://docs.unity3d.com/Packages/com.unity.entities@latest?subfolder=/manual/editor-authoring-runtime.html) will be occludees. It's your responsibility to specify which entities are occluders. For help on how to decide which entities should be occluders, refer to [How to choose occluders](#how-to-choose-occluders).

## When to use Burst occlusion culling

Burst occlusion culling isn't appropriate for every application or scene. Scenes with many unique objects (with unique meshes or materials) that produce a lot of overdraw are perfect for Burst occlusion culling. Examples of this type of scene include large open worlds, dense cities, or interiors with separate rooms.

Entities graphics can render instanced objects very quickly so it's often not beneficial to calculate which instanced objects are or aren't occluded and instead pass them all to the GPU to render. This is because the overhead of the occlusion culling calculations can exceed the overhead saved by reducing the number of instanced objects to draw.

If there is a mix of unique and instanced objects in a scene, you can enable Burst occlusion culling for the scene, but make the instanced objects not occludees (disable **Dynamic Occlusion** on their Mesh Renderer component). This makes Burst occlusion culling optimize the draw submission for unique objects without wasting resources processing the instanced objects.

## How to choose occluders

Occlusion culling gives performance improvements if the overhead of the occlusion culling process is less than the overhead saved by reducing the number of entities to draw. The more occluders there are and the more complex their shape, the more resource intensive the occlusion culling process is. This means that it's important to choose which entities to set as occluders. An entity's size and shape decide its suitability as an occluder. 

Entities likely to be suitable occluders are:

* Entities that fill a significant part of the screen. For example, in a first-person perspective, items in the character’s hands are close to the camera. Also buildings and other large objects that the character can approach, that move toward the character, or that move around the scene.
* Entities that are likely to occlude other entities. For example, a large mountain or building located in the center of the scene, or the walls in the interior of a building.

Entities likely to be unsuitable occluders are:

* Entities unlikely to fill a significant part of the screen. For example, a small book that the camera will never go very close to, or large objects that will always be far away from the camera and will therefore have small screen-space coverage.
* Entities that are unlikely to occlude other entities. For example, mountains at the far distant edge of  a world, or a road mesh where there are few/no objects below the ground. Objects that are thin from more than one perspective are also unlikely to occlude other entities.
* Entities that have a complex and non-convex shape. For example, hands or a tree with many branches and leaves.


## Additional resources

- [Set up Burst occlusion culling](burst-occlusion-culling-setup.md)
