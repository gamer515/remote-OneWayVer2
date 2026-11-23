using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ConfigureMenuButtonFeedback
{
    const string ScenePath = "Assets/Scenes/MainMenuScene.unity";
    static Transform Find(Scene scene, string name) => scene.GetRootGameObjects()
        .SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Single(t => t.name == name);
    static object[] Clicks(Button button) => Enumerable.Range(0, button.onClick.GetPersistentEventCount())
        .Select(i => (object)new { target = button.onClick.GetPersistentTarget(i).GetInstanceID(),
            method = button.onClick.GetPersistentMethodName(i), state = button.onClick.GetPersistentListenerState(i).ToString() }).ToArray();

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Edit mode required.");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) throw new Exception("MainMenuScene must be open.");
        Directory.CreateDirectory("Temp/MenuButtonFeedback");
        File.Copy(ScenePath,"Temp/MenuButtonFeedback/MainMenuScene.before.unity",true);
        var results = new System.Collections.Generic.List<object>();
        foreach (var name in new[] { "Start", "Quit" })
        {
            var root = Find(scene, name + "Button");
            var button = root.GetComponent<Button>();
            var image = root.Find(name + "Image").GetComponent<Image>();
            var clicks = JsonConvert.SerializeObject(Clicks(button));
            var rootBefore = EditorJsonUtility.ToJson(root);
            Undo.RecordObjects(new UnityEngine.Object[] { button, image }, "Add menu button press feedback");
            var feedback = button.GetComponent<MainMenuButtonFeedback>();
            if (feedback == null) feedback = Undo.AddComponent<MainMenuButtonFeedback>(button.gameObject);
            else Undo.RecordObject(feedback,"Configure menu button press feedback");
            feedback.visual = image;
            feedback.hoverScale = 1.03f;
            feedback.pressedScale = .94f;
            feedback.pressOffset = new Vector2(0,-3);
            feedback.pressedBrightness = .85f;
            feedback.pressDuration = .08f;
            feedback.releaseDuration = .12f;
            button.transition = Selectable.Transition.None;
            // Only the stationary parent handles raycasts, including near the image's edges.
            image.raycastTarget = false;
            foreach (var obj in new UnityEngine.Object[] { button, image, feedback }) EditorUtility.SetDirty(obj);
            if (JsonConvert.SerializeObject(Clicks(button)) != clicks || EditorJsonUtility.ToJson(root) != rootBefore)
                throw new Exception("Button action or hit-area transform changed: " + name);
            results.Add(new { button = root.name, visual = image.name, feedback.hoverScale, feedback.pressedScale,
                offset = new[] { feedback.pressOffset.x, feedback.pressOffset.y }, feedback.pressedBrightness,
                feedback.pressDuration, feedback.releaseDuration, clickActionsUnchanged = true, hitAreaUnchanged = true });
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Scene save failed.");
        return JsonConvert.SerializeObject(new { saved=scene.path, buttons=results }, Formatting.Indented);
    }

    public static string EnterPaused()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Edit mode required.");
        if (SceneManager.GetActiveScene().path != ScenePath) throw new Exception("MainMenuScene required.");
        EditorApplication.isPaused = true;
        EditorApplication.EnterPlaymode();
        return "Starting paused MainMenu verification.";
    }

    public static string Finish()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play Mode first.");
        EditorApplication.isPaused = false;
        return JsonConvert.SerializeObject(new { paused=EditorApplication.isPaused, scenes=Enumerable.Range(0,SceneManager.sceneCount)
            .Select(SceneManager.GetSceneAt).Select(s=>new{s.path,s.isDirty}).ToArray() });
    }
}
