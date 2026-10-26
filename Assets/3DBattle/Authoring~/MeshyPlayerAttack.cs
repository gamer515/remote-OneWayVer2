using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
public static class MeshyPlayerAttack {
 const string Output="Assets/3DBattle/Animations/MeshyWeaponCombo_Player.anim";
 public static async System.Threading.Tasks.Task<string> Run(){
 if(EditorApplication.isPlaying)throw new Exception("Stop play");
 string path="Assets/3DBattle/MeshyPlayer/Meshy_AI_KnightCharacter_Weapon_Combo.fbx";
 var clip=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__"));
 var src=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
 var dst=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3DBattle/Prefabs/BattleKnight.prefab"));
 src.hideFlags=dst.hideFlags=HideFlags.HideAndDontSave;dst.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
 try{
 var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/3DBattle/Animations/KnightBattle.controller");
 controller.animationClips.First(c=>c.name.EndsWith("Idle_swordRight")).SampleAnimation(dst,0);
 var sr=src.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.name=="Knight");var dr=dst.GetComponentInChildren<SkinnedMeshRenderer>();
 var sb=sr.bones.Select((b,i)=>new{b.name,Matrix=sr.transform.localToWorldMatrix*sr.sharedMesh.bindposes[i].inverse}).ToDictionary(x=>x.name,x=>x.Matrix);
 var db=dr.bones.Select((b,i)=>new{b.name,Matrix=dr.transform.localToWorldMatrix*dr.sharedMesh.bindposes[i].inverse}).ToDictionary(x=>x.name,x=>x.Matrix);
 var st=src.GetComponentsInChildren<Transform>().GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
 var map=new Dictionary<string,string>{{"Body","Hips"},{"Hips","Hips"},{"Abdomen","Spine02"},{"Torso","Spine"},{"Neck","neck"},{"Head","Head"}};
 foreach(var side in new[]{"L","R"}){string s=side=="L"?"Left":"Right";map["Shoulder."+side]=s+"Shoulder";map["UpperArm."+side]=s+"Arm";map["LowerArm."+side]=s+"ForeArm";map["Palm."+side]=s+"Hand";map["UpperLeg."+side]=s+"UpLeg";map["LowerLeg."+side]=s+"Leg";map["Foot."+side]=s+"Foot";}
 var bones=dst.GetComponentsInChildren<Transform>().Where(t=>dr.bones.Contains(t)).ToArray();
 Vector3 sourceRight=(Vector3)(sb["RightArm"].GetColumn(3)-sb["LeftArm"].GetColumn(3));sourceRight.y=0;
 Vector3 targetRight=(Vector3)(db["UpperArm.R"].GetColumn(3)-db["UpperArm.L"].GetColumn(3));targetRight.y=0;
 Quaternion align=Quaternion.FromToRotation(sourceRight,targetRight);
 float scale=Vector3.Distance(db["Head"].GetColumn(3),db["Body"].GetColumn(3))/Vector3.Distance(sb["Head"].GetColumn(3),sb["Hips"].GetColumn(3));
 var curves=bones.Select(t=>Enumerable.Range(0,7).Select(_=>new AnimationCurve()).ToArray()).ToArray();var previous=new Quaternion[bones.Length];
 int frames=Mathf.CeilToInt(clip.length*60);clip.SampleAnimation(src,0);var initialHip=st["Hips"].position;
 for(int f=0;f<=frames;f++){
 float time=clip.length*f/frames;clip.SampleAnimation(src,time);var travel=st["Hips"].position-initialHip;travel.y=0;
 foreach(var b in bones)if(map.TryGetValue(b.name,out var source)){
 var rest=sb[source];var target=db[b.name];
 var pos=(Vector3)target.GetColumn(3)+align*(st[source].position-(Vector3)rest.GetColumn(3)-travel)*scale;
 var rot=align*st[source].rotation*Quaternion.Inverse(rest.rotation)*Quaternion.Inverse(align)*target.rotation;
 b.SetPositionAndRotation(pos,rot);
 }
 for(int i=0;i<bones.Length;i++){var p=bones[i].localPosition;var q=bones[i].localRotation;if(f>0&&Quaternion.Dot(previous[i],q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous[i]=q;var v=new[]{p.x,p.y,p.z,q.x,q.y,q.z,q.w};for(int j=0;j<7;j++)curves[i][j].AddKey(time,v[j]);}
 }
 var output=new AnimationClip{frameRate=60,name="MeshyWeaponCombo_Player"};string[] props={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w"};
 for(int i=0;i<bones.Length;i++)for(int j=0;j<7;j++){var c=curves[i][j];for(int k=0;k<c.length;k++){AnimationUtility.SetKeyLeftTangentMode(c,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(c,k,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(output,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones[i],dst.transform),typeof(Transform),props[j]),c);}
 output.EnsureQuaternionContinuity();var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(Output);if(existing==null)AssetDatabase.CreateAsset(output,Output);else{EditorUtility.CopySerialized(output,existing);UnityEngine.Object.DestroyImmediate(output);output=existing;EditorUtility.SetDirty(output);AssetDatabase.SaveAssetIfDirty(output);}
 var state=controller.layers[0].stateMachine.states.Single(s=>s.state.name=="AttackNew").state;state.motion=output;EditorUtility.SetDirty(state);EditorUtility.SetDirty(controller);AssetDatabase.SaveAssetIfDirty(controller);
 await Render(dst,output);
 return "Retargeted "+clip.name+" duration="+clip.length+" mapped bones="+map.Count+" scale="+scale+"; AttackNew connected";
 }finally{UnityEngine.Object.DestroyImmediate(src);UnityEngine.Object.DestroyImmediate(dst);}
 }
 static async System.Threading.Tasks.Task Render(GameObject g,AnimationClip clip){
 foreach(var t in g.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
 var camera=new GameObject("Preview Camera");var light=new GameObject("Preview Light");var rt=new RenderTexture(480,640,24);var tex=new Texture2D(480,640,TextureFormat.RGB24,false);var old=RenderTexture.active;
 try{var c=camera.AddComponent<Camera>();c.cullingMask=1<<31;c.orthographic=true;c.nearClipPlane=.01f;c.farClipPlane=1000;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.16f,.16f,.16f);c.targetTexture=rt;var l=light.AddComponent<Light>();l.type=LightType.Directional;l.intensity=1;l.transform.rotation=Quaternion.Euler(40,-30,0);
 for(int i=0;i<5;i++){clip.SampleAnimation(g,clip.length*i/5f);EditorApplication.QueuePlayerLoopUpdate();await System.Threading.Tasks.Task.Delay(150);foreach(var r in g.GetComponentsInChildren<SkinnedMeshRenderer>())r.updateWhenOffscreen=true;var bounds=g.GetComponentInChildren<SkinnedMeshRenderer>().bounds;c.orthographicSize=Mathf.Max(bounds.size.y,bounds.size.x)*.8f;c.transform.position=bounds.center+new Vector3(2,1,4).normalized*20;c.transform.LookAt(bounds.center);var sk=g.GetComponentInChildren<SkinnedMeshRenderer>();var baked=new Mesh();sk.BakeMesh(baked);var proxy=new GameObject("Baked preview");proxy.layer=31;proxy.transform.SetPositionAndRotation(sk.transform.position,sk.transform.rotation);proxy.transform.localScale=Vector3.one;proxy.AddComponent<MeshFilter>().sharedMesh=baked;proxy.AddComponent<MeshRenderer>().sharedMaterials=sk.sharedMaterials;sk.enabled=false;c.Render();sk.enabled=true;UnityEngine.Object.DestroyImmediate(proxy);UnityEngine.Object.DestroyImmediate(baked);RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,480,640),0,0);tex.Apply();File.WriteAllBytes("Assets/3DBattle/Authoring~/MeshyAttackPreview"+i+".png",tex.EncodeToPNG());}
 }finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(camera);UnityEngine.Object.DestroyImmediate(light);}
 }
}





