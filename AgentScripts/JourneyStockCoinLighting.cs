using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using JourneyMapKit;

// Editor-only: change only the 56 variable-stock renderers, never source V4 assets.
public static class JourneyStockCoinLighting
{
    const string ScenePath="Assets/Scenes/DecisionScene.unity";
    const string MaterialPath="Assets/Decision_YYS/JourneyV4Integration/StockCoinGold_Realtime.mat";
    static Scene Scene()
    {
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(!scene.IsValid()||!scene.isLoaded)throw new Exception("Open DecisionScene first");
        return scene;
    }
    static Transform Board(Scene scene)=>scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="JourneyInterfaceV3_Runtime");
    static MeshRenderer[] Coins(Scene scene)=>Board(scene).GetComponentsInChildren<JourneyCoinStack>(true).SelectMany(s=>s.coins).Select(c=>c.GetComponent<MeshRenderer>()).ToArray();
    public static object Apply()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop Play first");
        var scene=Scene();var coins=Coins(scene);
        if(coins.Length!=56)throw new Exception("Unexpected stock count");
        if(AssetDatabase.LoadAssetAtPath<Material>(MaterialPath)!=null)throw new Exception("Already applied; refusing overwrite");
        var preserved=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Component>(true))
            .Where(c=>c!=null&&!coins.Contains(c as MeshRenderer)).ToDictionary(c=>c,EditorJsonUtility.ToJson);
        var meshes=coins.Select(c=>c.GetComponent<MeshFilter>().sharedMesh).ToArray();
        Directory.CreateDirectory("Temp/JourneyV4Migration");
        var backup="Temp/JourneyV4Migration/BeforeStockLighting-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".unity";
        if(!EditorSceneManager.SaveScene(scene,backup,true))throw new Exception("Backup failed");
        var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/AiAsset/JourneyInterfaceV4Baked/Materials/JourneyCoin_Blue_Baked_coin___CoinGold.mat");
        if(source==null||source.shader.name!="Universal Render Pipeline/Lit")throw new Exception("Missing realtime source material");
        Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Realtime lighting for variable stock coins only");
        var material=new Material(source){name="StockCoinGold_Realtime"};bool created=false;
        try
        {
            // Stock meshes have the assembly UV atlas, not the standalone coin AO atlas.
            // No baked texture or copied AO: exposed top faces use current scene lighting.
            material.SetTexture("_OcclusionMap",null);material.SetFloat("_OcclusionStrength",0);
            material.DisableKeyword("_OCCLUSIONMAP");
            AssetDatabase.CreateAsset(material,MaterialPath);created=true;
            foreach(var renderer in coins)
            {
                Undo.RecordObject(renderer,"Stock coin realtime material");
                renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            foreach(var p in preserved)if(p.Key==null||EditorJsonUtility.ToJson(p.Key)!=p.Value)throw new Exception("Unrelated setup changed: "+p.Key?.name);
            if(coins.Where((c,i)=>c.GetComponent<MeshFilter>().sharedMesh!=meshes[i]).Any())throw new Exception("Stock geometry changed");
            AssetDatabase.SaveAssetIfDirty(material);EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new Exception("Save failed");
            Undo.CollapseUndoOperations(group);
            return new{backup,stockRenderers=coins.Length,preservedComponents=preserved.Count,material=MaterialPath,sourceAssetsUntouched=true};
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            if(created)AssetDatabase.DeleteAsset(MaterialPath);else UnityEngine.Object.DestroyImmediate(material);
            throw;
        }
    }
    public static object Validate()
    {
        var scene=Scene();var board=Board(scene);var coins=Coins(scene);
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(material==null)throw new Exception("Missing stock material");
        var stacks=board.GetComponentsInChildren<JourneyCoinStack>(true);
        var checks=new List<object>();
        foreach(var stack in stacks)
        {
            int original=stack.Count;var active=stack.coins.Select(c=>c.activeSelf).ToArray();
            try
            {
                foreach(int count in new[]{14,10,9,1,0})
                {
                    stack.SetCount(count);int visible=stack.coins.Count(c=>c.activeSelf);
                    if(visible!=count)throw new Exception("Stock display mismatch");
                    if(count>0&&stack.coins[count-1].GetComponent<Renderer>().sharedMaterials.Any(m=>m!=material))throw new Exception("Exposed top still baked");
                    checks.Add(new{stack.name,count,visible,topHasRealtimeMaterial=count==0||stack.coins[count-1].GetComponent<Renderer>().sharedMaterials.All(m=>m==material)});
                }
            }
            finally{stack.SetCount(original);for(int i=0;i<active.Length;i++)stack.coins[i].SetActive(active[i]);}
        }
        var preview=EditorSceneManager.OpenPreviewScene(ScenePath);
        try
        {
            if(Coins(preview).Any(c=>c.sharedMaterials.Any(m=>m!=material)))throw new Exception("Saved stock material not retained");
            return new{scene.isDirty,stockRenderers=coins.Length,checks,
                shader=material.shader.name,supported=material.shader.isSupported,
                noBakedOcclusion=material.GetTexture("_OcclusionMap")==null&&!material.IsKeywordEnabled("_OCCLUSIONMAP"),
                otherBoardRenderersBaked=board.GetComponentsInChildren<MeshRenderer>(true).Where(c=>!coins.Contains(c)).All(c=>c.sharedMaterials.All(m=>m.shader.name=="Journey/BakedSurfaceURP"||m.shader.name=="Universal Render Pipeline/Lit")),
                savedMaterialVerified=true};
        }
        finally{EditorSceneManager.ClosePreviewScene(preview);}
    }
}
