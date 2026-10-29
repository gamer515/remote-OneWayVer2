using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
public static class MenuWalkerSetup
{
    public static string Apply()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(Application.isPlaying || scene.path!="Assets/Scenes/MainMenuScene.unity")throw new Exception("MainMenuScene edit mode required");
        const string dir="Assets/MainMenu/Player";
        if(!AssetDatabase.IsValidFolder(dir))AssetDatabase.CreateFolder("Assets/MainMenu","Player");
        var source=AssetDatabase.LoadAllAssetsAtPath("Assets/UnityAssets/KnightAsset/KnightCharacter.fbx").OfType<AnimationClip>().First(c=>c.name=="HumanArmature|Walking");
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(dir+"/MenuWalk.anim");
        if(clip==null){clip=UnityEngine.Object.Instantiate(source);clip.name="MenuWalk";var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;settings.loopBlend=true;AnimationUtility.SetAnimationClipSettings(clip,settings);AssetDatabase.CreateAsset(clip,dir+"/MenuWalk.anim");}
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(dir+"/MenuWalk.controller");
        if(controller==null){controller=AnimatorController.CreateAnimatorControllerAtPath(dir+"/MenuWalk.controller");var state=controller.layers[0].stateMachine.AddState("Walk");state.motion=clip;controller.layers[0].stateMachine.defaultState=state;EditorUtility.SetDirty(controller);AssetDatabase.SaveAssetIfDirty(controller);}
        var root=GameObject.Find("Menu Coast").transform;
        var existing=root.Find("Menu Player");if(existing!=null)throw new Exception("Menu Player already exists");
        var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3DBattle/Prefabs/BattleKnight.prefab"));
        Undo.RegisterCreatedObjectUndo(go,"Create menu walker");PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        go.name="Menu Player";go.transform.SetParent(root,false);
        foreach(var script in go.GetComponentsInChildren<MonoBehaviour>(true))UnityEngine.Object.DestroyImmediate(script);
        foreach(var collider in go.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(collider);
        foreach(var body in go.GetComponentsInChildren<Rigidbody>(true))UnityEngine.Object.DestroyImmediate(body);
        foreach(var t in go.GetComponentsInChildren<Transform>(true))if(t.name.IndexOf("Sword",StringComparison.OrdinalIgnoreCase)>=0 || t.name.IndexOf("Shield",StringComparison.OrdinalIgnoreCase)>=0)t.gameObject.SetActive(false);
        var animator=go.GetComponentInChildren<Animator>(true);animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        go.transform.rotation=Quaternion.LookRotation(Vector3.forward);go.transform.localScale=Vector3.one;
        clip.SampleAnimation(animator.gameObject,0);
        var renderers=go.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
        go.transform.localScale*=3.7f/bounds.size.y;
        var camera=Camera.main;var ray=camera.ViewportPointToRay(new Vector3(.215f,.57f,0));var plane=new Plane(Vector3.up,new Vector3(0,1.55f,0));float distance;if(!plane.Raycast(ray,out distance))throw new Exception("Camera misses ground");
        go.transform.position=ray.GetPoint(distance);
        bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);go.transform.position+=Vector3.up*(1.55f-bounds.min.y);
        var anchor=root.Find("Player Placement");if(anchor!=null){Undo.RecordObject(anchor,"Menu player position");anchor.position=go.transform.position;anchor.rotation=go.transform.rotation;}
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject=go;
        return "Walking clip="+source.name+"; animator="+animator.name+"; player position="+go.transform.position+"; scale="+go.transform.localScale;
    }
    public static string Sample()
    {
        var go=GameObject.Find("Menu Coast/Menu Player");var animator=go.GetComponentInChildren<Animator>();
        AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/MainMenu/Player/MenuWalk.anim").SampleAnimation(animator.gameObject,.3f);
        return "Sampled walk at 0.3 seconds";
    }
}
