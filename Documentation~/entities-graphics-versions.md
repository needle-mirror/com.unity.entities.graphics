# Entities Graphics feature matrix

Entities Graphics is in active development. The goal is to reach full High Definition Render Pipeline (HDRP) and Universal Render Pipeline (URP) feature coverage. The only exceptions are select old deprecated features in each render pipeline that now have modern replacements.

The following table compares Entities Graphics feature support between HDRP and URP:

| **Feature**                     | **URP**      | **HDRP**         |
| ------------------------------- | ------------ | ---------------- |
| **Material property overrides** | Yes          | Yes              |
| **Built-in property overrides** | Yes          | Yes              |
| **Shader Graph**                | Yes          | Yes              |
| **Lit shader**                  | Yes          | Yes              |
| **Unlit shader**                | Yes          | Yes              |
| **Decal shader**                | N/A          | Yes              |
| **LayeredLit shader**           | N/A          | Yes              |
| **RenderLayer**                 | Yes          | Yes              |
| **TransformParams**             | Yes          | Yes              |
| **DisableRendering**            | Yes          | Yes              |
| **Motion blur**                 | Yes          | Yes              |
| **Temporal AA**                 | N/A          | Yes              |
| **Sun light**                   | Yes          | Yes              |
| **Point + spot lights**         | 2022         | Yes              |
| **Ambient probe**               | Yes          | Yes              |
| **Light probes**                | Yes          | Yes              |
| **Reflection probes**           | 2022         | Yes              |
| **Lightmaps**                   | Experimental | Experimental     |
| **LOD crossfade**               | 2021         | 2021             |
| **Custom pass shader override** | Yes          | Yes              |
| **Sorted Transparencies**       | Yes          | Yes              |
| **Dynamic Occlusion culling**   | Experimental | Experimental     |
| **Skinning & deformations**     | Experimental | Experimental     |
