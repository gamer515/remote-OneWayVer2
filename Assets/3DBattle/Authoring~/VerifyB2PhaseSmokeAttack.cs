using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;

public static class VerifyB2PhaseSmokeAttack
{
 public static object SavedSetup()
 {
  if(EditorApplication.isPlaying)throw new Exception("Edit mode required");Time.timeScale=1;
  var phase=UnityEngine.Object.FindObjectsByType<B2PhaseSmokeAttack>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single();
  var inspector=UnityEditor.Editor.CreateEditor(phase);
  try {if(inspector.GetType().Name!="B2PhaseSmokeAttackEditor")throw new Exception("Custom phase inspector not active");}
  finally {UnityEngine.Object.DestroyImmediate(inspector);}
  Selection.activeGameObject=phase.gameObject;UnityEditorInternal.InternalEditorUtility.SetIsInspectorExpanded(phase,true);
  if(phase.combat.smokePhase!=phase||phase.emissionPoints.Length!=8||!phase.smokePrefab||!phase.slashPrefab||!phase.arenaCenter)throw new Exception("Scene configuration incomplete");
  if(phase.smokePrefab.GetComponentsInChildren<Transform>(true).Any(t=>t.CompareTag("EditorOnly")))throw new Exception("Runtime prefab has editor-only child");
  return new{scene=phase.gameObject.scene.path,unsaved=phase.gameObject.scene.isDirty,phase.shotCount,phase.smokeSpreadSeconds,phase.shotInterval,phase.launchDelay,phase.slashSpeed,damage=phase.Damage,cloudLayers=phase.smokePrefab.shape.cloudLayers.Length,center=phase.arenaCenter.position.ToString(),points=phase.emissionPoints.Select(t=>t.position.ToString()).ToArray(),bodyVisible=phase.combat.animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r=>r.enabled)};
 }
 public static object Setup()
 {
  if(!EditorApplication.isPlaying)throw new Exception("Play mode required");
  Time.timeScale=0;
  var battle=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
  var intro=battle.GetComponent<BattleIntroWalkDebug>();if(intro){intro.autoPlayOnStageStart=false;intro.Stop();}
  battle.SelectStage(2);
  var player=battle.player;player.enabled=false;player.CancelInput();
  var loco=player.GetComponent<NewPlayerLocomotion>();if(loco){loco.Stop();loco.enabled=false;}
  var combo=player.GetComponent<NewPlayerAttackCombo>();if(combo){combo.Cancel();combo.enabled=false;}
  var health=player.GetComponent<BattlePlayerHealth>();health.ignoreDamage=true;health.ResetHealth();
  foreach(var root in player.gameObject.scene.GetRootGameObjects())if(root.CompareTag("EditorOnly"))root.SetActive(false);
  var combat=battle.CurrentStage.boss.GetComponent<B2CombatController>();combat.reactAtHealthThresholds=false;
  combat.ResetCombat();battle.CurrentStage.boss.TakeDamage(battle.CurrentStage.boss.maxHealth*.51f);
  return new{combat.State,combat.PendingPhase,health=health.Health,bossHealth=battle.CurrentStage.boss.Health,phase=combat.smokePhase.CurrentPhase,scene=player.gameObject.scene.path};
 }
 public static object DebugButtonChecks()
 {
  Setup();var battle=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();var boss=battle.CurrentStage.boss;boss.ResetBoss();
  var combat=boss.GetComponent<B2CombatController>();var runner=boss.GetComponent<BossPatternRunner>();var weapon=boss.GetComponent<BossWeaponHit>();
  runner.SetTarget(battle.player.GetComponent<BattlePlayerHealth>());
  var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
  if(!(bool)typeof(B2CombatController).GetMethod("BeginAttack",flags).Invoke(combat,null)||!runner.IsRunning)throw new Exception("Test attack did not start");
  float hp=boss.Health;
  if(!combat.CanForcePhaseTwo||!combat.ForcePhaseTwo())throw new Exception("Full-health button unavailable");
  if(boss.Health!=hp||combat.State!=B2CombatState.PhaseTransition||runner.IsRunning||weapon.WindowOpen||weapon.CurrentPattern!=null)throw new Exception("Forced phase failed to stop attack cleanly");
  if(combat.CanForcePhaseTwo||combat.ForcePhaseTwo()||combat.PhaseTransitionCount!=1)throw new Exception("Duplicate transition allowed");
  combat.animator.Play("Base Layer.PhaseRecovery",0,1);combat.animator.Update(0);combat.Tick(.01f);
  if(combat.State!=B2CombatState.SmokePhase||!combat.smokePhase.IsRunning)throw new Exception("Forced recovery did not enter smoke phase");
  combat.smokePhase.Cancel();combat.Tick(.01f);
  if(!combat.CanForcePhaseTwo||!combat.ForcePhaseTwo()||combat.PhaseTransitionCount!=2)throw new Exception("Replay unavailable after previous phase ends");
  combat.ResetCombat();Time.timeScale=1;
  return new{fullHealthTrigger=true,healthUnchanged=hp,attackInterrupted=true,duplicateBlocked=true,smokePhaseStarted=true,replayAvailable=true};
 }
 public static object Step()
 {
  var battle=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();var combat=battle.CurrentStage.boss.GetComponent<B2CombatController>();var phase=combat.smokePhase;
  var transitions=new List<object>();string last=combat.State+"/"+phase.CurrentPhase;
  var launches=new List<object>();var seen=new HashSet<int>(UnityEngine.Object.FindObjectsByType<B2PhaseSlashProjectile>(FindObjectsSortMode.None).Where(s=>s.Launched).Select(s=>s.GetInstanceID()));
  var clock=typeof(B2PhaseSmokeAttack).GetField("phaseTime",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
  var watch=System.Diagnostics.Stopwatch.StartNew();
  for(int i=0;i<180;i++)
  {
   combat.Tick(1f/60);combat.animator.Update(1f/60);phase.Tick(1f/60);Physics.SyncTransforms();
   foreach(var shot in UnityEngine.Object.FindObjectsByType<B2PhaseSlashProjectile>(FindObjectsSortMode.None))
    if(shot.Launched&&seen.Add(shot.GetInstanceID()))launches.Add(new{shot=shot.ShotIndex+1,time=(float)clock.GetValue(phase),point=phase.ShotLocations[shot.ShotIndex]});
   string next=combat.State+"/"+phase.CurrentPhase;
   if(next!=last){transitions.Add(new{at=(i+1)/60f,state=next,shots=phase.ShotsEmitted,active=phase.ActiveProjectileCount,position=combat.transform.position.ToString()});last=next;}
   if(phase.Completed)break;
  }
  return new{state=combat.State.ToString(),phase=phase.CurrentPhase.ToString(),phase.ShotsEmitted,phase.ActiveProjectileCount,phase.Completed,combat.PhaseTransitionCount,combat.PhaseTransitionCompleted,transitions,launches,ms=watch.ElapsedMilliseconds,locations=phase.ShotLocations.ToArray(),visible=combat.animator.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.enabled),collider=battle.CurrentStage.boss.hitbox.enabled};
 }
 static B2PhaseSlashProjectile Spawn(B2PhaseSmokeAttack phase,BattlePlayerHealth health)
 {
  var go=UnityEngine.Object.Instantiate(phase.slashPrefab,health.transform.position+Vector3.back*18+Vector3.up*phase.slashHeight,Quaternion.identity);
  go.name="Temporary projectile validation";go.transform.localScale=Vector3.one*phase.slashScale;
  var shot=go.AddComponent<B2PhaseSlashProjectile>();shot.Initialize(phase,health,99);return shot;
 }
 static void HitOnce(B2PhaseSmokeAttack phase,BattlePlayerHealth health)
 {
  var shot=Spawn(phase,health);
  try {shot.Advance(.19f);if(shot.Launched)throw new Exception("Shot launched before delay");shot.Advance(.01f);shot.Advance(.6f);float after=health.Health;shot.Advance(.1f);if(health.Health!=after)throw new Exception("One projectile hit twice");}
  finally{UnityEngine.Object.DestroyImmediate(shot.gameObject);}
 }
 public static object Contacts()
 {
  var battle=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();var player=battle.player;var health=player.GetComponent<BattlePlayerHealth>();var phase=battle.CurrentStage.boss.GetComponent<B2PhaseSmokeAttack>();
  if(!phase.IsRunning)throw new Exception("Smoke phase required");
  var loco=player.GetComponent<NewPlayerLocomotion>();loco.enabled=true;loco.Stop();loco.SetDefenseMode(false);health.ignoreDamage=false;health.ResetHealth();Physics.SyncTransforms();
  try
  {
   HitOnce(phase,health);if(health.Health!=health.maxHealth-phase.Damage)throw new Exception("Normal projectile damage failed: "+health.Health);
   HitOnce(phase,health);if(health.Health!=health.maxHealth-phase.Damage*2)throw new Exception("Independent projectile damage failed");
   health.ResetHealth();loco.StartParry();int parries=phase.Parries;
   if(!player.IsBlocking)throw new Exception("Guard not active");HitOnce(phase,health);
   if(health.Health!=health.maxHealth||phase.Parries!=parries+1)throw new Exception("Shield parry failed");
   loco.Stop();loco.SetDefenseMode(false);loco.StartRoll();if(!player.IsDashing)throw new Exception("Roll not active");HitOnce(phase,health);
   if(health.Health!=health.maxHealth)throw new Exception("Roll did not avoid projectile");
   loco.Stop();float bossHealth=battle.CurrentStage.boss.Health;battle.CurrentStage.boss.TakeDamage(50);
   if(battle.CurrentStage.boss.Health!=bossHealth)throw new Exception("Hidden boss took damage");
   health.ignoreDamage=true;
   var origin=player.transform.position;var shot=Spawn(phase,health);
   try
   {
    var start=shot.transform.position;shot.Advance(.1f);if(Vector3.Distance(start,shot.transform.position)>.0001f)throw new Exception("Moved while preparing");
    player.transform.position=origin+Vector3.right*5;Physics.SyncTransforms();shot.Advance(.1f);
    if(Vector3.Distance(shot.AimPosition,player.transform.position)>.001f)throw new Exception("Did not aim at launch-time player position");
    var direction=shot.Direction;player.transform.position=origin-Vector3.right*5;shot.Advance(.4f);
    if(Vector3.Distance(direction,shot.Direction)>.0001f)throw new Exception("Projectile homed after launch");
   }
   finally {player.transform.position=origin;UnityEngine.Object.DestroyImmediate(shot.gameObject);Physics.SyncTransforms();}
   return new{damage=phase.Damage,sweptContact=true,oncePerProjectile=true,independentShots=true,parry=true,rollDodge=true,launchDelay=true,launchTimeAim=true,noHoming=true,hiddenBossInvulnerable=true};
  }
  finally {health.ignoreDamage=true;health.ResetHealth();loco.Stop();loco.SetDefenseMode(false);loco.enabled=false;}
 }
 public static object ReturnChecks()
 {
  Setup();
  var battle=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();var boss=battle.CurrentStage.boss;var combat=boss.GetComponent<B2CombatController>();var phase=combat.smokePhase;
  var health=battle.player.GetComponent<BattlePlayerHealth>();
  combat.ResetCombat();health.transform.position=phase.arenaCenter.position;boss.transform.position=phase.arenaCenter.position;Physics.SyncTransforms();
  if(!phase.Begin(health))throw new Exception("Phase start failed");
  var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
  Action<string,object[]> call=(name,args)=>typeof(B2PhaseSmokeAttack).GetMethod(name,flags).Invoke(phase,args);
  call("SetOpacity",new object[]{0f});call("ClearClouds",null);call("SpawnCloud",new object[]{phase.arenaCenter.position,Quaternion.identity});call("Change",new object[]{B2PhaseSmokeAttack.Phase.CenterSmoke});
  int ticks=0;while(phase.CurrentPhase==B2PhaseSmokeAttack.Phase.CenterSmoke&&ticks++<240){phase.Tick(1f/60);combat.animator.Update(1f/60);Physics.SyncTransforms();}
  if(phase.CurrentPhase!=B2PhaseSmokeAttack.Phase.RunOut)throw new Exception("No return state");
  float separation=Vector3.ProjectOnPlane(boss.transform.position-health.transform.position,Vector3.up).magnitude;
  if(separation<3.9f||!boss.hitbox.enabled)throw new Exception("Centre overlap was not resolved before appearance: "+separation);
  for(int i=0;i<24;i++){phase.Tick(1f/60);combat.animator.Update(1f/60);Physics.SyncTransforms();}
  if(phase.Completed)throw new Exception("Smoke ended before fade");
  var clouds=(System.Collections.ICollection)typeof(B2PhaseSmokeAttack).GetField("clouds",flags).GetValue(phase);
  if(clouds.Count!=1)throw new Exception("Central smoke vanished early");
  RenderReturn(boss.transform.position+Vector3.up*3);
  for(int i=0;i<210&&!phase.Completed;i++){phase.Tick(1f/60);combat.animator.Update(1f/60);Physics.SyncTransforms();}
  if(!phase.Completed||clouds.Count!=0||!boss.hitbox.enabled)throw new Exception("Return cleanup failed");
  if(!phase.Begin(health))throw new Exception("Cancellation setup failed");phase.Tick(.1f);phase.Cancel();
  if(phase.IsRunning||!boss.hitbox.enabled||combat.animator.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>!r.enabled))throw new Exception("Mid-phase cancellation failed");
  return new{centreSeparation=separation,smokeRetainedDuringAppearance=true,fadeCompleted=true,cancellationRestoredBody=true,image="Assets/3DBattle/Authoring~/B2PhaseReturnVerification.png"};
 }
 static void RenderReturn(Vector3 focus)
 {
  var go=new GameObject("Temporary phase review camera");var cam=go.AddComponent<Camera>();var rt=new RenderTexture(960,640,24);var image=new Texture2D(960,640,TextureFormat.RGB24,false);var active=RenderTexture.active;
  try{cam.transform.position=focus+new Vector3(-14,6,-18);cam.transform.LookAt(focus);cam.fieldOfView=55;cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,640),0,0);image.Apply();System.IO.File.WriteAllBytes("Assets/3DBattle/Authoring~/B2PhaseReturnVerification.png",image.EncodeToPNG());}
  finally{RenderTexture.active=active;cam.targetTexture=null;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(go);}
 }
 public static object FinishChecks()
 {
  var battle=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();var boss=battle.CurrentStage.boss;var combat=boss.GetComponent<B2CombatController>();var phase=combat.smokePhase;
  if(!phase.Completed||phase.ShotsEmitted!=8||phase.ActiveProjectileCount!=0)throw new Exception("Phase did not finish eight shots");
  combat.Tick(.01f);if(!combat.PhaseTransitionCompleted||combat.State==B2CombatState.SmokePhase)throw new Exception("Normal AI handoff failed");
  boss.TakeDamage(1);if(combat.PendingPhase||combat.PhaseTransitionCount!=1)throw new Exception("Phase queued a second time");
  combat.ResetCombat();
  if(phase.IsRunning||phase.ActiveProjectileCount!=0||!boss.hitbox.enabled||combat.animator.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>!r.enabled))throw new Exception("Reset left hidden/colliding state");
  var materials=combat.animator.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).Where(m=>m).ToArray();
  if(materials.Any(m=>string.IsNullOrEmpty(AssetDatabase.GetAssetPath(m))))throw new Exception("Temporary fade material was not restored");
  Time.timeScale=1;
  return new{normalAiResumed=true,phaseOnceOnly=true,resetRestoredBody=true,originalMaterialsRestored=true};
 }
}
