using System;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using OneWay.Battle3D;

public static class ConfigureMinimalBattleHUD
{
    const string ScenePath = "Assets/Scenes/3DBattleScene.unity";
    static string PathOf(Transform t) => t == null ? null : (t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name);
    static object RectInfo(RectTransform r) => r == null ? null : new {
        min=r.anchorMin.ToString(), max=r.anchorMax.ToString(), pivot=r.pivot.ToString(),
        position=r.anchoredPosition.ToString(), size=r.sizeDelta.ToString(), rect=r.rect.ToString()
    };
    static Scene GetScene() { var s=SceneManager.GetSceneByPath(ScenePath); if(!s.IsValid() || !s.isLoaded)throw new Exception("Open 3DBattleScene first");return s; }
    static T[] Components<T>() where T:Component => GetScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).ToArray();
    public static object Inspect()
    {
        var s=GetScene();
        return new {
            playing=Application.isPlaying, compiling=EditorApplication.isCompiling, animation=AnimationMode.InAnimationMode(), dirty=s.isDirty,
            player=Components<BattlePlayerHealthUI>().Select(u=>new {path=PathOf(u.transform),health=PathOf(u.playerHealth?.transform),fill=PathOf(u.fill),damage=PathOf(u.damageFill),text=PathOf(u.valueText?.transform),vitals=PathOf(u.vitals?.transform)}).ToArray(),
            boss=Components<BattleBossHealthUI>().Select(u=>new {path=PathOf(u.transform),bar=PathOf(u.bar),fill=PathOf(u.fill),damage=PathOf(u.damageFill),text=PathOf(u.valueText?.transform)}).ToArray(),
            canvases=Components<Canvas>().Where(c=>c.isRootCanvas).Select(c=>new {path=PathOf(c.transform), mode=c.renderMode.ToString(),active=c.gameObject.activeSelf,
                nodes=c.GetComponentsInChildren<RectTransform>(true).Select(t=>new {path=PathOf(t),active=t.gameObject.activeSelf,rect=RectInfo(t),components=t.GetComponents<Component>().Select(x=>x?.GetType().Name).ToArray(),text=t.GetComponent<Text>()?.text,font=t.GetComponent<Text>()?.font?.name}).ToArray()
            }).ToArray()
        };
    }
    public static object InspectIcons()
    {
        return new[]{"C:/Users/hkh50/Downloads/순백의 검 HUD 아이콘-1.png","C:/Users/hkh50/Downloads/투명 배경의 흰색 육각 방패 아이콘-2.png"}.Select(path=>{
            var tex=new Texture2D(2,2);tex.LoadImage(File.ReadAllBytes(path));var pixels=tex.GetPixels32();
            int transparent=0,opaque=0,minX=tex.width,minY=tex.height,maxX=-1,maxY=-1;
            for(int y=0;y<tex.height;y++)for(int x=0;x<tex.width;x++){
                var p=pixels[y*tex.width+x];if(p.a==0)transparent++;if(p.a==255)opaque++;
                if(p.a>128){minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);}
            }
            var result=new {path,width=tex.width,height=tex.height,transparent,opaque,minX,maxX,minY,maxY};UnityEngine.Object.DestroyImmediate(tex);return result;
        }).ToArray();
    }

    static void EnsureEditable()
    {
        if(Application.isPlaying || EditorApplication.isCompiling || AnimationMode.InAnimationMode()) throw new Exception("Edit mode without compilation or animation preview required");
        if(GetScene().isDirty) throw new Exception("The scene has unsaved edits; inspect before saving");
    }
    static Sprite ImportIcon(string name)
    {
        string path="Assets/3DBattle/UI/Icons/"+name+".png";
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
        importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Clamp;
        importer.filterMode=FilterMode.Bilinear;importer.maxTextureSize=512;
        importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
    static RectTransform RectNode(Transform parent,string name)
    {
        var existing=parent.Find(name) as RectTransform;
        if(existing!=null)return existing;
        var go=new GameObject(name,typeof(RectTransform));go.layer=5;go.transform.SetParent(parent,false);
        return (RectTransform)go.transform;
    }
    static T Component<T>(GameObject go) where T:Component => go.GetComponent<T>() ?? go.AddComponent<T>();
    static void FixedRect(RectTransform r, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        r.anchorMin=r.anchorMax=anchor;r.pivot=pivot;r.sizeDelta=size;r.anchoredPosition=position;r.localScale=Vector3.one;r.localRotation=Quaternion.identity;
    }
    static Text NumberLabel(RectTransform rect,Font font,float bottom,float top)
    {
        rect.anchorMin=new Vector2(0,bottom);rect.anchorMax=new Vector2(1,top);rect.pivot=new Vector2(.5f,.5f);
        rect.offsetMin=rect.offsetMax=Vector2.zero;rect.localScale=Vector3.one;rect.localRotation=Quaternion.identity;
        rect.gameObject.SetActive(true);
        var text=Component<Text>(rect.gameObject);text.enabled=true;text.font=font;text.fontSize=28;text.fontStyle=FontStyle.Normal;
        text.alignment=TextAnchor.MiddleCenter;text.alignByGeometry=true;text.color=Color.white;text.raycastTarget=false;
        text.resizeTextForBestFit=false;text.horizontalOverflow=HorizontalWrapMode.Overflow;text.verticalOverflow=VerticalWrapMode.Overflow;
        var shadow=Component<Shadow>(rect.gameObject);shadow.effectColor=new Color(0,0,0,.8f);shadow.effectDistance=new Vector2(1,-1);
        return text;
    }
    public static object Apply()
    {
        EnsureEditable();
        var attack=ImportIcon("AttackMode");var parry=ImportIcon("ParryMode");
        if(attack==null||parry==null)throw new Exception("Icon import failed");
        var ui=Components<BattlePlayerHealthUI>().Single();var boss=Components<BattleBossHealthUI>().Single();
        var canvas=ui.GetComponent<Canvas>();
        var scaler=ui.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        var graphic=ui.vitals;graphic.minimalLayout=true;graphic.raycastTarget=false;graphic.color=Color.white;
        graphic.healthBottom=new Color(.035f,.52f,.23f);graphic.healthTop=new Color(.13f,.77f,.37f);
        FixedRect(graphic.rectTransform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,30),new Vector2(620,90));
        var font=ui.valueText!=null?ui.valueText.font:Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var healthLabel=ui.valueText!=null?ui.valueText.rectTransform:RectNode(graphic.transform,"Health Value");
        healthLabel.SetParent(graphic.transform,false);healthLabel.name="Health Value";
        ui.valueText=NumberLabel(healthLabel,font,BattlePlayerVitalsGraphic.HealthRowBottom,1);
        ui.energyValueText=NumberLabel(RectNode(graphic.transform,"Energy Value"),font,0,BattlePlayerVitalsGraphic.EnergyRowTop);
        ui.fill=null;ui.damageFill=null;
        var legacy=ui.transform.Find("Player Health");if(legacy!=null)UnityEngine.Object.DestroyImmediate(legacy.gameObject);

        boss.alignToTop=true;boss.screenWidth=.98f;
        FixedRect(boss.bar,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-20),new Vector2(0,38));
        var bossBackground=boss.bar.GetComponent<Image>();bossBackground.color=new Color(.012f,.055f,.035f,.85f);bossBackground.raycastTarget=false;
        var bossFill=boss.fill.GetComponent<Image>();bossFill.color=Color.white;bossFill.raycastTarget=false;
        var gradient=Component<BattleHudVerticalGradient>(bossFill.gameObject);gradient.bottom=graphic.healthBottom;gradient.top=graphic.healthTop;
        var frame=boss.bar.Find("Metal Frame");if(frame!=null)UnityEngine.Object.DestroyImmediate(frame.gameObject);
        if(boss.valueText!=null)boss.valueText.gameObject.SetActive(false);
        boss.Refresh();

        var modeRoot=RectNode(canvas.transform,"Combat Mode");
        FixedRect(modeRoot,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(420,30),new Vector2(128,196));
        var mode=Component<BattleCombatModeUI>(modeRoot.gameObject);mode.player=ui.playerHealth.GetComponent<BattlePlayerController>();
        var attackRect=RectNode(modeRoot,"Attack - Sword");
        FixedRect(attackRect,new Vector2(.5f,1),new Vector2(.5f,1),Vector2.zero,new Vector2(108,108));
        mode.attackIcon=Component<Image>(attackRect.gameObject);mode.attackIcon.sprite=attack;mode.attackIcon.preserveAspect=true;mode.attackIcon.raycastTarget=false;
        var parryRect=RectNode(modeRoot,"Parry - Shield");
        FixedRect(parryRect,new Vector2(.5f,0),new Vector2(.5f,0),Vector2.zero,new Vector2(80,80));
        mode.parryIcon=Component<Image>(parryRect.gameObject);mode.parryIcon.sprite=parry;mode.parryIcon.preserveAspect=true;mode.parryIcon.raycastTarget=false;
        RestyleWidgets(ui,boss);
        mode.Refresh();ui.Refresh();
        EditorUtility.SetDirty(ui);EditorUtility.SetDirty(graphic);EditorUtility.SetDirty(boss);EditorUtility.SetDirty(mode);
        EditorSceneManager.MarkSceneDirty(GetScene());EditorSceneManager.SaveScene(GetScene());
        return new{saved=ScenePath,playerWidth=620,startingEnergy=ui.energy.Gauge,maximumEnergy=ui.energy.maxGauge,health=ui.valueText.text,energy=ui.energyValueText.text};
    }

    public static object WidenIconSpacing()
    {
        EnsureEditable();var mode=Components<BattleCombatModeUI>().Single();
        var rect=(RectTransform)mode.transform;rect.sizeDelta=new Vector2(128,196);
        EditorSceneManager.MarkSceneDirty(GetScene());EditorSceneManager.SaveScene(GetScene());
        return new{saved=ScenePath,addedVerticalGap=30};
    }

    public static object MoveIconsNearBars()
    {
        EnsureEditable();var mode=Components<BattleCombatModeUI>().Single();
        var rect=(RectTransform)mode.transform;
        rect.anchorMin=rect.anchorMax=new Vector2(.5f,0);rect.anchoredPosition=new Vector2(420,30);
        EditorSceneManager.MarkSceneDirty(GetScene());EditorSceneManager.SaveScene(GetScene());
        return new{saved=ScenePath,position=rect.anchoredPosition.ToString(),height=rect.sizeDelta.y};
    }

    static void StretchRow(RectTransform rect,float bottom,float top)
    {
        rect.anchorMin=new Vector2(0,bottom);rect.anchorMax=new Vector2(1,top);
        rect.offsetMin=rect.offsetMax=Vector2.zero;rect.localScale=Vector3.one;rect.localRotation=Quaternion.identity;
    }
    static void RestyleWidgets(BattlePlayerHealthUI ui,BattleBossHealthUI boss)
    {
        ui.vitals.minimalBarInset=4;
        var hpFrame=RectNode(ui.vitals.transform,"Health - Grey Frame");StretchRow(hpFrame,BattlePlayerVitalsGraphic.HealthRowBottom,1);
        var energyFrame=RectNode(ui.vitals.transform,"Energy - Grey Frame");StretchRow(energyFrame,0,BattlePlayerVitalsGraphic.EnergyRowTop);
        foreach(var rect in new[]{hpFrame,energyFrame})
        {
            var frame=Component<BattleHealthFrame>(rect.gameObject);frame.greyRectangularFrame=true;frame.raycastTarget=false;frame.color=Color.white;
            frame.SetVerticesDirty();
        }
        foreach(var label in new[]{ui.valueText,ui.energyValueText})
        {
            label.alignment=TextAnchor.MiddleLeft;label.rectTransform.offsetMin=new Vector2(12,0);label.rectTransform.offsetMax=new Vector2(-12,0);
            label.transform.SetAsLastSibling();
        }
        ui.vitals.SetVerticesDirty();
        boss.alignToTop=true;
        boss.bar.sizeDelta=new Vector2(boss.bar.sizeDelta.x,32);
        boss.bar.GetComponent<Image>().color=Color.black;
        var bossFill=boss.fill.GetComponent<Image>();bossFill.color=new Color(.1f,.75f,.32f,1);
        var gradient=bossFill.GetComponent<BattleHudVerticalGradient>();if(gradient!=null)UnityEngine.Object.DestroyImmediate(gradient);
        var bossFrame=RectNode(boss.bar,"Metal Frame");StretchRow(bossFrame,0,1);bossFrame.SetAsLastSibling();
        var legacyFrame=Component<BattleHealthFrame>(bossFrame.gameObject);legacyFrame.greyRectangularFrame=false;legacyFrame.raycastTarget=false;legacyFrame.color=Color.white;
        legacyFrame.SetVerticesDirty();bossFill.SetVerticesDirty();
        var mode=ui.GetComponentInChildren<BattleCombatModeUI>(true);
        // Match the supplied image: sword pommel at the health row, shield centred on energy.
        FixedRect((RectTransform)mode.transform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(362,30),new Vector2(128,160));
        FixedRect(mode.attackIcon.rectTransform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,45),new Vector2(108,108));
        FixedRect(mode.parryIcon.rectTransform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,-22),new Vector2(80,80));
    }
    public static object ApplyReferenceStyle()
    {
        EnsureEditable();var ui=Components<BattlePlayerHealthUI>().Single();var boss=Components<BattleBossHealthUI>().Single();
        RestyleWidgets(ui,boss);ui.Refresh();
        EditorSceneManager.MarkSceneDirty(GetScene());EditorSceneManager.SaveScene(GetScene());
        return new{saved=ScenePath,bossAtTop=boss.alignToTop,playerInset=ui.vitals.minimalBarInset,labelAlignment=ui.valueText.alignment.ToString()};
    }

    public static object Preview()
    {
        EnsureEditable();
        var preview=EditorSceneManager.NewPreviewScene();
        RenderTexture target=null;Texture2D capture=null;var previous=RenderTexture.active;
        try
        {
            var original=Components<BattlePlayerHealthUI>().Single();
            var root=UnityEngine.Object.Instantiate(original.gameObject);SceneManager.MoveGameObjectToScene(root,preview);
            var ui=root.GetComponent<BattlePlayerHealthUI>();ui.enabled=false;
            var boss=root.GetComponentInChildren<BattleBossHealthUI>(true);boss.enabled=false;
            var mode=root.GetComponentInChildren<BattleCombatModeUI>(true);mode.enabled=false;
            var actor=new GameObject("HUD verification values");SceneManager.MoveGameObjectToScene(actor,preview);
            ui.playerHealth=actor.AddComponent<BattlePlayerHealth>();ui.energy=actor.AddComponent<DebugPlayerSpecialCombo>();
            ui.energy.alwaysFullGauge=false;mode.player=actor.AddComponent<BattlePlayerController>();
            var cg=new GameObject("HUD preview camera");SceneManager.MoveGameObjectToScene(cg,preview);
            var camera=cg.AddComponent<Camera>();camera.scene=preview;camera.enabled=false;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            camera.orthographic=true;camera.orthographicSize=360;camera.nearClipPlane=.1f;camera.farClipPlane=100;
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            root.GetComponent<CanvasScaler>().enabled=false;
            var outcomes=new System.Collections.Generic.List<object>();
            foreach(int max in new[]{70,100,130,150})
            {
                ui.playerHealth.maxHealth=max;ui.playerHealth.ResetHealth();ui.energy.ConfigureDecisionEnergy(max,5,25);ui.energy.currentGauge=50;ui.Refresh();
                if(ui.energyValueText.text!="50/"+max || ui.vitals.energyMaximum!=max)throw new Exception("Capacity binding failed: "+max);
                ui.energy.ChargeFromAttackHit();ui.Refresh();if(ui.energyValueText.text!="55/"+max)throw new Exception("Hit recharge label failed");
                ui.energy.ChargeFromParry();ui.Refresh();if(ui.energy.Gauge!=Mathf.Min(max,80))throw new Exception("Parry recharge cap failed");
                if(!ui.energy.TryConsume())throw new Exception("Special cannot consume");ui.Refresh();
                if(ui.energy.Gauge!=Mathf.Min(max,80)-50)throw new Exception("Special consumption label failed");
                outcomes.Add(new{maximum=max,afterHit=55,afterParry=Math.Min(max,80),afterSpecial=ui.energyValueText.text});
            }
            mode.player.ProcessControlFrame(-1,false,false,0);mode.Refresh();
            if(mode.parryIcon.color!=mode.selected || mode.attackIcon.color!=mode.unselected)throw new Exception("Parry mode icons not selected");
            mode.player.ProcessControlFrame(1,false,false,0);mode.Refresh();
            if(mode.attackIcon.color!=mode.selected || mode.parryIcon.color!=mode.unselected)throw new Exception("Attack mode icons not selected");
            foreach(var sample in new[]{new[]{1280,720,100,50,0},new[]{1280,720,100,100,0},new[]{1280,720,130,60,1},new[]{1280,800,150,100,0}})
            {
                int width=sample[0],height=sample[1],maximum=sample[2],current=sample[3];
                target=new RenderTexture(width,height,24);camera.targetTexture=target;
                camera.backgroundColor=current==50?new Color(.55f,.52f,.43f):Color.black;
                canvas.scaleFactor=Mathf.Sqrt((width/1920f)*(height/1080f));
                ui.playerHealth.maxHealth=maximum;ui.energy.ConfigureDecisionEnergy(maximum,5,25);ui.energy.currentGauge=current;ui.Refresh();
                mode.player.ProcessControlFrame(sample[4]==1?-1:1,false,false,0);mode.Refresh();
                boss.visibility.alpha=1;boss.bar.anchorMin=new Vector2(.01f,1);boss.bar.anchorMax=new Vector2(.99f,1);
                boss.fill.anchorMax=Vector2.one;boss.damageFill.anchorMax=Vector2.one;
                Canvas.ForceUpdateCanvases();
                var corners=new Vector3[4];ui.vitals.rectTransform.GetWorldCorners(corners);
                foreach(var corner in corners){var v=camera.WorldToViewportPoint(corner);if(v.x<0||v.x>1||v.y<0||v.y>1)throw new Exception("Player bars outside the viewport");}
                camera.Render();RenderTexture.active=target;
                capture=new Texture2D(width,height,TextureFormat.RGB24,false);capture.ReadPixels(new Rect(0,0,width,height),0,0);capture.Apply();
                string path="Assets/3DBattle/Authoring~/FramedHUD_"+maximum+"_"+current+"_"+(sample[4]==1?"Parry":"Attack")+".png";
                File.WriteAllBytes(path,capture.EncodeToPNG());
                if(current==50)
                {
                    var detail=new Texture2D(520,150,TextureFormat.RGB24,false);
                    detail.ReadPixels(new Rect(420,0,520,150),0,0);detail.Apply();
                    File.WriteAllBytes("Assets/3DBattle/Authoring~/FramedHUD_Detail.png",detail.EncodeToPNG());UnityEngine.Object.DestroyImmediate(detail);
                }
                outcomes.Add(new{preview=path,width,height,health=ui.valueText.text,energy=ui.energyValueText.text});
                camera.targetTexture=null;RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(target);target=null;
                UnityEngine.Object.DestroyImmediate(capture);capture=null;
            }
            File.WriteAllText("Assets/3DBattle/Authoring~/MinimalHUDVerification.json",Newtonsoft.Json.JsonConvert.SerializeObject(outcomes,Newtonsoft.Json.Formatting.Indented));
            return new{capacityBindings=outcomes,modeSwitching=true,sourceSceneDirty=GetScene().isDirty};
        }
        finally
        {
            RenderTexture.active=previous;if(target!=null)UnityEngine.Object.DestroyImmediate(target);if(capture!=null)UnityEngine.Object.DestroyImmediate(capture);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}
