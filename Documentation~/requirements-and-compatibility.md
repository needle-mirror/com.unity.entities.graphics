# Requirements and compatibility

This page contains information on system requirements and compatibility of the Entities Graphics package.

## Render pipeline compatibility

Entities Graphics requires a Scriptable Render Pipeline (SRP) to function.

| **Render pipeline**                        | **Compatibility**                                          |
| ------------------------------------------ | ---------------------------------------------------------- |
| **Built-in Render Pipeline**               | Not supported                                              |
| **High Definition Render Pipeline (HDRP)** | HDRP version 13.0.0 and above, with Unity 2022.1 and above |
| **Universal Render Pipeline (URP)**        | URP version 13.0.0 and above, with Unity 2022.1 and above  |

For more information about the supported feature set, see [Entities Graphics feature matrix](entities-graphics-versions.md).

## Unity Player system requirements

This section describes the Entities Graphics package’s target platform requirements. For platforms or use cases not covered in this section, general system requirements for the Unity Player apply.

For more information, see [System requirements for Unity](https://docs.unity3d.com/Manual/system-requirements.html).

Entities Graphics is not yet tested or supported on [XR](https://docs.unity3d.com/Manual/XR.html) devices. XR support is intended in a later version.

Entities Graphics does not support ray-tracing (DXR). Ray-tracing support is intended in a later version.

## DOTS feature compatibility

Entities Graphics does not support multiple DOTS [Worlds](https://docs.unity3d.com/Packages/com.unity.entities@latest?subfolder=/manual/world.html). Limited support for multiple Worlds is intended in a later version. The current plan is to add support for creating multiple rendering systems, one per renderable World, but then only have one World active for rendering at once.
