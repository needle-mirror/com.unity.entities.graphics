#if UNITY_EDITOR && ENABLE_UNITY_OCCLUSION && (HDRP_10_0_0_OR_NEWER || URP_10_0_0_OR_NEWER)

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Entities;

namespace Unity.Rendering.Occlusion
{
    public class OcclusionBrowseWindow : EditorWindow
    {
        public static bool IsVisible;

        [MenuItem("Occlusion/Browse")]
        public static void ShowMenuItem()
        {
            OcclusionBrowseWindow wnd = GetWindow<OcclusionBrowseWindow>();
            wnd.titleContent = new GUIContent("Occlusion Browser");
        }

        internal StyleSheet StyleSheet
        {
            get
            {
                return AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.unity.entities.graphics/Unity.Entities.Graphics/Occlusion/OcclusionBrowseWindow.uss");
            }
        }

        readonly int kMargin = 10;
        readonly int kPadding = 10;
        readonly int kBoxSize = 200;

        public void OnInspectorUpdate()
        {
            rootVisualElement.Clear();
            CreateGUI();
        }

        private void OnBecameVisible()
        {
            IsVisible = true;
        }

        private void OnBecameInvisible()
        {
            IsVisible = false;
        }

        public void CreateGUI()
        {
            var root = this.rootVisualElement;
            root.Clear();

            var boxes = new VisualElement()
            {
                style =
                {
                    marginLeft = kMargin,
                    marginTop = kMargin,
                    marginRight = kMargin,
                    marginBottom = kMargin,
                    backgroundColor = Color.grey,
                    paddingLeft = kPadding,
                    paddingTop = kPadding,
                    paddingRight = kPadding,
                    paddingBottom = kPadding,
                    alignSelf = Align.FlexStart,
                    flexShrink = 0f,
                    flexWrap = Wrap.Wrap,
                    flexDirection = FlexDirection.Row // makes the container horizontal
                }
            };

            root.Add(boxes);
            boxes.StretchToParentSize();

            if (World.DefaultGameObjectInjectionWorld == null)
            {
                return;
            }
            var entitiesGraphicsSystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<EntitiesGraphicsSystem>();

            foreach (var pair in entitiesGraphicsSystem.OcclusionCulling.BufferGroups)
            {
                 
                var id = (int)(pair.Key & 0xffffffff);
                var slice = (int)(pair.Key >> 32);
                var obj = (Behaviour)EditorUtility.InstanceIDToObject(id);
                Texture2D gpuDepth = pair.Value.GetVisualizationTexture();

                if (!obj || !obj.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var background = new StyleBackground(gpuDepth);

                // inform layout system of desired width for each box
                boxes.Add(new Button()
                {
                    text = obj.name + "\n[Slice " + slice.ToString() + "]\nid: " + $"0x{id:x8}",
                    style =
                    {
                        width = kBoxSize,
                        height = kBoxSize,
                        backgroundImage = background,
                    }
                });
            }
        }
    }
}
#endif
