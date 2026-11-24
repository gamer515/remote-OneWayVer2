using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TaxCollectionDelivery
{
    static void ExportAssetsOnly()
    {
        const string root="Assets/ThreeFingersTaxCollection";
        var owned=AssetDatabase.FindAssets("",new[]{root}).Select(AssetDatabase.GUIDToAssetPath).ToArray();
        var paths=AssetDatabase.GetDependencies(owned,true).Concat(owned).Where(p=>p.StartsWith("Assets/")).Distinct().ToArray();
        // The destination game already uses URP. Never package UPM shader/editor sources.
        AssetDatabase.ExportPackage(paths,"output/ThreeFingersTaxCollection/ThreeFingers_TaxCollection.unitypackage",(ExportPackageOptions)0);
    }
    public static string RefreshPackage()
    {
        const string root="Assets/ThreeFingersTaxCollection";
        AssetDatabase.ImportAsset(root+"/Documentation",ImportAssetOptions.ImportRecursive|ImportAssetOptions.ForceSynchronousImport);
        ExportAssetsOnly();
        return "Package refreshed with integration instructions";
    }
    public static string Publish()
    {
        const string root="Assets/ThreeFingersTaxCollection";
        const string staged="AssetBuild/ThreeFingersTaxCollection/ValidationProject/Assets/ThreeFingersTaxCollection";
        const string output="output/ThreeFingersTaxCollection";
        var result=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(output+"/validation.json"));
        if(!(bool)result["passed"])throw new Exception("Playback validation must pass before publishing");
        if(Directory.Exists(root))throw new Exception("Delivery folder already exists; inspect it before replacing anything");
        string scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
        bool dirty=UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;
        foreach(string f in Directory.GetFiles(staged,"*",SearchOption.AllDirectories))
        {
            string target=root+f.Substring(staged.Length);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(f,target,false);
        }
        File.Copy(staged+".meta",root+".meta",false);
        AssetDatabase.ImportAsset(root,ImportAssetOptions.ImportRecursive|ImportAssetOptions.ForceSynchronousImport);
        // Only the new preview-table materials need conversion to the game's URP renderer.
        var shader=Shader.Find("Universal Render Pipeline/Lit");
        if(shader)foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{root+"/Materials"})){var m=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));var color=m.color;m.shader=shader;m.color=color;AssetDatabase.SaveAssetIfDirty(m);}
        var clips=AssetDatabase.FindAssets("t:AnimationClip",new[]{root}).Select(g=>AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(g))).Select(c=>new{name=c.name,length=c.length,fps=c.frameRate,curves=AnimationUtility.GetCurveBindings(c).Length,events=AnimationUtility.GetAnimationEvents(c).Select(e=>new{frame=Mathf.RoundToInt(e.time*60),function=e.functionName}).ToArray()}).ToArray();
        ExportAssetsOnly();
        if(scene!=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path||dirty!=UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)throw new Exception("Unexpected active scene change");
        string json=Newtonsoft.Json.JsonConvert.SerializeObject(new{clips,originalScene=scene,scenePreserved=true,package=output+"/ThreeFingers_TaxCollection.unitypackage"},Newtonsoft.Json.Formatting.Indented);
        File.WriteAllText(output+"/delivery.json",json);return json;
    }
}
