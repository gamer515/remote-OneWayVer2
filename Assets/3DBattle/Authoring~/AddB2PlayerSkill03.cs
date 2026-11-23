using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using OneWay.Battle3D;
public static class AddB2PlayerSkill03
{
 const string Folder="Assets/3DBattle/Boss2/BossAnim/Crisp/";
 static Dictionary<string,Matrix4x4> Rest(GameObject g)
 {
  var d=new Dictionary<string,Matrix4x4>();foreach(var s in g.GetComponentsInChildren<SkinnedMeshRenderer>())for(int i=0;i<s.bones.Length;i++)if(s.bones[i])d[s.bones[i].name]=g.transform.worldToLocalMatrix*s.transform.localToWorldMatrix*s.sharedMesh.bindposes[i].inverse;
  foreach(var t in g.GetComponentsInChildren<Transform>())if(!d.ContainsKey(t.name))d[t.name]=g.transform.worldToLocalMatrix*t.localToWorldMatrix;return d;
 }
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Edit mode required");
  var combo=UnityEngine.Object.FindObjectsByType<NewPlayerAttackCombo>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(x=>x.gameObject.activeInHierarchy);
  var playerAnimator=combo.GetComponentInChildren<Animator>();var motion=combo.motions.attacks[4];
  var bossAnimator=UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(a=>a.name.Contains("Heavy_Hammer")&&a.gameObject.activeInHierarchy);
  var source=UnityEngine.Object.Instantiate(playerAnimator.gameObject);
  var target=UnityEngine.Object.Instantiate(BattleCharacterAssetPaths.Load<GameObject>(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(bossAnimator.gameObject))));
  try
  {
   source.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);source.transform.localScale=Vector3.one;
   foreach(var a in source.GetComponentsInChildren<Animator>())a.enabled=false;
   var sr=Rest(source);var tr=Rest(target);
   var src=source.GetComponentsInChildren<Transform>().GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
   var rig=target.transform.Find("target_character");var nodes=rig.GetComponentsInChildren<Transform>().Where(t=>t!=rig).ToArray();
   foreach(var t in nodes)if(tr.ContainsKey(t.name))t.SetPositionAndRotation(tr[t.name].GetColumn(3),tr[t.name].rotation);
   var localPositions=nodes.Select(t=>t.localPosition).ToArray();var localRotations=nodes.Select(t=>t.localRotation).ToArray();var localScales=nodes.Select(t=>t.localScale).ToArray();
   var hips=nodes.Single(t=>t.name=="Hips");var head=nodes.Single(t=>t.name=="Head");var front=nodes.Single(t=>t.name=="headfront");
   motion.clip.SampleAnimation(source,0);float startHeight=src["Hips"].position.y;
   float ratio=Vector3.Distance(tr["Head"].GetColumn(3),tr["Hips"].GetColumn(3))/Vector3.Distance(sr["Head"].GetColumn(3),sr["Hips"].GetColumn(3));
   Quaternion heading=Quaternion.identity;
   void Pose(float time)
   {
    motion.clip.SampleAnimation(source,time);
    for(int i=0;i<nodes.Length;i++){nodes[i].localPosition=localPositions[i];nodes[i].localRotation=localRotations[i];nodes[i].localScale=localScales[i];}
    foreach(var t in nodes)
    {
     string key=t.name=="Spine"?"Chest":t.name;
     if(!src.ContainsKey(key))continue;
     Quaternion animated=src[key].rotation,rest=sr[key].rotation;
     if(t.name=="Chest") {animated=Quaternion.Slerp(src["Chest"].rotation,src["UpperChest"].rotation,.5f);rest=Quaternion.Slerp(sr["Chest"].rotation,sr["UpperChest"].rotation,.5f);}
     t.rotation=heading*animated*Quaternion.Inverse(rest)*tr[t.name].rotation;
    }
    hips.position=(Vector3)tr["Hips"].GetColumn(3)+Vector3.up*((src["Hips"].position.y-startHeight)*ratio);
   }
   Pose(0);heading=Quaternion.FromToRotation(Vector3.ProjectOnPlane(front.position-head.position,Vector3.up).normalized,Vector3.forward);
   var clip=new AnimationClip{name="B2_Skill03_FromPlayer",frameRate=30};
   var curves=nodes.Select(t=>Enumerable.Range(0,10).Select(_=>new AnimationCurve()).ToArray()).ToArray();var previous=new Quaternion[nodes.Length];
   // Hold the first pose while the incoming crossfade completes. The actual
   // slash occupies the first six source frames and must start at full weight.
   var incomingController=(AnimatorController)bossAnimator.runtimeAnimatorController;
   float entrySeconds=incomingController.layers[0].stateMachine.states.SelectMany(s=>s.state.transitions).Where(t=>t.destinationState&&t.destinationState.name=="Skill03").Select(t=>t.duration).DefaultIfEmpty(.12f).Max();
   int entryFrames=Mathf.CeilToInt(entrySeconds*combo.playbackSpeed*30f)+2;
   int end=Mathf.RoundToInt(motion.Frames)+entryFrames;
   for(int frame=0;frame<=end;frame++)
   {
    Pose(Mathf.Max(0,frame-entryFrames)/30f);
    for(int i=0;i<nodes.Length;i++)
    {
     var t=nodes[i];var p=t.localPosition;var q=t.localRotation;var s=t.localScale;
     if(frame>0&&Quaternion.Dot(previous[i],q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous[i]=q;
     float[] v={p.x,p.y,p.z,q.x,q.y,q.z,q.w,s.x,s.y,s.z};for(int j=0;j<10;j++)curves[i][j].AddKey(new Keyframe(frame/30f,v[j]));
    }
   }
   string[] props={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w","m_LocalScale.x","m_LocalScale.y","m_LocalScale.z"};
   for(int i=0;i<nodes.Length;i++)for(int j=0;j<10;j++){var c=curves[i][j];for(int k=0;k<c.length;k++){AnimationUtility.SetKeyLeftTangentMode(c,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(c,k,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(nodes[i],target.transform),typeof(Transform),props[j]),c);}
   clip.EnsureQuaternionContinuity();float strikeFrame=motion.damageWindows[0].x+entryFrames;
   AnimationUtility.SetAnimationEvents(clip,new[]{new AnimationEvent{time=strikeFrame/30f,functionName="EmitB2Skill03Slash"}});
   string clipPath=Folder+"B2_Skill03_FromPlayer.anim";var existing=BattleCharacterAssetPaths.Load<AnimationClip>(clipPath);if(existing){EditorUtility.CopySerialized(clip,existing);UnityEngine.Object.DestroyImmediate(clip);clip=existing;}else AssetDatabase.CreateAsset(clip,clipPath);
   var controller=(AnimatorController)bossAnimator.runtimeAnimatorController;
   string backup=Folder+"B2_CrispSequence_BeforeSkill03.controller";if(!BattleCharacterAssetPaths.Load<AnimatorController>(backup))AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(controller),backup);
   var sm=controller.layers[0].stateMachine;var state=sm.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Skill03");if(!state)state=sm.AddState("Skill03",new Vector3(780,0,0));state.motion=clip;state.speed=combo.playbackSpeed;
   var weapon=sm.states.Select(s=>s.state).Single(s=>s.name=="WeaponCombo");var power=sm.states.Select(s=>s.state).Single(s=>s.name=="PowerSlash");
   foreach(var transition in weapon.transitions.Where(t=>t.destinationState==power)){Undo.RecordObject(transition,"Add B2 Skill03 to sequence");transition.destinationState=state;}
   if(!weapon.transitions.Any(t=>t.destinationState==state))throw new Exception("WeaponCombo transition not found");
   if(!state.transitions.Any(t=>t.destinationState==power)){var t=state.AddTransition(power);t.hasExitTime=true;t.exitTime=1;t.hasFixedDuration=true;t.duration=.12f;}
   EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
   int missing=AnimationUtility.GetCurveBindings(clip).Count(b=>b.path!=""&&bossAnimator.transform.Find(b.path)==null);if(missing>0)throw new Exception("Missing targets: "+missing);
   return new{clip=clipPath,source=AssetDatabase.GetAssetPath(motion.clip),frames=end,entryFrames,stateSpeed=state.speed,strikeFrame,missingPaths=missing};
  }
  finally{UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(target);}
 }
}
