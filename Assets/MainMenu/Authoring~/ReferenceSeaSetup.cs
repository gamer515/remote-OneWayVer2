using System;
using UnityEngine;
using UnityEditor;
public static class ReferenceSeaSetup
{
    public static string Apply()
    {
        if(Application.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!="Assets/Scenes/MainMenuScene.unity") throw new Exception("MainMenuScene edit mode required");
        AssetDatabase.ImportAsset("Assets/MainMenu/Coast/MenuSea.shader",ImportAssetOptions.ForceUpdate);
        var mat=AssetDatabase.LoadAssetAtPath<Material>("Assets/MainMenu/Coast/Menu Sea.mat");
        Undo.RecordObject(mat,"Reference sea colors and foam");
        mat.SetColor("_DeepColor",new Color(.015f,.30f,.85f));
        mat.SetColor("_ShallowColor",new Color(.015f,.80f,.66f));
        mat.SetFloat("_WaveHeight",.16f);mat.SetFloat("_FoamStrength",.95f);
        mat.SetFloat("_FoamWidth",1.4f);mat.SetFloat("_SparkleStrength",.8f);
        EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        return "Shader errors="+ShaderUtil.ShaderHasError(mat.shader);
    }
}
