using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class EncounterDistanceInspectorCheck
{
    public static object Check()
    {
        var manager = UnityEngine.Object.FindObjectsByType<DecisionManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .SingleOrDefault(m => m.gameObject.scene.path == "Assets/Scenes/DecisionScene.unity");
        if (manager != null) return Verify(manager);
        var preview = EditorSceneManager.OpenPreviewScene("Assets/Scenes/DecisionScene.unity");
        try
        {
            manager = preview.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<DecisionManager>(true)).Single();
            return Verify(manager);
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    static object Verify(DecisionManager manager)
    {
        using (var serialized = new SerializedObject(manager))
        {
            var property = serialized.FindProperty("encounterStopDistance");
            if (property == null || property.propertyType != SerializedPropertyType.Float)
                throw new Exception("Encounter distance is not exposed to the Inspector");
            var field = typeof(DecisionManager).GetField("encounterStopDistance", BindingFlags.Instance | BindingFlags.NonPublic);
            var range = field.GetCustomAttribute<RangeAttribute>();
            if (range == null || range.min != 0.5f || range.max != 10f)
                throw new Exception("Distance slider range is incorrect");
            if (Mathf.Abs(property.floatValue - 4f) > 0.0001f)
                throw new Exception("Existing encounter distance did not remain at 4 units");
            return new { component = manager.name, label = property.displayName, value = property.floatValue, min = range.min, max = range.max };
        }
    }
}
