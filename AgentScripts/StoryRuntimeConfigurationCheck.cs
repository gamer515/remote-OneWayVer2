using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

// Read-only. Never prints API credentials or creates a client.
public static class StoryRuntimeConfigurationCheck
{
    public static object CheckDecision()
    {
        var scene=EditorSceneManager.OpenPreviewScene("Assets/Scenes/DecisionScene.unity");
        try
        {
            var roots=scene.GetRootGameObjects();
            var relays=roots.SelectMany(root=>root.GetComponentsInChildren<StoryRelayManager>(true)).ToArray();
            var clients=roots.SelectMany(root=>root.GetComponentsInChildren<AIAPIClient>(true)).ToArray();
            return new{scene=scene.path,relayCount=relays.Length,
                promptsConnected=relays.All(relay=>new SerializedObject(relay).FindProperty("promptData").objectReferenceValue!=null),
                apiClientCount=clients.Length,
                clientPlacement=clients.Select(client=>new{objectName=client.name,
                    rootObject=client.transform.parent==null,
                    components=client.GetComponents<Component>().Where(component=>component!=null)
                        .Select(component=>component.GetType().Name).ToArray()}).ToArray(),
                keyConfigured=clients.Any(client=>{
                    var key=new SerializedObject(client).FindProperty("apiKey").stringValue;
                    return !string.IsNullOrWhiteSpace(key)&&key!="YOUR_API_KEY";
                }),sceneSaved=false,externalRequestsMade=false};
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }

    public static object Check()
    {
        var relays=UnityEngine.Object.FindObjectsByType<StoryRelayManager>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        var clients=UnityEngine.Object.FindObjectsByType<AIAPIClient>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        return new{
            scene=SceneManager.GetActiveScene().path,
            relayCount=relays.Length,
            relays=relays.Select(relay=>new{
                active=relay.gameObject.activeInHierarchy,
                promptConnected=new SerializedObject(relay).FindProperty("promptData").objectReferenceValue!=null
            }).ToArray(),
            apiClientCount=clients.Length,
            clients=clients.Select(client=>{
                var key=new SerializedObject(client).FindProperty("apiKey").stringValue;
                return new{active=client.gameObject.activeInHierarchy,
                    keyConfigured=!string.IsNullOrWhiteSpace(key)&&key!="YOUR_API_KEY"};
            }).ToArray(),
            mainMenuInBuild=EditorBuildSettings.scenes.Any(scene=>scene.enabled&&scene.path.EndsWith("/MainMenuScene.unity",StringComparison.Ordinal)),
            externalRequestsMade=false
        };
    }
}
