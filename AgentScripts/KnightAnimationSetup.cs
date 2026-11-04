using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;

// Maintained authoring and validation entry points; invoke through Unity Pipeline run_script.
public static class KnightAnimationSetup
{
    const string ScenePath = "Assets/Scenes/DecisionScene.unity";
    const string AnimRoot = "Assets/Decision_YYS/Animation/MaximoAni/";
    const string ControllerPath = "Assets/Decision_YYS/Animation/KnightPresentation.controller";
    const string PrefabPath = "Assets/Decision_YYS/Prafabs/People/Knight_For_Ani.prefab";
    static AnimationClip Clip(string name) => AssetDatabase.FindAssets("t:AnimationClip", new[]{AnimRoot.TrimEnd('/')})
        .Select(AssetDatabase.GUIDToAssetPath).Distinct().SelectMany(AssetDatabase.LoadAllAssetsAtPath)
        .OfType<AnimationClip>().First(c=>c.name==name);
    static void Ref(UnityEngine.Object component, string field, UnityEngine.Object value)
    {
        var so=new SerializedObject(component); so.FindProperty(field).objectReferenceValue=value; so.ApplyModifiedPropertiesWithoutUndo();
    }
    public static object Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded) scene=EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var transforms=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
        var preview=transforms.SelectMany(t=>t.GetComponents<SeatedPlayerPreview>()).Single();
        var previewSo=new SerializedObject(preview);
        var seated=(GameObject)previewSo.FindProperty("seatedPlayer").objectReferenceValue;
        var setup=(TutorialMiniGameController)previewSo.FindProperty("miniGames").objectReferenceValue;
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller==null) controller=AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var machine=controller.layers[0].stateMachine;
        foreach(var state in machine.states) machine.RemoveState(state.state);
        string[] names={"Sitting Idle","Sitting And Pointing","Sitting Rubbing Arm","Knight_DeskDismount_Smooth","Walking","Warrior Idle"};
        var states=names.Select((n,i)=>{var s=machine.AddState(n=="Knight_DeskDismount_Smooth"?"Desk Dismount":n,new Vector3(200+(i%3)*250,(i/3)*140));s.motion=Clip(n);s.writeDefaultValues=false;return s;}).ToArray();
        machine.defaultState=states[0];
        foreach(var state in states.Skip(1).Take(2))
        {
            var transition=state.AddTransition(states[0]); transition.hasExitTime=true;transition.exitTime=1f;
            transition.hasFixedDuration=true;transition.duration=0.3f;
        }
        // Existing duel hit-reaction code resumes this state by name.
        var legacy=machine.AddState("HumanArmature_New_Idle_swordRight"); legacy.motion=Clip("Warrior Idle");legacy.writeDefaultValues=false;
        var attacked=machine.AddState("HumanArmature_Attacked");
        attacked.motion=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/UnityAssets/KnightAsset/KnightAnim/HumanArmature_Attacked.anim");
        attacked.writeDefaultValues=false;
        EditorUtility.SetDirty(controller);
        var animator=seated.GetComponent<Animator>();
        var avatarPath="Assets/Decision_YYS/Animation/KnightForAniAvatar.asset";
        var avatar=AssetDatabase.LoadAssetAtPath<Avatar>(avatarPath);
        if(avatar==null) {avatar=UnityEngine.Object.Instantiate(animator.avatar);avatar.name="KnightForAniAvatar";AssetDatabase.CreateAsset(avatar,avatarPath);}
        if(!avatar.isValid||!avatar.isHuman)throw new InvalidOperationException("Invalid target Avatar.");
        animator.avatar=avatar;animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        // Knight_Center is an authoring marker, not a second copy of the knight mesh.
        var center=seated.transform.Find("Knight_Center");
        if(center!=null) {var renderer=center.GetComponent<Renderer>();if(renderer!=null)renderer.enabled=false;}
        var presentation=seated.GetComponent<KnightPresentation>();
        if(presentation==null)presentation=seated.AddComponent<KnightPresentation>();
        Ref(presentation,"deskDismount",Clip("Knight_DeskDismount_Smooth"));
        var sample=UnityEngine.Object.Instantiate(seated);
        sample.transform.SetParent(null);sample.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);sample.transform.localScale=Vector3.one;
        var sampleAnimator=sample.GetComponent<Animator>();sampleAnimator.enabled=true;sampleAnimator.Rebind();
        Vector3 Hips(string state,float time)
        {
            sampleAnimator.Play(state,0,time);sampleAnimator.Update(0f);
            return sample.transform.InverseTransformPoint(sampleAnimator.GetBoneTransform(HumanBodyBones.Hips).position);
        }
        Vector3 startOffset=Hips("Sitting Idle",0f)-Hips("Desk Dismount",0f);
        Vector3 endOffset=Hips("Walking",0f)-Hips("Desk Dismount",1f);
        UnityEngine.Object.DestroyImmediate(sample);
        var presentationSo=new SerializedObject(presentation);
        presentationSo.FindProperty("dismountStartPoseOffset").vector3Value=startOffset;
        presentationSo.FindProperty("dismountEndPoseOffset").vector3Value=endOffset;
        presentationSo.ApplyModifiedPropertiesWithoutUndo();
        Vector3 position=seated.transform.position;Quaternion rotation=seated.transform.rotation;Vector3 scale=seated.transform.localScale;
        if(PrefabUtility.IsPartOfPrefabInstance(seated))PrefabUtility.UnpackPrefabInstance(seated,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        seated.name="Knight_For_Ani";
        seated.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);seated.transform.localScale=Vector3.one;
        var prefab=PrefabUtility.SaveAsPrefabAssetAndConnect(seated,PrefabPath,InteractionMode.AutomatedAction);
        seated.transform.SetPositionAndRotation(position,rotation);seated.transform.localScale=scale;
        Ref(setup,"knightTargetPrefab",prefab);Ref(setup,"seatedKnightPose",seated.transform);
        // Persist the marked inspector pose on the source anchor; generated DuelNpcHand copies it.
        var references=transforms.SelectMany(t=>t.GetComponents<DuelAuthoringReferences>()).Single();
        var referencesSo=new SerializedObject(references);
        var hand=(Transform)referencesSo.FindProperty("npcHandStartPose").objectReferenceValue;
        hand.SetPositionAndRotation(new Vector3(-5.36f,-7.7199f,-2f),Quaternion.Euler(-73.519f,170.87f,-21.078f));
        EditorUtility.SetDirty(hand);EditorUtility.SetDirty(animator);EditorUtility.SetDirty(setup);
        EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
        return new {prefab=PrefabPath,avatar=avatar.isHuman,states=machine.states.Select(s=>s.state.name).ToArray(),seated=position.ToString("F3"),hand=hand.position.ToString("F4")};
    }
    static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
    public static object ConfigureImports()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before authoring.");
        var existingAvatar = AssetDatabase.LoadAssetAtPath<Avatar>("Assets/Decision_YYS/Animation/SeatedKnightAvatar.asset");
        var results = new System.Collections.Generic.List<object>();
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[]{AnimRoot.TrimEnd('/')}))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (path.EndsWith("Knight_DeskDismount.fbx"))
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var description = existingAvatar.humanDescription;
                var corrected = description.skeleton.ToDictionary(b=>b.name, b=>b);
                description.skeleton = model.GetComponentsInChildren<Transform>(true).Select(t=> {
                    SkeletonBone bone;
                    if (corrected.TryGetValue(t.name, out bone))
                    {
                        // Dismount was authored at 1.8 m: its armature scale is 32.15, not the target's 100.
                        // Keep the corrected neutral rotations/positions, but calibrate this Avatar at its own scale.
                        bone.scale = t.localScale;
                        return bone;
                    }
                    return new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale};
                }).ToArray();
                importer.humanDescription = description;
            }
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            var clips = importer.defaultClipAnimations;
            foreach(var clip in clips)
            {
                clip.loopTime = path.Contains("Sitting Idle") || path.Contains("Walking") || path.Contains("Warrior Idle");
                clip.loopPose = clip.loopTime;
                clip.lockRootRotation = true;
                // Extract authored forward travel; the entrance component owns world movement.
                clip.lockRootPositionXZ = !(path.EndsWith("Knight_DeskDismount.fbx") || path.Contains("@Walking"));
                clip.lockRootHeightY = !path.EndsWith("Knight_DeskDismount.fbx");
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionXZ = true;
                clip.keepOriginalPositionY = !path.EndsWith("Knight_DeskDismount.fbx");
                clip.heightFromFeet = path.EndsWith("Knight_DeskDismount.fbx");
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            results.Add(new{path,valid=avatar!=null && avatar.isValid,human=avatar!=null && avatar.isHuman,
                clips=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).Select(c=>new{c.name,c.humanMotion,c.length}).ToArray()});
        }
        return results;
    }
    public static object Inspect()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var objects = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)).ToArray();
        return new {
            objects = objects.Where(t => t.name.Contains("Knight") || t.name.Contains("Pose") || t.name.Contains("Seated") || t.name.Contains("DuelNpc"))
                .Select(t => new {path=Path(t), pos=t.position.ToString("F3"), rot=t.eulerAngles.ToString("F2"), scale=t.lossyScale.ToString("F3"), active=t.gameObject.activeSelf,
                    components=t.GetComponents<Component>().Where(c=>c!=null).Select(c=>c.GetType().Name).ToArray()}).ToArray(),
            refs=objects.SelectMany(t=>t.GetComponents<MonoBehaviour>()).Where(c=>c is TutorialMiniGameController || c is SeatedPlayerPreview || c is DuelAuthoringReferences)
                .Select(c=>new {path=Path(c.transform), json=EditorJsonUtility.ToJson(c)}).ToArray(),
            assets=AssetDatabase.FindAssets("t:Model", new[]{AnimRoot.TrimEnd('/'),"Assets/UnityAssets/KnightAsset"})
                .Select(AssetDatabase.GUIDToAssetPath).Where(p=>p.Contains("Knight")).Select(p=> {
                    var model=AssetDatabase.LoadAssetAtPath<GameObject>(p); var importer=(ModelImporter)AssetImporter.GetAtPath(p);
                    return new {path=p,type=importer.animationType.ToString(),avatar=AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().Select(a=>new{name=a.name,valid=a.isValid,human=a.isHuman}).ToArray(),
                        clips=AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).Select(c=>new{name=c.name,length=c.length,human=c.humanMotion}).ToArray(),
                        bones=model.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.bones).Where(t=>t!=null).Select(t=>t.name).Distinct().ToArray()}; }).ToArray()
        };
    }
}
