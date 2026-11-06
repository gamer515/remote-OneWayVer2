using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
public static class InspectKnightParts
{
 public static object Run()
 {
  var skin=GameObject.Find("Running Knight Preview").GetComponentInChildren<SkinnedMeshRenderer>();var m=skin.sharedMesh;var v=m.vertices;var tris=m.triangles;var parents=Enumerable.Range(0,v.Length).ToArray();
  Func<int,int> find=null;find=i=>parents[i]==i?i:parents[i]=find(parents[i]);Action<int,int> join=(a,b)=>parents[find(a)]=find(b);
  var seen=new Dictionary<string,int>();for(int i=0;i<v.Length;i++){string key=v[i].ToString("F5");if(seen.TryGetValue(key,out int j))join(i,j);else seen[key]=i;}
  for(int i=0;i<tris.Length;i+=3){join(tris[i],tris[i+1]);join(tris[i],tris[i+2]);}
  var groups=Enumerable.Range(0,v.Length).GroupBy(find).OrderByDescending(g=>g.Count()).ToArray();var w=m.boneWeights;
  return groups.Select((g,n)=>new{n,count=g.Count(),min=new[]{g.Min(i=>v[i].x),g.Min(i=>v[i].y),g.Min(i=>v[i].z)},max=new[]{g.Max(i=>v[i].x),g.Max(i=>v[i].y),g.Max(i=>v[i].z)},bones=g.GroupBy(i=>skin.bones[w[i].boneIndex0].name).Select(b=>b.Key+":"+b.Count()).ToArray()}).ToArray();
 }
}
