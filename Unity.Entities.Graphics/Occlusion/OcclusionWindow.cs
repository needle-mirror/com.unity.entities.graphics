#if UNITY_EDITOR && ENABLE_UNITY_OCCLUSION && (HDRP_10_0_0_OR_NEWER || URP_10_0_0_OR_NEWER)

using System.Linq;
using UnityEditor;
using UnityEngine;
using Unity.Rendering.Occlusion;
using Unity.Entities;
using Unity.Rendering;

public class OcclusionWindow : EditorWindow
{
    enum VisibilityFilter
    {
        None,
        Occluders,
        Occludees,
    }

    private VisibilityFilter _visibilityFilter = VisibilityFilter.None;
    bool occlusionEnabled;

    // Add menu item named "My Window" to the Window menu
    public static void ShowWindow()
    {
        //Show existing window instance. If one doesn't exist, make one.
        EditorWindow.GetWindow(typeof(OcclusionWindow));
    }

    void OnGUI()
    {
        if (World.DefaultGameObjectInjectionWorld == null)
        {
            return;
        }

        var occlusionCulling = World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<EntitiesGraphicsSystem>().OcclusionCulling;

        if (occlusionCulling == null)
        {
            return;
        }

        GUILayout.Space(5);
        GUILayout.Label ("Options", EditorStyles.boldLabel);

        occlusionCulling.IsEnabled = GUILayout.Toggle(occlusionCulling.IsEnabled, "Enable Occlusion");

        GUILayout.Space(20);
        GUILayout.Label ("Tools", EditorStyles.boldLabel);
        GUILayout.Space(5);

        VisibilityFilter currentVis = _visibilityFilter;
        if (GUILayout.Toggle(_visibilityFilter == VisibilityFilter.Occluders, "Show Only Ocludders"))
        {
            var list = FindObjectsOfType<Occluder>().Select(x => x.gameObject).ToArray();
            ScriptableSingleton<SceneVisibilityManager>.instance.Isolate(list, true);
            _visibilityFilter = VisibilityFilter.Occluders;
        }
        else
        {
            if (_visibilityFilter == VisibilityFilter.Occluders)
            {
                ScriptableSingleton<SceneVisibilityManager>.instance.ExitIsolation();
                _visibilityFilter = VisibilityFilter.None;
            }
        }

        if (GUILayout.Button("Select Occluders"))
        {
            Selection.objects = FindObjectsOfType<Occluder>().Select(x => x.gameObject).ToArray();
        }

        if (GUILayout.Button("Remove Occluders from Selected"))
        {
            foreach (var go in Selection.gameObjects)
            {
                foreach (var o in go.GetComponents<Occluder>())
                {
                    DestroyImmediate(o);
                }
            }
        }
    }
}
#endif
