using UnityEngine;using UnityEditor;
public static class SealB2Cathedral{
 public static string Run(){foreach(var name in new[]{"Wall Front","Wall Entrance"}){
 var m=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/3DBattle/Boss2/Cathedral/StageAssets/"+name+".asset");var vertices=m.vertices;var uv=m.uv;
 for(int row=0;row<=32;row++)for(int col=0;col<=64;col++){float u=col/64f,v=row/32f,a=u*Mathf.PI,s=-Mathf.Cos(a);int i=row*65+col;float y=v<=.5f?v*48:24+(v-.5f)*36*Mathf.Sin(a);vertices[i]=new Vector3(name=="Wall Front"?-36:36,y,s*36);float top=42+530*Mathf.Pow(Mathf.Abs(s),1.22f);float py=v<=.5f?Mathf.Lerp(1265,570,v*2):Mathf.Lerp(570,top,(v-.5f)*2);uv[i]=new Vector2(Mathf.Lerp(.069f,.931f,(s+1)*.5f),1-py/1293f);}
 m.vertices=vertices;m.uv=uv;m.RecalculateNormals();m.RecalculateBounds();EditorUtility.SetDirty(m);
 }AssetDatabase.SaveAssets();return "Arch vertices match ceiling exactly";}
}
