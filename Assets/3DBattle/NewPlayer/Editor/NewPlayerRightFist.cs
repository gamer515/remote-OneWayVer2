using System;
using System.Linq;
using UnityEngine;
using UnityEditor;

// NewPlayer characters use a permanently closed right hand; the left hand stays original.
public sealed class NewPlayerRightFist : AssetPostprocessor
{
 public override uint GetVersion()=>1;
 void OnPostprocessModel(GameObject model)
 {
  if(!assetPath.StartsWith("Assets/3DBattle/NewPlayer/",StringComparison.OrdinalIgnoreCase))return;
  foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))Apply(skin);
 }
 public static int Apply(SkinnedMeshRenderer skin)
 {
  var mesh=skin.sharedMesh;if(mesh==null||mesh.name.EndsWith("_RightFist"))return 0;
  int hand=Array.FindIndex(skin.bones,b=>b.name=="RightHand");
  int forearm=Array.FindIndex(skin.bones,b=>b.name=="RightLowerArm"||b.name=="RightForeArm");
  if(hand<0||forearm<0)return 0;
  var bind=mesh.bindposes;Vector3 wrist=bind[hand].inverse.GetColumn(3);Vector3 elbow=bind[forearm].inverse.GetColumn(3);
  Vector3 axis=(wrist-elbow).normalized;Vector3 up=Vector3.ProjectOnPlane(Vector3.up,axis).normalized;
  var vertices=mesh.vertices;var weights=mesh.boneWeights;
  Func<int,float> influence=i=>{var w=weights[i];return (w.boneIndex0==hand?w.weight0:0)+(w.boneIndex1==hand?w.weight1:0)+(w.boneIndex2==hand?w.weight2:0)+(w.boneIndex3==hand?w.weight3:0);};
  float length=Enumerable.Range(0,vertices.Length).Where(i=>influence(i)>.5f).Select(i=>Vector3.Dot(vertices[i]-wrist,axis)).DefaultIfEmpty(0).Max();
  if(length<=.0001f)return 0;
  float start=length*.32f,angle=155f*Mathf.Deg2Rad,radius=(length-start)/angle;
  int changed=0;
  for(int i=0;i<vertices.Length;i++)
  {
   float distance=Vector3.Dot(vertices[i]-wrist,axis);if(influence(i)<.4f||distance<=start)continue;
   float theta=Mathf.Clamp01((distance-start)/(length-start))*angle;
   Vector3 side=vertices[i]-wrist-axis*distance;float thickness=Vector3.Dot(side,up);side-=up*thickness;
   vertices[i]=wrist+axis*(start+(radius+thickness)*Mathf.Sin(theta))+up*((radius+thickness)*Mathf.Cos(theta)-radius)+side;
   changed++;
  }
  mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.name+="_RightFist";return changed;
 }
}
