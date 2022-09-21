using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.Entities.Graphics
{
    /// <summary>
    /// Represents settings that control when to render a given entity.
    /// </summary>
    /// <remarks>
    /// For example, you can set the layermask of the entity and also set whether to render an entity in shadow maps or motion passes.
    /// </remarks>
    public struct RenderFilterSettings : ISharedComponentData, IEquatable<RenderFilterSettings>
    {
        /// <summary>
        /// The [LayerMask](https://docs.unity3d.com/ScriptReference/LayerMask.html) index.
        /// </summary>
        /// <remarks>
        /// For entities that Unity converts from GameObjects, this value is the same as the Layer setting of the source
        /// GameObject.
        /// </remarks>
        [LayerField] public int Layer;

        /// <summary>
        /// The rendering layer the entity is part of.
        /// </summary>
        /// <remarks>
        /// This value corresponds to <see cref="Renderer.renderingLayerMask"/>.
        /// </remarks>
        public uint RenderingLayerMask;

        /// <summary>
        /// Specifies what kinds of motion vectors to generate for the entity, if any.
        /// </summary>
        /// <remarks>
        /// This value corresponds to <see cref="Renderer.motionVectorGenerationMode"/>.
        ///
        /// This value only affects render pipelines that use motion vectors.
        /// </remarks>
        public MotionVectorGenerationMode MotionMode;

        /// <summary>
        /// Specifies how the entity should cast shadows.
        /// </summary>
        /// <remarks>
        /// For entities that Unity converts from GameObjects, this value is the same as the Cast Shadows property of the source
        /// Mesh Renderer component.
        /// For more information, refer to [ShadowCastingMode](https://docs.unity3d.com/ScriptReference/Rendering.ShadowCastingMode.html).
        /// </remarks>
        public ShadowCastingMode ShadowCastingMode;

        /// <summary>
        /// Indicates whether to cast shadows onto the entity.
        /// </summary>
        /// <remarks>
        /// For entities that Unity converts from GameObjects, this value is the same as the Receive Shadows property of the source
        /// Mesh Renderer component.
        /// This value only affects [Progressive Lightmappers](https://docs.unity3d.com/Manual/ProgressiveLightmapper.html).
        /// </remarks>
        public bool ReceiveShadows;

        /// <summary>
        /// Indicates whether the entity is a static shadow caster.
        /// </summary>
        /// <remarks>
        /// This value is important to the BatchRenderGroup.
        /// </remarks>
        public bool StaticShadowCaster;

        /// <summary>
        /// Returns a new default instance of RenderFilterSettings.
        /// </summary>
        public static RenderFilterSettings Default => new RenderFilterSettings
        {
            Layer = 0,
            RenderingLayerMask = 0xffffffff,
            MotionMode = MotionVectorGenerationMode.Object,
            ShadowCastingMode = ShadowCastingMode.On,
            ReceiveShadows = true,
            StaticShadowCaster = false,
        };

        /// <summary>
        /// Indicates whether the motion mode for the current pass is not camera.
        /// </summary>
        public bool IsInMotionPass =>
            MotionMode != MotionVectorGenerationMode.Camera;

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (obj is RenderFilterSettings)
                return Equals((RenderFilterSettings) obj);

            return false;
        }

        /// <inheritdoc/>
        public bool Equals(RenderFilterSettings other)
        {
            return Layer == other.Layer && RenderingLayerMask == other.RenderingLayerMask && MotionMode == other.MotionMode && ShadowCastingMode == other.ShadowCastingMode && ReceiveShadows == other.ReceiveShadows && StaticShadowCaster == other.StaticShadowCaster;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new xxHash3.StreamingState(true);
            hash.Update(Layer);
            hash.Update(RenderingLayerMask);
            hash.Update(MotionMode);
            hash.Update(ShadowCastingMode);
            hash.Update(ReceiveShadows);
            hash.Update(StaticShadowCaster);
            return (int)hash.DigestHash64().x;
        }

        /// <inheritdoc/>
        public static bool operator ==(RenderFilterSettings left, RenderFilterSettings right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(RenderFilterSettings left, RenderFilterSettings right)
        {
            return !left.Equals(right);
        }
    }
}
