using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using JourneyMapKit;

// Read-only diagnostic; GPU readback uses temporary resources, no asset/scene changes.
public static class JourneyCoinBakeDiagnostic
{
    public static object Check()
    {
        var readbacks=new Dictionary<Texture,Texture2D>();
        var oldActive=RenderTexture.active;
        try
        {
            Func<Texture,Texture2D> read=source=>
            {
                if(readbacks.TryGetValue(source,out var result))return result;
                var rt=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
                try
                {
                    Graphics.Blit(source,rt);RenderTexture.active=rt;
                    result=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
                    result.ReadPixels(new Rect(0,0,source.width,source.height),0,0);result.Apply();
                    readbacks.Add(source,result);return result;
                }
                finally{RenderTexture.active=oldActive;RenderTexture.ReleaseTemporary(rt);}
            };
            return new[]{"Blue","Red","Yellow","Teal"}.Select((color,colorIndex)=>new {
                color,
                coins=Enumerable.Range(0,14).Select(index=>{
                    int globalIndex=colorIndex*14+index;
                    string name=color+"_StoredCoin"+(globalIndex==0?"":"."+globalIndex.ToString("D3"));
                    var mesh=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Decision_YYS/JourneyV4Integration/"+name+".asset");
                    var vertices=mesh.vertices;var normals=mesh.normals;var uv=mesh.uv2;
                    var materials=new[]{"AgedBrass","CoinGold","HoneyOak"}.Select(m=>AssetDatabase.LoadAssetAtPath<Material>("Assets/AiAsset/JourneyInterfaceV4Baked/Materials/assembly__CoinSupplyRack_Canister_"+color+"_Visuals_"+m+".mat")).ToArray();
                    double weighted=0,areaTotal=0;int samples=0;
                    for(int sub=0;sub<mesh.subMeshCount;sub++)
                    {
                        var texture=materials[sub].GetTexture("_BakedTexture");if(texture==null)continue;
                        var readable=read(texture);var triangles=mesh.GetTriangles(sub);
                        for(int i=0;i<triangles.Length;i+=3)
                        {
                            int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                            if((normals[a].y+normals[b].y+normals[c].y)/3f<.95f)continue;
                            float area=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).magnitude/2;
                            var point=(uv[a]+uv[b]+uv[c])/3f;var color=readable.GetPixelBilinear(point.x,point.y);
                            weighted+=area*(color.r*.2126+color.g*.7152+color.b*.0722);areaTotal+=area;samples++;
                        }
                    }
                    return new {index=index+1,name,
                        topBakedLuminance=areaTotal>0?weighted/areaTotal:0,topFaceSamples=samples,
                        gain=materials[0].GetFloat("_Gain"),shader=materials[0].shader.name};
                }).ToArray()
            }).ToArray();
        }
        finally{RenderTexture.active=oldActive;foreach(var texture in readbacks.Values)UnityEngine.Object.DestroyImmediate(texture);}
    }
}
