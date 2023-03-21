# Burst occlusion culling requirements and compatibility

This page contains information on requirements and feature compatibility of Burst occlusion culling. Burst occlusion culling currently only supports [Entities](https://docs.unity3d.com/Packages/com.unity.entities@latest/index.html)-based applications.

## Hardware requirements

Burst occlusion culling requires the target CPU to support SSE4 or Neon instructions. Burst doesn't support 32-bit intrinsics for Neon so, to build for ARM, you must use a 64-bit build target.

## Renderer compatibility

The following table shows which renderers Burst occlusion culling supports.

| Renderer                 | Occludee support | Occluder support |
| ------------------------ | ---------------- | ---------------- |
| Mesh Renderer            | Yes              | Yes              |
| Skinned Mesh Renderer    | No               | No               |
| Visual Effect            | No               | No               |
| Particle System Renderer | No               | No               |
| Light Probe Proxy Volume | No               | N/A              |
| Sprite Renderer          | No               | No               |
| Decal Projectors         | No               | No               |
| TextMesh                 | No               | No               |

## Occlusion view compatibility

The following table shows which components Burst occlusion culling supports as views:

| Component                | View support |
| ------------------------ | ------------ |
| Camera                   | Yes          |
| Light (for shadows)      | Yes          |
| Reflection Probe         | Yes          |
| Planar Reflection Probe  | Yes          |


## Feature compatibility

Burst occlusion culling doesn't support the following:

* [Mesh deformations](mesh_deformations.md).
* Concurrent usage with Unity's [built-in occlusion culling system](xref:OcclusionCulling).

## Additional resources

* [Burst occlusion culling overview](burst-occlusion-culling-overview.md)