using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;

public static class AdjustRunningShield
{
 static NewPlayerLocomotion Live => UnityEngine.Object.FindFirstObjectByType<NewPlayerLocomotion>();
 public static object Inspect()
 {
  var loco=Live;var player=loco.GetComponent<BattlePlayerController>();var shield=player.shield.transform;
  return new{scene=loco.gameObject.scene.path,player=loco.name,run=AssetDatabase.GetAssetPath(loco.runClip),frames=loco.runClip.length*loco.runClip.frameRate,
   visual=player.animator.transform.parent.name,shield=shield.name,parent=shield.parent.name,position=shield.localPosition.ToString("F5"),rotation=shield.localEulerAngles.ToString("F3"),scale=shield.localScale.ToString(),worldScale=shield.lossyScale.ToString(),idleRotation=loco.idleShieldRotationOffset.ToString(),idlePosition=loco.idleShieldPositionOffset.ToString(),
   mountVector=(Quaternion.Inverse(shield.localRotation)*shield.localPosition*shield.parent.lossyScale.x).ToString("F5"),staticMeshes=player.animator.GetComponentsInChildren<MeshFilter>(true).Select(m=>m.name).ToArray(),
   meshes=shield.GetComponentsInChildren<MeshFilter>(true).Select(m=>new{m.name,vertices=m.sharedMesh.vertexCount,bounds=m.sharedMesh.bounds.ToString("F5")}),
   skin=player.animator.GetComponentsInChildren<SkinnedMeshRenderer>().Select(s=>new{s.name,vertices=s.sharedMesh.vertexCount,bones=s.bones.Select(t=>t.name).ToArray()})};
 }
 static Vector3[] SkinVertices(SkinnedMeshRenderer skin)
 {
  var mesh=skin.sharedMesh;var v=mesh.vertices;var w=mesh.boneWeights;var matrices=skin.bones.Select((b,i)=>b.localToWorldMatrix*mesh.bindposes[i]).ToArray();
  for(int i=0;i<v.Length;i++){var p=v[i];var b=w[i];v[i]=matrices[b.boneIndex0].MultiplyPoint3x4(p)*b.weight0+matrices[b.boneIndex1].MultiplyPoint3x4(p)*b.weight1+matrices[b.boneIndex2].MultiplyPoint3x4(p)*b.weight2+matrices[b.boneIndex3].MultiplyPoint3x4(p)*b.weight3;}
  return v;
 }
 public static object Review() => Render(Vector3.zero,"RunShieldBefore");
 public static object ReviewCandidate() => Render(new Vector3(0,0,10),"RunShieldCandidate",0,true);
 public static object Search() => SearchInternal(false);
 public static object SearchStable() => SearchInternal(true);
 public static object ValidateClearance() => SearchInternal(true,true);
 public static object Apply()
 {
  if(Application.isPlaying)throw new Exception("Edit mode required");var loco=Live;
  Undo.RecordObject(loco,"Correct running shield pose");loco.correctRunningShield=true;loco.runningShieldOutwardTilt=10;
  EditorUtility.SetDirty(loco);EditorSceneManager.MarkSceneDirty(loco.gameObject.scene);EditorSceneManager.SaveScene(loco.gameObject.scene);
  Selection.activeGameObject=loco.gameObject;UnityEditorInternal.InternalEditorUtility.SetIsInspectorExpanded(loco,true);
  return new{loco.name,loco.correctRunningShield,loco.runningShieldOutwardTilt,saved=!loco.gameObject.scene.isDirty};
 }
 public static object RuntimeChecks()
 {
  if(!Application.isPlaying)throw new Exception("Play mode required");var loco=Live;var player=loco.GetComponent<BattlePlayerController>();var combo=loco.GetComponent<NewPlayerAttackCombo>();
  var intro=UnityEngine.Object.FindFirstObjectByType<BattleIntroWalkDebug>();if(intro)intro.Stop();
  Time.timeScale=0;player.enabled=false;combo.readMouseInput=false;player.CancelInput();loco.enabled=true;loco.SetDefenseMode(true);
  var shield=player.shield.transform;var attachment=shield.localRotation;var position=shield.localPosition;var scale=shield.localScale;
  var apply=typeof(NewPlayerLocomotion).GetMethod("ApplyShieldPose",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
  Action refresh=()=>apply.Invoke(loco,new object[]{.1f});
  var chest=player.animator.GetComponentsInChildren<Transform>(true).First(t=>t.name=="UpperChest");var right=player.animator.GetComponentsInChildren<Transform>(true).First(t=>t.name=="RightHand");
  try{
   loco.StartRoll();loco.Tick(loco.rollClip.length/Mathf.Max(.1f,player.rollAnimationSpeed)+.01f);if(!loco.Running)throw new Exception("Run did not start");
   float maximumError=0;
   for(int f=0;f<=120;f++){
    player.animator.Play("Base Layer."+loco.runClip.name,0,f/120f);player.animator.Update(0);var swordHand=right.localRotation;refresh();
    maximumError=Mathf.Max(maximumError,Quaternion.Angle(shield.rotation,RadialRotation(shield.parent.position,chest.position,loco.runningShieldOutwardTilt)));
    if(Quaternion.Angle(swordHand,right.localRotation)>.05f||Quaternion.Angle(shield.localRotation,attachment)>.05f||Vector3.Distance(shield.localPosition,position)>.0001f||shield.localScale!=scale)throw new Exception("Grip, scale or sword hand changed");
   }
   if(maximumError>.1f)throw new Exception("Running shield angle mismatch: "+maximumError);
   loco.StartParry();var parryHand=shield.parent.localRotation;refresh();if(Quaternion.Angle(parryHand,shield.parent.localRotation)>.05f||Quaternion.Angle(shield.localRotation,attachment)>.05f)throw new Exception("Parry was changed");
   loco.StartRoll();var rollHand=shield.parent.localRotation;refresh();if(Quaternion.Angle(rollHand,shield.parent.localRotation)>.05f||Quaternion.Angle(shield.localRotation,attachment)>.05f)throw new Exception("Roll was changed");
   loco.Stop();player.animator.Play("Base Layer.Idle",0,0);player.animator.Update(0);var idleHand=shield.parent.localRotation;refresh();
   if(Quaternion.Angle(idleHand,shield.parent.localRotation)>.05f||Quaternion.Angle(shield.localRotation,attachment*Quaternion.Euler(loco.idleShieldRotationOffset))>.05f)throw new Exception("Idle correction changed");
   return new{samples=121,maximumAngleError=maximumError,attachmentAndSizeUnchanged=true,rightHandUnchanged=true,parryUnchanged=true,rollUnchanged=true,idleUnchanged=true};
  }finally{loco.Stop();loco.SetDefenseMode(false);refresh();Time.timeScale=1;}
 }
 static object SearchInternal(bool stable,bool validate=false)
 {
  var loco=Live;var player=loco.GetComponent<BattlePlayerController>();var visual=UnityEngine.Object.Instantiate(player.animator.transform.parent.gameObject);visual.hideFlags=HideFlags.HideAndDontSave;
  var owned=new List<UnityEngine.Object>{visual};
  try{
   visual.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);var animator=visual.GetComponentInChildren<Animator>();animator.enabled=false;
   var shield=visual.GetComponentsInChildren<Transform>(true).First(t=>t.name==player.shield.name);var skin=visual.GetComponentInChildren<SkinnedMeshRenderer>();var weights=skin.sharedMesh.boneWeights;
   Func<int,float> mountWeight=i=>{var w=weights[i];int[] ids={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};float[] ws={w.weight0,w.weight1,w.weight2,w.weight3};float sum=0;for(int j=0;j<4;j++)if(skin.bones[ids[j]].name=="LeftHand"||skin.bones[ids[j]].name=="LeftLowerArm")sum+=ws[j];return sum;};
   var sourceTriangles=skin.sharedMesh.triangles;var triangles=new List<int>();for(int i=0;i<sourceTriangles.Length;i+=3)if((mountWeight(sourceTriangles[i])+mountWeight(sourceTriangles[i+1])+mountWeight(sourceTriangles[i+2]))/3<.5f)triangles.AddRange(new[]{sourceTriangles[i],sourceTriangles[i+1],sourceTriangles[i+2]});
   var bodies=new List<MeshCollider>();var positions=new List<Vector3>();var rotations=new List<Quaternion>();var hands=new List<Vector3>();var chests=new List<Vector3>();var mountVector=Quaternion.Inverse(shield.localRotation)*shield.localPosition*shield.parent.lossyScale.x;
   int sampleCount=validate?121:20;
   for(int f=0;f<sampleCount;f++){
    loco.runClip.SampleAnimation(animator.gameObject,loco.runClip.length*f/(sampleCount-1));positions.Add(shield.position);rotations.Add(shield.rotation);hands.Add(shield.parent.position);chests.Add(skin.bones.First(b=>b.name=="UpperChest").position);
    var verts=SkinVertices(skin).ToList();var tris=new List<int>(triangles);
    foreach(var mf in visual.GetComponentsInChildren<MeshFilter>(true))if(!mf.transform.IsChildOf(shield)&&!mf.name.Contains("Sword")&&mf.GetComponent<Renderer>()?.enabled==true){int start=verts.Count;verts.AddRange(mf.sharedMesh.vertices.Select(v=>mf.transform.TransformPoint(v)));tris.AddRange(mf.sharedMesh.triangles.Select(t=>t+start));}
    var mesh=new Mesh();mesh.vertices=verts.ToArray();mesh.triangles=tris.ToArray();mesh.RecalculateBounds();owned.Add(mesh);var go=new GameObject("Temporary sampled run body");owned.Add(go);var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;bodies.Add(collider);
   }
   var probeGo=new GameObject("Temporary shield probe");owned.Add(probeGo);probeGo.transform.localScale=shield.lossyScale;var probe=probeGo.AddComponent<MeshCollider>();probe.sharedMesh=shield.GetComponent<MeshFilter>().sharedMesh;probe.convex=true;Physics.SyncTransforms();
   Func<Vector3,float[]> score=angles=>{
    float maximum=0,sum=0,count=0,facing=0;var offset=Quaternion.Euler(angles);
    for(int i=0;i<bodies.Count;i++){var rotation=stable?RadialRotation(hands[i],chests[i],angles.z):rotations[i]*offset;var position=stable?hands[i]+rotation*mountVector-Vector3.right*angles.x:positions[i];if(Physics.ComputePenetration(probe,position,rotation,bodies[i],Vector3.zero,Quaternion.identity,out var direction,out var depth)){count++;sum+=depth;maximum=Mathf.Max(maximum,depth);}facing+=Mathf.Max(0,.4f-Vector3.Dot(rotation*Vector3.right,Vector3.right));}
    return new[]{maximum,sum,count,facing};};
   if(validate){var measurement=score(new Vector3(0,0,10));if(measurement[2]!=0)throw new Exception("Body overlap remains: "+string.Join(",",measurement));return new{samples=sampleCount,overlapSamples=measurement[2],maximumPenetration=measurement[0],parts="torso, legs, upper arms, helmet, shoulder pads; grip/forearm excluded"};}
   var candidates=new List<(Vector3 angle,float[] value,float rank)>();
   if(stable){for(int d=0;d<=20;d++)for(int z=0;z<=30;z+=5){var a=new Vector3(d*.01f,0,z);var s=score(a);candidates.Add((a,s,s[0]*1000+s[1]*10+s[2]+d*.01f+z*.0001f));}}
   else{
   for(int x=-180;x<=180;x+=30)for(int y=-90;y<=90;y+=30)for(int z=-90;z<=90;z+=30){var a=new Vector3(x,y,z);var s=score(a);float rank=s[0]*100+s[1]*3+s[2]*.03f+s[3]*2+Quaternion.Angle(Quaternion.identity,Quaternion.Euler(a))*.0001f;candidates.Add((a,s,rank));}
   var best=candidates.OrderBy(c=>c.rank).First();
   for(int x=-15;x<=15;x+=5)for(int y=-15;y<=15;y+=5)for(int z=-15;z<=15;z+=5){var a=best.angle+new Vector3(x,y,z);var s=score(a);float rank=s[0]*100+s[1]*3+s[2]*.03f+s[3]*2+Quaternion.Angle(Quaternion.identity,Quaternion.Euler(a))*.0001f;candidates.Add((a,s,rank));}
   }
   var results=candidates.OrderBy(c=>c.rank).Take(10).Select(c=>new{angle=c.angle.ToString(),maxPenetration=c.value[0],total=c.value[1],overlapSamples=c.value[2],facing=c.value[3]}).ToArray();
   System.IO.File.WriteAllText("Assets/3DBattle/Authoring~/RunShieldBest.txt",candidates.OrderBy(c=>c.rank).First().angle.ToString("F4"));
   return new{baseline=score(Vector3.zero),results};
  }finally{foreach(var item in owned)if(item)UnityEngine.Object.DestroyImmediate(item);}
 }
 static Quaternion RadialRotation(Vector3 hand,Vector3 chest,float tilt)
 {
  Vector3 inward=Vector3.ProjectOnPlane(chest-hand,Vector3.up).normalized;
  return Quaternion.LookRotation(Vector3.up,Vector3.Cross(Vector3.up,inward))*Quaternion.AngleAxis(-tilt,Vector3.up);
 }
 static object Render(Vector3 offset,string name,float outward=0,bool stable=false)
 {
  var loco=Live;var player=loco.GetComponent<BattlePlayerController>();var visual=UnityEngine.Object.Instantiate(player.animator.transform.parent.gameObject);visual.hideFlags=HideFlags.HideAndDontSave;
  var cameraGo=new GameObject("Temporary run shield camera");var cam=cameraGo.AddComponent<Camera>();cam.enabled=false;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.24f,.26f,.29f);cam.fieldOfView=36;
  var rt=new RenderTexture(320,420,24);var tex=new Texture2D(1920,840,TextureFormat.RGB24,false);var active=RenderTexture.active;var draw=new GameObject("Temporary deformed body");var mesh=new Mesh();
  try{
   visual.transform.SetPositionAndRotation(new Vector3(1000,0,1000),Quaternion.identity);var animator=visual.GetComponentInChildren<Animator>();animator.enabled=false;
   var skin=visual.GetComponentInChildren<SkinnedMeshRenderer>();var shield=visual.GetComponentsInChildren<Transform>(true).First(t=>t.name==player.shield.name);shield.gameObject.SetActive(true);var attachment=shield.localRotation;var attachmentPosition=shield.localPosition;
   var filter=draw.AddComponent<MeshFilter>();filter.sharedMesh=mesh;draw.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
   cam.targetTexture=rt;var normals=new List<object>();
   for(int col=0;col<6;col++){
    float time=loco.runClip.length*col/6;loco.runClip.SampleAnimation(animator.gameObject,time);shield.localRotation=attachment*Quaternion.Euler(offset);if(stable){shield.localRotation=attachment;shield.parent.rotation=RadialRotation(shield.parent.position,skin.bones.First(b=>b.name=="UpperChest").position,offset.z)*Quaternion.Inverse(attachment);}shield.localPosition=attachmentPosition;shield.position-=visual.transform.right*outward;
    mesh.Clear();mesh.vertices=SkinVertices(skin);mesh.subMeshCount=skin.sharedMesh.subMeshCount;for(int sub=0;sub<mesh.subMeshCount;sub++)mesh.SetTriangles(skin.sharedMesh.GetTriangles(sub),sub);mesh.RecalculateNormals();mesh.RecalculateBounds();skin.enabled=false;
    var hips=skin.bones.First(t=>t.name=="Hips");var focus=hips.position+Vector3.up*.1f;
    normals.Add(new{frame=time*loco.runClip.frameRate,shieldNormal=shield.right.ToString("F3"),center=(shield.position-visual.transform.position).ToString("F3"),hand=(shield.parent.position-visual.transform.position).ToString("F3")});
    for(int row=0;row<2;row++){
     cam.transform.position=focus+(row==0?new Vector3(-3.2f,1,3.5f):new Vector3(-3.8f,.8f,-3.1f));cam.transform.LookAt(focus);cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,320,420),col*320,(1-row)*420);
    }
   }
   tex.Apply();string path="Assets/3DBattle/Authoring~/"+name+".png";System.IO.File.WriteAllBytes(path,tex.EncodeToPNG());return new{path,normals=normals.ToArray()};
  }finally{RenderTexture.active=active;cam.targetTexture=null;foreach(var obj in new UnityEngine.Object[]{visual,cameraGo,rt,tex,draw,mesh})if(obj)UnityEngine.Object.DestroyImmediate(obj);}
 }
}
