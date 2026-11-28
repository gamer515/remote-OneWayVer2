using System;
using System.Linq;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using OneWay.Loading;

public static class ConfigureBattleLoadingGuide
{
    const string Folder="Assets/Loading/Authoring~/";
    static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static LoadingScreenController Screen(Scene scene)=>scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<LoadingScreenController>(true)).Single();
    static RectTransform RectNode(Transform parent,string name)
    {
        var found=parent.Find(name) as RectTransform;if(found!=null)return found;
        var go=new GameObject(name,typeof(RectTransform)){layer=5};go.transform.SetParent(parent,false);return (RectTransform)go.transform;
    }
    static T Component<T>(GameObject go) where T:Component=>go.GetComponent<T>()??go.AddComponent<T>();
    static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;r.localScale=Vector3.one;}
    static void Place(RectTransform r,Vector2 anchor,Vector2 size){r.anchorMin=r.anchorMax=anchor;r.pivot=new Vector2(.5f,.5f);r.sizeDelta=size;r.anchoredPosition=Vector2.zero;r.localScale=Vector3.one;}
    static Sprite Icon(string name)
    {
        string path="Assets/Loading/Art/Mouse"+name+".png";
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
        importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;
        importer.maxTextureSize=512;importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
    static void SetProgress(LoadingScreenController screen,float value)=>typeof(LoadingScreenController).GetMethod("SetProgress",Hidden).Invoke(screen,new object[]{value});
    static string Hash(string path){using(var stream=File.OpenRead(path))using(var sha=System.Security.Cryptography.SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(stream));}
    static string[] PreservedPaths={"Assets/Scenes/MainMenuScene.unity","Assets/Scenes/DecisionScene.unity","Assets/Scenes/3DBattleScene.unity","Assets/Loading/LoadingTips.asset","Assets/Loading/LoadingText/LoadDecisionScene/NextRunDialogue.txt"};

    public static object Build()
    {
        if(Application.isPlaying||EditorApplication.isCompiling||AnimationMode.InAnimationMode())throw new Exception("Edit mode required");
        var originalScene=SceneManager.GetActiveScene();var scene=SceneManager.GetSceneByPath(LoadingScreenController.LoadingScenePath);
        bool opened=!scene.IsValid()||!scene.isLoaded;
        var hashes=PreservedPaths.ToDictionary(p=>p,Hash);
        if(opened)scene=EditorSceneManager.OpenScene(LoadingScreenController.LoadingScenePath,OpenSceneMode.Additive);
        try
        {
            if(scene.isDirty)throw new Exception("LoadingScene has unsaved changes; inspect them first");
            var screen=Screen(scene);var canvas=screen.GetComponentInChildren<Canvas>(true);
            var originalBar=screen.fill.parent.parent as RectTransform;
            if(originalBar.name!="LoadingBar")throw new Exception("Unexpected original loading bar hierarchy");
            var before=new{screen.fillStartDelay,screen.minimumDisplayTime,screen.previewOnly,screen.previewDuration,
                tipFont=AssetDatabase.GetAssetPath(screen.tipLabel.font),tipRect=screen.tipLabel.rectTransform.rect.ToString(),
                barMin=originalBar.anchorMin.ToString(),barMax=originalBar.anchorMax.ToString(),barSize=originalBar.sizeDelta.ToString(),preservedHashes=hashes};
            File.WriteAllText(Folder+"BattleLoadingGuideBefore.json",Newtonsoft.Json.JsonConvert.SerializeObject(before,Newtonsoft.Json.Formatting.Indented));
            var font=screen.tipLabel.font;
            string needed=new string("공격/방어방패 꺼내기구르기".Where(c=>!font.HasCharacter(c)).Distinct().ToArray());
            if(needed.Length>0&&!font.TryAddCharacters(needed,out string missing)&&!string.IsNullOrEmpty(missing))throw new Exception("Missing Korean glyphs: "+missing);
            EditorUtility.SetDirty(font);foreach(var atlas in font.atlasTextures)EditorUtility.SetDirty(atlas);AssetDatabase.SaveAssetIfDirty(font);

            var guide=RectNode(canvas.transform,"BattleControlGuide");Stretch(guide);
            var icons=new[]{Icon("LeftButton"),Icon("MouseWheel"),Icon("RightButton")};
            var labels=new[]{"공격/방어","방패 꺼내기","구르기"};
            var names=new[]{"Left Click","Mouse Wheel","Right Click"};
            for(int i=0;i<3;i++)
            {
                if(icons[i]==null)throw new Exception("Mouse sprite missing");
                float x=.28f+.22f*i;
                var picture=RectNode(guide,names[i]+" Icon");Place(picture,new Vector2(x,.585f),new Vector2(180,270));
                var image=Component<Image>(picture.gameObject);image.sprite=icons[i];image.color=Color.white;image.preserveAspect=true;image.raycastTarget=false;
                var labelRect=RectNode(guide,names[i]+" Label");Place(labelRect,new Vector2(x,.41f),new Vector2(320,60));
                var label=Component<TextMeshProUGUI>(labelRect.gameObject);label.font=font;label.fontSize=28;label.enableAutoSizing=false;
                label.text=labels[i];label.alignment=TextAlignmentOptions.Center;label.color=new Color(.75f,.75f,.75f,1);
                label.richText=false;label.raycastTarget=false;label.textWrappingMode=TextWrappingModes.NoWrap;label.overflowMode=TextOverflowModes.Overflow;
            }
            var bar=RectNode(canvas.transform,"BattleLoadingBar");bar.anchorMin=new Vector2(.04f,.095f);bar.anchorMax=new Vector2(.96f,.095f);
            bar.sizeDelta=new Vector2(0,64);bar.anchoredPosition=Vector2.zero;
            var outline=Component<LoadingShapeGraphic>(bar.gameObject);outline.color=Color.white;outline.raycastTarget=false;
            var inner=RectNode(bar,"RoundedClip");Stretch(inner);inner.offsetMin=Vector2.one*5;inner.offsetMax=-Vector2.one*5;
            var maskGraphic=Component<LoadingShapeGraphic>(inner.gameObject);maskGraphic.color=Color.black;maskGraphic.raycastTarget=false;
            Component<Mask>(inner.gameObject).showMaskGraphic=true;
            var fill=RectNode(inner,"CurrentProgress");Stretch(fill);
            var fillImage=Component<Image>(fill.gameObject);fillImage.color=Color.white;fillImage.raycastTarget=false;
            screen.standardLoadingBar=originalBar.gameObject;screen.battleInstructions=guide.gameObject;screen.battleLoadingBar=bar.gameObject;screen.battleFill=fill;
            screen.battleFillStartDelay=5;
            screen.RefreshPresentation();SetProgress(screen,0);
            EditorUtility.SetDirty(screen);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            foreach(var pair in hashes)if(Hash(pair.Key)!=pair.Value)throw new Exception("Unexpected file change: "+pair.Key);
            return new{saved=scene.path,standardDelay=screen.fillStartDelay,battleDelay=screen.EffectiveFillStartDelay,screen.previewOnly,destination=screen.destination.ToString(),labels,preservedOtherScenesAndDecisionTips=true};
        }
        finally{if(opened)EditorSceneManager.CloseScene(scene,true);if(originalScene.IsValid()&&originalScene.isLoaded)SceneManager.SetActiveScene(originalScene);}
    }

    public static object VerifyAndRender()
    {
        if(Application.isPlaying||EditorApplication.isCompiling)throw new Exception("Edit mode required");
        var originalScene=SceneManager.GetActiveScene();var source=SceneManager.GetSceneByPath(LoadingScreenController.LoadingScenePath);
        bool opened=!source.IsValid()||!source.isLoaded;if(opened)source=EditorSceneManager.OpenScene(LoadingScreenController.LoadingScenePath,OpenSceneMode.Additive);
        var preview=EditorSceneManager.NewPreviewScene();RenderTexture rt=null;Texture2D capture=null;var oldRT=RenderTexture.active;
        try
        {
            var root=UnityEngine.Object.Instantiate(Screen(source).gameObject);SceneManager.MoveGameObjectToScene(root,preview);
            var screen=root.GetComponent<LoadingScreenController>();screen.enabled=false;
            var originalStandard=(RectTransform)screen.standardLoadingBar.transform;
            Vector2 barMin=originalStandard.anchorMin,barMax=originalStandard.anchorMax,barSize=originalStandard.sizeDelta;
            float standardDelay=screen.fillStartDelay;
            var expected=File.ReadAllText("Assets/Loading/LoadingText/LoadDecisionScene/NextRunDialogue.txt").Trim();
            var cg=new GameObject("Loading guide preview camera");SceneManager.MoveGameObjectToScene(cg,preview);
            var camera=cg.AddComponent<Camera>();camera.scene=preview;camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            camera.orthographic=true;camera.orthographicSize=360;camera.nearClipPlane=.1f;camera.farClipPlane=100;
            var canvas=root.GetComponentInChildren<Canvas>(true);canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            canvas.GetComponent<CanvasScaler>().enabled=false;canvas.scaleFactor=2f/3f;
            rt=new RenderTexture(1280,720,24);camera.targetTexture=rt;
            var outcomes=new System.Collections.Generic.List<object>();
            foreach(var destination in new[]{LoadingDestination.Battle,LoadingDestination.Decision,LoadingDestination.MainMenu,LoadingDestination.Battle})
            {
                screen.destination=destination;screen.RefreshPresentation();
                bool battle=destination==LoadingDestination.Battle;
                if(screen.battleInstructions.activeSelf!=battle||screen.battleLoadingBar.activeSelf!=battle||screen.standardLoadingBar.activeSelf==battle)throw new Exception("Incorrect destination layout");
                if(battle&&(screen.tipLabel.gameObject.activeSelf||screen.SelectedTip!=""))throw new Exception("Old Battle tip still visible");
                if(destination==LoadingDestination.Decision&&(!screen.tipLabel.gameObject.activeSelf||screen.SelectedTip!=expected))throw new Exception("Decision text changed");
                if(destination==LoadingDestination.MainMenu&&screen.SelectedTip!="")throw new Exception("Main menu tip changed");
                if(originalStandard.anchorMin!=barMin||originalStandard.anchorMax!=barMax||originalStandard.sizeDelta!=barSize)throw new Exception("Other destination bar layout changed");
                if(screen.fillStartDelay!=standardDelay||screen.EffectiveFillStartDelay!=(battle?5:standardDelay))throw new Exception("Destination delay incorrect");
                foreach(float progress in new[]{0f,.5f,1f})
                {
                    SetProgress(screen,progress);var shownFill=battle?screen.battleFill:screen.fill;
                    if(shownFill.anchorMax.x!=progress||shownFill.gameObject.activeSelf!=(progress>0))throw new Exception("Progress binding failed");
                    foreach(var graphic in root.GetComponentsInChildren<Graphic>(true))graphic.SetAllDirty();
                    Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;
                    capture=new Texture2D(1280,720,TextureFormat.RGB24,false);capture.ReadPixels(new Rect(0,0,1280,720),0,0);capture.Apply();
                    var path=Folder+"LoadingGuide_"+destination+"_"+Mathf.RoundToInt(progress*100)+".png";
                    File.WriteAllBytes(path,capture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(capture);capture=null;
                }
                outcomes.Add(new{destination=destination.ToString(),tip=screen.SelectedTip,battleInstructions=screen.battleInstructions.activeSelf,delay=screen.EffectiveFillStartDelay});
            }
            File.WriteAllText(Folder+"BattleLoadingGuideVerification.json",Newtonsoft.Json.JsonConvert.SerializeObject(outcomes,Newtonsoft.Json.Formatting.Indented));
            return new{passed=true,destinations=outcomes,sourceSceneDirty=source.isDirty};
        }
        finally
        {
            RenderTexture.active=oldRT;if(capture!=null)UnityEngine.Object.DestroyImmediate(capture);if(rt!=null)UnityEngine.Object.DestroyImmediate(rt);
            EditorSceneManager.ClosePreviewScene(preview);if(opened)EditorSceneManager.CloseScene(source,true);
            if(originalScene.IsValid()&&originalScene.isLoaded)SceneManager.SetActiveScene(originalScene);
        }
    }

    public static object PrepareRuntimePreview()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||AnimationMode.InAnimationMode())throw new Exception("Edit mode required");
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new Exception("Unsaved scene remains open");
        SessionState.SetString("BattleLoadingGuide.SceneSetup",Newtonsoft.Json.JsonConvert.SerializeObject(EditorSceneManager.GetSceneManagerSetup()));
        SessionState.SetString("BattleLoadingGuide.StartScene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene=null;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorApplication.isPlaying=true;return "Starting isolated loading preview";
    }
    public static async System.Threading.Tasks.Task<object> RuntimePreview()
    {
        if(!Application.isPlaying)throw new Exception("Play mode required");
        LoadingScreenController screen=null;
        void Configure(Scene scene,LoadSceneMode mode)
        {
            if(scene.path!=LoadingScreenController.LoadingScenePath)return;
            screen=Screen(scene);screen.previewOnly=true;screen.destination=LoadingDestination.Battle;
        }
        SceneManager.sceneLoaded+=Configure;
        try
        {
            var operation=EditorSceneManager.LoadSceneAsyncInPlayMode(LoadingScreenController.LoadingScenePath,new LoadSceneParameters(LoadSceneMode.Single));
            while(!operation.isDone)await System.Threading.Tasks.Task.Delay(25);
            if(screen==null)throw new Exception("Loading screen not found");
            while(!screen.IsPreview)await System.Threading.Tasks.Task.Delay(20);
            screen.ReplayPreview();float started=Time.realtimeSinceStartup;
            await System.Threading.Tasks.Task.Delay(4200);
            if(screen.DisplayedProgress!=0||!screen.battleInstructions.activeInHierarchy||screen.tipLabel.gameObject.activeInHierarchy)throw new Exception("Battle guide/delay incorrect before five seconds");
            float heldUntil=Time.realtimeSinceStartup-started;
            await System.Threading.Tasks.Task.Delay(1800);
            if(screen.DisplayedProgress<=0||!screen.battleInstructions.activeInHierarchy||!screen.IsPreview)throw new Exception("Battle guide or progress missing after five seconds");
            float progressedAt=Time.realtimeSinceStartup-started,progress=screen.DisplayedProgress;
            string battleCapture=Folder+"LoadingGuide_RuntimeBattle.png";
            ScreenCapture.CaptureScreenshot(battleCapture);
            await System.Threading.Tasks.Task.Delay(250);
            screen.destination=LoadingDestination.Decision;screen.ReplayPreview();
            await System.Threading.Tasks.Task.Delay(250);
            string expected=File.ReadAllText("Assets/Loading/LoadingText/LoadDecisionScene/NextRunDialogue.txt").Trim();
            if(screen.battleInstructions.activeInHierarchy||!screen.tipLabel.gameObject.activeInHierarchy||screen.SelectedTip!=expected||screen.EffectiveFillStartDelay!=2)throw new Exception("Decision presentation changed");
            string decisionCapture=Folder+"LoadingGuide_RuntimeDecision.png";ScreenCapture.CaptureScreenshot(decisionCapture);
            await System.Threading.Tasks.Task.Delay(250);
            var result=new{passed=true,heldUntil,progressedAt,progress,decisionTip=screen.SelectedTip,battleCapture,decisionCapture,previewOnly=true};
            File.WriteAllText(Folder+"BattleLoadingGuideRuntimeVerification.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));return result;
        }
        finally{SceneManager.sceneLoaded-=Configure;}
    }
    public static object StopRuntimePreview(){EditorApplication.isPlaying=false;return "Stopping preview";}
    public static object RestoreEditor()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Wait until play mode stops");
        var setups=Newtonsoft.Json.JsonConvert.DeserializeObject<SceneSetup[]>(SessionState.GetString("BattleLoadingGuide.SceneSetup",""));
        EditorSceneManager.RestoreSceneManagerSetup(setups);
        string path=SessionState.GetString("BattleLoadingGuide.StartScene","");
        EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(path)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
        return new{restored=setups.Select(s=>s.path).ToArray()};
    }
}
