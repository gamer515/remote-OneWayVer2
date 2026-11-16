using System;using UnityEngine;using UnityEditor;
public static class FitCathedralAlpha{
 public static string Run(){string folder="Assets/3DBattle/Boss2/Cathedral/StageAssets/";
 foreach(var name in new[]{"Wall Front","Wall Entrance"}){
 var mat=AssetDatabase.LoadAssetAtPath<Material>(folder+(name=="Wall Front"?"Cathedral Front":"Cathedral Entrance")+".mat");var tex=new Texture2D(2,2);tex.LoadImage(System.IO.File.ReadAllBytes(AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap"))));
 try{var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(folder+name+".asset");var uv=mesh.uv;
 for(int col=0;col<=64;col++){float x=uv[col].x;int px=Mathf.RoundToInt(x*(tex.width-1));int top=tex.height-1;while(top>0&&tex.GetPixel(px,top).a<.95f)top--;float upper=(top-3f)/tex.height;
 for(int row=17;row<=32;row++){int i=row*65+col;uv[i].y=Mathf.Lerp(1-570f/1293f,upper,(row-16)/16f);}}
 mesh.uv=uv;EditorUtility.SetDirty(mesh);
 }finally{UnityEngine.Object.DestroyImmediate(tex);}}
 AssetDatabase.SaveAssets();return "Front and entrance arch UV fitted inside opaque image silhouette";
 }
}
