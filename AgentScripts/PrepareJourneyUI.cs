using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using TMPro;

public static class PrepareJourneyUI
{
    static TextMeshProUGUI fontSource;
    static RectTransform Node(string name,Transform parent,Vector2 min,Vector2 max)
    {
        var go=new GameObject(name,typeof(RectTransform));var rt=(RectTransform)go.transform;rt.SetParent(parent,false);
        rt.anchorMin=min;rt.anchorMax=max;rt.offsetMin=rt.offsetMax=Vector2.zero;return rt;
    }
    static void Background(RectTransform rt,Color color,float border=1)
    {
        var bg=rt.gameObject.AddComponent<ChatBubbleGraphic>();bg.color=color;bg.borderWidth=border;bg.borderColor=new Color(.68f,.47f,.18f,1);bg.cornerRadius=4;bg.raycastTarget=false;
    }
    static TextMeshProUGUI Text(string name,Transform parent,Vector2 min,Vector2 max,string value,float size=10)
    {
        var rt=Node(name,parent,min,max);var t=rt.gameObject.AddComponent<TextMeshProUGUI>();t.font=fontSource.font;t.fontSharedMaterial=fontSource.fontSharedMaterial;
        t.text=value;t.fontSize=size;t.enableAutoSizing=false;t.color=new Color(.17f,.12f,.07f,1);t.alignment=TextAlignmentOptions.Center;t.raycastTarget=false;t.margin=new Vector4(5,3,5,3);return t;
    }
    static void Ref(SerializedObject data,string field,UnityEngine.Object value)=>data.FindProperty(field).objectReferenceValue=value;
    static void Refs(SerializedObject data,string field,IEnumerable<UnityEngine.Object> values)
    {
        var list=values.ToArray();var property=data.FindProperty(field);property.arraySize=list.Length;
        for(int i=0;i<list.Length;i++)property.GetArrayElementAtIndex(i).objectReferenceValue=list[i];
    }
    public static object Dialogue()
    {
        if(Application.isPlaying)throw new Exception("Edit mode required");
        var chat=UnityEngine.Object.FindFirstObjectByType<ChatHistoryUi>(FindObjectsInactive.Include);var old=new SerializedObject(chat);
        fontSource=(TextMeshProUGUI)old.FindProperty("messageTemplate").objectReferenceValue;
        var walking=UnityEngine.Object.FindObjectsByType<UnityEngine.UI.RawImage>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(r=>r.name=="Walking_View").rectTransform;
        if(walking.Find("FloatingDialogue")!=null)throw new Exception("Already authored; preserve current configuration");
        var root=Node("FloatingDialogue",walking,Vector2.zero,Vector2.one);var view=root.gameObject.AddComponent<FloatingDialogueView>();
        var area=Node("NpcSpeechArea",root,new Vector2(.52f,.74f),new Vector2(.95f,.96f));area.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var player=Node("PlayerChoiceArea",root,new Vector2(.32f,.055f),new Vector2(.88f,.19f));Background(player,new Color(.98f,.96f,.9f,1),2);
        var draft=Text("PlayerText",player,new Vector2(.02f,.05f),new Vector2(.86f,.95f),"");draft.alignment=TextAlignmentOptions.MidlineLeft;
        var confirm=Node("Confirm",player,new Vector2(.88f,.2f),new Vector2(.98f,.8f));Background(confirm,new Color(.79f,.7f,.43f,1));
        var button=confirm.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=confirm.GetComponent<ChatBubbleGraphic>();confirm.GetComponent<ChatBubbleGraphic>().raycastTarget=true;
        Text("Caption",confirm,Vector2.zero,Vector2.one,"확인");UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick,UnityEngine.Object.FindFirstObjectByType<DecisionManager>().OnJourneyYellowPressed);
        var data=new SerializedObject(view);Ref(data,"npcArea",area);Ref(data,"playerArea",player);Ref(data,"template",fontSource);Ref(data,"playerText",draft);Ref(data,"confirmButton",button);data.ApplyModifiedPropertiesWithoutUndo();
        Ref(old,"floatingView",view);old.ApplyModifiedPropertiesWithoutUndo();
        foreach(Transform child in chat.transform)child.gameObject.SetActive(false);
        var own=chat.GetComponent<UnityEngine.UI.Graphic>();if(own!=null)own.enabled=false;
        EditorSceneManager.MarkSceneDirty(chat.gameObject.scene);EditorSceneManager.SaveScene(chat.gameObject.scene);return new{font=10,opaque=true};
    }
    public static object Station()
    {
        if(Application.isPlaying)throw new Exception("Edit mode required");
        var chat=UnityEngine.Object.FindFirstObjectByType<ChatHistoryUi>(FindObjectsInactive.Include);fontSource=(TextMeshProUGUI)new SerializedObject(chat).FindProperty("messageTemplate").objectReferenceValue;
        var original=(RectTransform)chat.transform;if(original.parent.Find("LearningStation")!=null)throw new Exception("Station already exists");
        var root=Node("LearningStation",original.parent,original.anchorMin,original.anchorMax);root.pivot=original.pivot;root.sizeDelta=original.sizeDelta;root.anchoredPosition=original.anchoredPosition;
        Background(root,new Color(.29f,.16f,.07f,1),5);var station=root.gameObject.AddComponent<MineLearningStation>();
        var inside=Node("MachinePanel",root,new Vector2(.17f,.1f),new Vector2(.97f,.97f));Background(inside,new Color(.075f,.13f,.21f,1),3);
        Text("Title",inside,new Vector2(0,.88f),Vector2.one,"능력 학습",12).color=new Color(1,.88f,.56f);
        Color[] colors={new Color(.1f,.4f,.85f),new Color(.8f,.12f,.19f),new Color(.95f,.72f,.12f),new Color(.02f,.6f,.64f)};
        string[] names={"힘","속도","지능","매력"};var buttons=new List<UnityEngine.Object>();var balances=new List<UnityEngine.Object>();
        for(int i=0;i<4;i++)
        {
            float x=i*.25f;var count=Text("Balance_"+i,inside,new Vector2(x,.76f),new Vector2(x+.25f,.87f),"0",12);count.color=colors[i];balances.Add(count);
            var window=Node("Ability_"+i,inside,new Vector2(x+.015f,.44f),new Vector2(x+.235f,.75f));Background(window,new Color(.13f,.21f,.29f,1),2);
            var cover=Node("BookPreview",window,new Vector2(.18f,.3f),new Vector2(.82f,.8f));Background(cover,colors[i],1);
            var pages=Node("Pages",cover,new Vector2(.12f,.08f),new Vector2(.88f,.3f));Background(pages,new Color(.95f,.91f,.78f,1),0);
            var b=Node("Select_"+i,inside,new Vector2(x+.03f,.32f),new Vector2(x+.22f,.43f));Background(b,colors[i],2);b.GetComponent<ChatBubbleGraphic>().raycastTarget=true;
            var button=b.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=b.GetComponent<ChatBubbleGraphic>();buttons.Add(button);Text("Caption",b,Vector2.zero,Vector2.one,names[i]).color=Color.white;
        }
        var queue=Node("WaitingQueue",root,new Vector2(.025f,.18f),new Vector2(.145f,.85f));Background(queue,new Color(.1f,.08f,.1f,1),2);
        Text("QueueTitle",root,new Vector2(.02f,.86f),new Vector2(.15f,.97f),"대기").color=new Color(1,.87f,.5f);
        var active=Node("ActiveLearning",inside,new Vector2(.1f,.05f),new Vector2(.9f,.3f));Background(active,new Color(.85f,.76f,.54f,1),1);
        var status=Text("Status",root,new Vector2(.17f,.01f),new Vector2(.97f,.095f),"학습 준비");status.color=new Color(1,.88f,.56f);
        var chute=Node("DeliveryChannel",root,new Vector2(.025f,.045f),new Vector2(.145f,.18f));Background(chute,new Color(.1f,.07f,.08f,1),2);
        var route=new List<UnityEngine.Object>();foreach(var point in new[]{new Vector2(.085f,.25f),new Vector2(.085f,.08f),new Vector2(.38f,.08f),new Vector2(.58f,.24f)})
        {var p=Node("DeliveryPoint_"+route.Count,root,point,point);p.sizeDelta=Vector2.zero;route.Add(p);}
        var book=Node("BookTemplate",root,new Vector2(.5f,.5f),new Vector2(.5f,.5f));book.sizeDelta=new Vector2(60,24);book.gameObject.AddComponent<UnityEngine.UI.Image>().color=colors[0];
        var label=Text("Label",book,Vector2.zero,Vector2.one,"5.0초");label.color=Color.white;book.gameObject.SetActive(false);
        var data=new SerializedObject(station);Refs(data,"abilityButtons",buttons);Refs(data,"balances",balances);Refs(data,"deliveryPath",route);Ref(data,"status",status);Ref(data,"queueArea",queue);Ref(data,"activeArea",active);Ref(data,"bookTemplate",book);data.ApplyModifiedPropertiesWithoutUndo();
        var manager=new SerializedObject(UnityEngine.Object.FindFirstObjectByType<DecisionManager>());Ref(manager,"learningStation",station);manager.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(chat.gameObject.scene);EditorSceneManager.SaveScene(chat.gameObject.scene);return new{initialSlots=1,waiting=5,seconds=5,trialCost=1,trialReward=2};
    }
    public static object Map()
    {
        var image=UnityEngine.Object.FindObjectsByType<UnityEngine.UI.RawImage>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(r=>r.name=="RawImage_Road_View_And_Map");
        fontSource=UnityEngine.Object.FindFirstObjectByType<ChatHistoryUi>(FindObjectsInactive.Include).GetComponentsInChildren<TextMeshProUGUI>(true).First();
        if(image.transform.Find("LiveMapMarkers")!=null)throw new Exception("Already authored");
        var root=Node("LiveMapMarkers",image.transform,Vector2.zero,Vector2.one);root.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();var markers=root.gameObject.AddComponent<JourneyMapMarkers>();
        var band=Node("CollectorApproach",root,Vector2.zero,Vector2.one).gameObject.AddComponent<MapSymbolGraphic>();band.warning=true;band.raycastTarget=false;
        var warning=Text("Warning",root,new Vector2(.06f,.02f),new Vector2(.94f,.1f),"징수원 접근 ↑",12);warning.color=Color.white;
        Text("Direction",root,new Vector2(.1f,.92f),new Vector2(.9f,.99f),"탈출 방향 ↑",12).color=Color.white;
        var data=new SerializedObject(markers);Ref(data,"mapCamera",UnityEngine.Object.FindFirstObjectByType<RoadViewCameraController>().GetComponent<Camera>());Ref(data,"placement",UnityEngine.Object.FindFirstObjectByType<MineNpcPlacement>());Ref(data,"collectors",UnityEngine.Object.FindFirstObjectByType<MineCollectorProcession>());Ref(data,"mapArea",root);Ref(data,"warning",band);Ref(data,"warningText",warning);data.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(image.gameObject.scene);EditorSceneManager.SaveScene(image.gameObject.scene);return "Building / NPC / player / collector markers and approach band";
    }
    public static object Sitting()
    {
        var knight=GameObject.Find("KnightCharacter_Copy_Exam");if(knight==null)throw new Exception("User's seated preview is missing");
        var animator=knight.GetComponent<Animator>();
        const string clipPath="Assets/Decision_YYS/Animation/X Bot@Sitting Idle.fbx";
        var importer=(ModelImporter)AssetImporter.GetAtPath(clipPath);importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
        var clips=importer.clipAnimations;if(clips.Length==0)clips=importer.defaultClipAnimations;foreach(var c in clips)c.loopTime=true;importer.clipAnimations=clips;importer.SaveAndReimport();
        RepairSeatedAvatar();
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Decision_YYS/Animation/SeatedKnight.controller") ?? AnimatorController.CreateAnimatorControllerAtPath("Assets/Decision_YYS/Animation/SeatedKnight.controller");
        var state=controller.layers[0].stateMachine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Sitting Idle") ?? controller.layers[0].stateMachine.AddState("Sitting Idle");
        state.motion=AssetDatabase.LoadAllAssetsAtPath(clipPath).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));controller.layers[0].stateMachine.defaultState=state;
        animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;
        var host=UnityEngine.Object.FindFirstObjectByType<DuelMiniGameBridge>().gameObject;var preview=host.GetComponent<SeatedPlayerPreview>()??host.AddComponent<SeatedPlayerPreview>();
        var data=new SerializedObject(preview);Ref(data,"seatedPlayer",knight);Ref(data,"duel",host.GetComponent<DuelMiniGameBridge>());Ref(data,"miniGames",UnityEngine.Object.FindFirstObjectByType<TutorialMiniGameController>());data.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(knight.scene);EditorSceneManager.SaveScene(knight.scene);AssetDatabase.SaveAssets();return new{avatar=animator.avatar.isHuman,clip=((AnimationClip)state.motion).isHumanMotion,position=knight.transform.position.ToString()};
    }
    public static object RepairSeatedAvatar()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/DecisionScene.unity");
        var knight=GameObject.Find("KnightCharacter_Copy_Exam");var animator=knight.GetComponent<Animator>();
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Decision_YYS/Prafabs/People/KnightCharacter_Copy.prefab");
        var temp=UnityEngine.Object.Instantiate(source);temp.name=knight.name;temp.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);temp.transform.localScale=Vector3.one;
        try
        {
            var bones=temp.GetComponentsInChildren<Transform>(true);Transform Bone(string name)=>bones.Single(t=>t.name==name);
            Bone("Foot.L").SetParent(Bone("LowerLeg.L_end"),true);Bone("Foot.R").SetParent(Bone("LowerLeg.R_end"),true);
            var map=new Dictionary<string,string>{{"Hips","Body"},{"Spine","Hips"},{"Chest","Abdomen"},{"UpperChest","Torso"},{"Neck","Neck"},{"Head","Head"},
                {"LeftShoulder","Shoulder.L"},{"LeftUpperArm","UpperArm.L"},{"LeftLowerArm","LowerArm.L"},{"LeftHand","Palm.L"},{"RightShoulder","Shoulder.R"},{"RightUpperArm","UpperArm.R"},{"RightLowerArm","LowerArm.R"},{"RightHand","Palm.R"},
                {"LeftUpperLeg","UpperLeg.L"},{"LeftLowerLeg","LowerLeg.L"},{"LeftFoot","Foot.L"},{"RightUpperLeg","UpperLeg.R"},{"RightLowerLeg","LowerLeg.R"},{"RightFoot","Foot.R"}};
            var description=new HumanDescription {human=map.Select(p=>new HumanBone{humanName=p.Key,boneName=p.Value,limit=new HumanLimit{useDefaultValues=true}}).ToArray(),skeleton=bones.Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray(),upperArmTwist=.5f,lowerArmTwist=.5f,upperLegTwist=.5f,lowerLegTwist=.5f,armStretch=.05f,legStretch=.05f};
            var avatar=AvatarBuilder.BuildHumanAvatar(temp,description);avatar.name="SeatedKnightAvatar";
            if(!avatar.isHuman||!avatar.isValid)throw new Exception("Invalid avatar");
            var existing=AssetDatabase.LoadAssetAtPath<Avatar>("Assets/Decision_YYS/Animation/SeatedKnightAvatar.asset");if(existing==null){AssetDatabase.CreateAsset(avatar,"Assets/Decision_YYS/Animation/SeatedKnightAvatar.asset");existing=avatar;}else{EditorUtility.CopySerialized(avatar,existing);UnityEngine.Object.DestroyImmediate(avatar);EditorUtility.SetDirty(existing);}
            foreach(var bone in knight.GetComponentsInChildren<Transform>(true))
            {
                if(bone==knight.transform)continue;var original=bones.FirstOrDefault(t=>t.name==bone.name);if(original==null)continue;
                bone.SetLocalPositionAndRotation(original.localPosition,original.localRotation);bone.localScale=original.localScale;
            }
            animator.avatar=existing;EditorSceneManager.MarkSceneDirty(knight.scene);EditorSceneManager.SaveScene(knight.scene);AssetDatabase.SaveAssets();
            return new{existing.isHuman,existing.isValid};
        }
        finally {UnityEngine.Object.DestroyImmediate(temp);}
    }
}
