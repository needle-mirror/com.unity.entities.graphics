## Mesh deformations
This page describes how to deform meshes using skinning and blendshapes, similar to what the [SkinnedMeshRenderer](https://docs.unity3d.com/Manual/class-SkinnedMeshRenderer.html) does. Generally, you want to use this in combination with DOTS Animation. For samples of setups and usage of the systems, see [DOTS Animation Samples](https://github.com/Unity-Technologies/Unity.Animation.Samples/blob/master/README.md). 


## Disclaimer
This version is highly experimental. This means that it is not yet ready for production use and parts of the implementation will change.
## Setup

To use mesh deformations in your Unity Project, you need to correctly set up:

- Your [Unity Project](#project-setup)
- A [material to use with the deformed mesh](#material-setup)
- A [mesh to apply the material to](#mesh-setup).

### Project setup

Follow these steps to set your Unity Project up to support mesh deformation.

1. Make sure your Unity Project uses [Scriptable Render Pipeline](https://docs.unity3d.com/Manual/ScriptableRenderPipeline.html) (SRP) version 7.x or higher.
2. Make sure your Unity Project uses Entities Graphics
3. If you intend to use Compute Deformation (required for blendshapes), go to Project Settings (menu: **Edit > Project Settings**) and, in the Player section, add the `ENABLE_COMPUTE_DEFORMATIONS` define to **Scripting Define Symbols**.

### Material setup

Follow these steps to create a material that Entities Graphics can use to render mesh deformations:

1. Create a Shader Graph and open it. You can use any Shader Graph from the High Definition Render Pipeline (HDRP) or the Universal Render Pipeline (URP).
2. Add either the [Compute Deformation](https://docs.unity3d.com/Packages/com.unity.shadergraph@latest?subfolder=/manual/Compute-Deformation-Node.html) or [Linear Blend Skinning](https://docs.unity3d.com/Packages/com.unity.shadergraph@latest?subfolder=/manual/Linear-Blend-Skinning-Node.html) node to the Shader Graph.
3. Connect the position, normal, and tangent outputs of the node to the vertex position, normal, and tangent slots in the master node respectively. Save the Shader Graph.

1. Create a material that uses the new Shader Graph. To do this, right-click on the Shader Graph asset and click **Create > Material**.
2. If you already have a mesh set up, assign the material to all material slots on the SkinnedMeshRenderer. If not, see [Mesh setup](#mesh-setup).
3. Now Entities Graphics is able to fetch the result of the deformation when the Entity renders. However, for the mesh to actually deform, you must set it up correctly. For information on how to do this, see [Mesh setup](#mesh-setup).

### Mesh setup

Follow these steps to set up a mesh that Entities Graphics can animate using mesh deformation:

1. Make sure your GameObject or Prefab is suitable for mesh deformations. This means it uses the SkinnedMeshRenderer component and not the MeshRenderer component. Furthermore, the mesh you assign to a SkinnedMeshRenderer needs to have blendshapes and/or a valid bind pose and skin weights. If it does not, an error appears in the SkinnedMeshRenderer component Inspector.
2. Assign the material you created in [Material setup](#material-setup) to all material slots on the SkinnedMeshRenderer(s).
3. When Unity converts the GameObject or Prefab into an entity, it adds the correct deformation components. Furthermore, the deformation systems dispatch and apply the deformations to the mesh. Note that to create motion you should either use Dots Animation or write to the SkinMatrix and BlendShapeWeights components directly.

### Vertex shader skinning
Skins the mesh on the GPU in the vertex shader. 
#### Features
- Linear blend skinning with four influences per vertex.
- Does not support blendshapes.
#### Requirements
- Unity 2019.3b11 or newer (recommended)
- Entities Graphics 0.5.0 or higher (recommended)
- SRP version 7.x.x or higher (recommended)


### Compute shader deformation
Applies mesh deformations on the GPU using compute shaders. 
#### Features
- Linear blend skinning, supports up to 255 sparse influences per vertex 
- Supports sparse blendshapes
#### Requirements
- Add the `ENABLE_COMPUTE_DEFORMATIONS` define to **Scripting Define Symbols** in your Project Settings (menu: **Edit > Project Settings > Player**)
- Unity 2020.1.0b6 or higher (recommended)
- Entities Graphics 0.5.0 or higher (recommended)
- SRP version 9.x.x or higher (recommended)

## Known limitations
- Wire frame mode and other debug modes do not display mesh deformations.
- Render bounds are not resized or transformed based on the mesh deformations.
- No frustum or occlusion culling, Unity processes mesh deformation for everything that uses it in the scene.
- Visual glitches may appear on the first frame.
- Live link is still untested with many of the features.
- Deformed meshes can disappear or show in their bind pose when Unity renders them as GameObjects.
- Compute deformation performance varies based on GPU.
