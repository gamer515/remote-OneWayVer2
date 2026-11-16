using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;

public static class BuildB2PhaseSmokeAttack
{
 const string Folder="Assets/3DBattle/Boss2/Phase2";
 static Bounds WorldBounds(BoxCollider box)
 {
  var bounds=new Bounds(box.transform.TransformPoint(box.center),Vector3.zero);
  for(int i=0;i<8;i++)bounds.Encapsulate(box.transform.TransformPoint(box.center+Vector3.Scale(box.size*.5f,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
  return bounds;
 }
 public static object Run()
 {
  if(EditorApplication.isPlaying)throw new Exception("Edit mode required");
  var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
  if(scene.path!="Assets/Scenes/3DBattleScene.unity")throw new Exception("3DBattleScene required");
  var combat=Resources.FindObjectsOfTypeAll<B2CombatController>().Single(c=>c.gameObject.scene==scene);
  var stage=combat.GetComponentInParent<BattleStage>();var boundary=stage.boundary;
  if(!boundary||boundary.walls.Length!=4)throw new Exception("Four B2 boundary walls required");
  if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/3DBattle/Boss2","Phase2");
  var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3DBattle/Boss2/PhasePreview/B2_SmokeSlash_Preview.prefab").GetComponent<B2SmokeSlashPreview>();
  var cloudGo=new GameObject("B2 Phase Floating Gold Smoke");
  var shape=cloudGo.AddComponent<B2SmokeSlashPreview>();shape.enabled=false;shape.playOnStart=false;
  shape.cloudRoot=UnityEngine.Object.Instantiate(source.cloudRoot.gameObject,cloudGo.transform).transform;
  shape.cloudRoot.localPosition=Vector3.zero;shape.cloudRoot.localRotation=Quaternion.identity;
  shape.cloudLayers=shape.cloudRoot.GetComponentsInChildren<ParticleSystem>(true);
  shape.smokeStart=source.smokeStart;shape.smokeEnd=source.smokeEnd;shape.cloudHoldTime=3.6f;shape.holdCloudShape=true;
  shape.cloudFloatDistance=source.cloudFloatDistance;shape.cloudFloatSpeed=source.cloudFloatSpeed;
  shape.StopPreview();var smoke=cloudGo.AddComponent<B2PhaseSmokeCloud>();smoke.shape=shape;
  foreach(var t in cloudGo.GetComponentsInChildren<Transform>(true))t.gameObject.tag="Untagged";
  var cloudPrefab=PrefabUtility.SaveAsPrefabAsset(cloudGo,Folder+"/B2_PhaseSmoke.prefab");UnityEngine.Object.DestroyImmediate(cloudGo);
  var phase=combat.GetComponent<B2PhaseSmokeAttack>();if(!phase)phase=Undo.AddComponent<B2PhaseSmokeAttack>(combat.gameObject);
  phase.combat=combat;phase.swordEffect=combat.animator.GetComponent<B2SwordSlash>();phase.boundary=boundary;
  phase.smokePrefab=cloudPrefab.GetComponent<B2PhaseSmokeCloud>();phase.slashPrefab=phase.swordEffect.slashPrefab;
  phase.damageReference=combat.attacks[0];phase.shotCount=8;phase.smokeSpreadSeconds=3.6f;phase.shotInterval=1;phase.launchDelay=.2f;
  phase.slashSpeed=phase.swordEffect.travelSpeed;phase.slashScale=phase.swordEffect.BossHeight*phase.swordEffect.heightMultiplier;phase.slashHeight=phase.swordEffect.BossHeight*.5f;
  var bounds=WorldBounds(boundary.walls[0]);foreach(var wall in boundary.walls.Skip(1))bounds.Encapsulate(WorldBounds(wall));
  var center=bounds.center;center.y=stage.bossSpawn.position.y;
  float minX=bounds.min.x-4,maxX=bounds.max.x+4,minZ=bounds.min.z-4,maxZ=bounds.max.z+4;
  var points=stage.transform.Find("B2 Phase 2 Smoke Points");
  if(!points){points=new GameObject("B2 Phase 2 Smoke Points").transform;points.SetParent(stage.transform,true);}
  var middle=points.Find("Center Return");if(!middle){middle=new GameObject("Center Return").transform;middle.SetParent(points,false);}middle.position=center;phase.arenaCenter=middle;
  Vector3[] positions={new Vector3(minX,center.y,maxZ),new Vector3(center.x,center.y,maxZ),new Vector3(maxX,center.y,maxZ),new Vector3(maxX,center.y,center.z),new Vector3(maxX,center.y,minZ),new Vector3(center.x,center.y,minZ),new Vector3(minX,center.y,minZ),new Vector3(minX,center.y,center.z)};
  phase.emissionPoints=new Transform[8];
  for(int i=0;i<8;i++)
  {
   string name="Smoke Point "+(i+1).ToString("00");var point=points.Find(name);if(!point){point=new GameObject(name).transform;point.SetParent(points,false);}
   point.SetPositionAndRotation(positions[i],Quaternion.LookRotation(center-positions[i]));phase.emissionPoints[i]=point;
  }
  phase.flightSeconds=Mathf.Max(phase.swordEffect.lifetime,(new Vector2(maxX-minX,maxZ-minZ).magnitude+5)/Mathf.Max(.1f,phase.slashSpeed));
  // Build contact bounds from the actual crescent mesh, excluding all trailing smoke.
  var visual=UnityEngine.Object.Instantiate(phase.slashPrefab);
  try
  {
   visual.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);visual.transform.localScale=Vector3.one;
   var ps=visual.GetComponent<ParticleSystem>();if(!ps)ps=visual.GetComponentInChildren<ParticleSystem>();
   ps.Simulate(.02f,false,true,false);var particle=new ParticleSystem.Particle[ps.particleCount];ps.GetParticles(particle);
   if(particle.Length==0)throw new Exception("Crescent particle did not emit");
   var mesh=ps.GetComponent<ParticleSystemRenderer>().mesh;
   var matrix=visual.transform.worldToLocalMatrix*ps.transform.localToWorldMatrix*Matrix4x4.TRS(particle[0].position,Quaternion.Euler(particle[0].rotation3D),particle[0].GetCurrentSize3D(ps));
   var hit=new Bounds(matrix.MultiplyPoint3x4(mesh.bounds.center),Vector3.zero);
   foreach(var vertex in mesh.vertices)hit.Encapsulate(matrix.MultiplyPoint3x4(vertex));
   phase.hitLocalCenter=hit.center;phase.hitLocalSize=hit.size+Vector3.one*.02f;
  }
  finally{UnityEngine.Object.DestroyImmediate(visual);}
  var retreat=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/Boss2/PhasePreview/B2_WalkBackward_Preview.anim");
  string loopPath=Folder+"/B2_PhaseRetreat_Loop.anim";var loop=AssetDatabase.LoadAssetAtPath<AnimationClip>(loopPath);
  if(!loop){loop=UnityEngine.Object.Instantiate(retreat);loop.name="B2_PhaseRetreat_Loop";AssetDatabase.CreateAsset(loop,loopPath);}
  var settings=AnimationUtility.GetAnimationClipSettings(loop);settings.loopTime=true;AnimationUtility.SetAnimationClipSettings(loop,settings);
  var controller=(AnimatorController)combat.animator.runtimeAnimatorController;var stateMachine=controller.layers[0].stateMachine;
  var state=stateMachine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="PhaseRetreat");if(!state)state=stateMachine.AddState("PhaseRetreat");state.motion=loop;state.speed=1;state.writeDefaultValues=true;
  combat.smokePhase=phase;
  // This B2 body was missing the arena reference; give its existing constraint the current battle.
  var body=combat.GetComponent<BattleArenaBody>();if(body)body.battle=combat.battle;
  EditorUtility.SetDirty(phase);EditorUtility.SetDirty(combat);EditorUtility.SetDirty(controller);if(body)EditorUtility.SetDirty(body);
  AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Selection.activeGameObject=combat.gameObject;
  return new{points=positions.Select(p=>p.ToString()).ToArray(),center=center.ToString(),damage=phase.Damage,phase.flightSeconds,phase.slashSpeed,phase.launchDelay,phase.smokeSpreadSeconds,phase.shotInterval,hitCenter=phase.hitLocalCenter.ToString(),hitSize=phase.hitLocalSize.ToString(),smokeLayers=cloudPrefab.GetComponent<B2SmokeSlashPreview>().cloudLayers.Length,saved=!scene.isDirty};
 }
 public static object Inspect()
 {
  var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
  var boss=Resources.FindObjectsOfTypeAll<B2CombatController>().FirstOrDefault(b=>b.gameObject.scene==scene);
  if(!boss)return new{scene=scene.path,bossFound=false};
  var arena=boss.GetComponent<BattleArenaBody>();var fx=boss.animator.GetComponent<B2SwordSlash>();
  var slash=fx.slashPrefab;var ps=slash.GetComponentInChildren<ParticleSystem>();var r=ps.GetComponent<ParticleSystemRenderer>();
  return new {scene=scene.path,boss=boss.name,controller=AssetDatabase.GetAssetPath(boss.animator.runtimeAnimatorController),stage=boss.GetComponentInParent<BattleStage>().name,arena=arena?EditorJsonUtility.ToJson(arena):null,
   walls=Resources.FindObjectsOfTypeAll<BattleArenaBoundary>().Where(a=>a.gameObject.scene==scene).Select(a=>new{name=a.name,walls=a.walls.Select(w=>new{name=w.name,center=w.bounds.center.ToString(),size=w.bounds.size.ToString()}).ToArray()}).ToArray(),
   attacks=boss.attacks.Select(a=>new{a.name,a.damage}).ToArray(),boss.phaseClip.length,
   effects=new{fx.BossHeight,fx.heightMultiplier,fx.travelSpeed,fx.lifetime,fx.launchDelay,mesh=r.mesh.name,meshBounds=r.mesh.bounds.ToString(),position=ps.transform.localPosition.ToString(),rotation=ps.transform.localEulerAngles.ToString(),scale=ps.transform.localScale.ToString(),size=ps.main.startSize.constantMax,rotation3D=ps.main.startRotation3D},
   components=boss.GetComponents<Component>().Select(c=>c.GetType().Name).ToArray()};
 }
}
