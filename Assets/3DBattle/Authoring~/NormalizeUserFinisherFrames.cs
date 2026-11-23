using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// One-time preparation of the manually editable copy. Never run the finisher builder here.
public static class NormalizeUserFinisherFrames
{
 const string Folder = "Assets/3DBattle/3DBattleDebug/Finisher";
 const string TargetPath = Folder + "/DebugFinisher_RetreatRush_UserEdit.anim";
 const string OriginalPath = Folder + "/DebugFinisher_RetreatRush.anim";
 const string ReportPath = "Assets/3DBattle/Authoring~/UserFinisherIntegerFramesVerification.json";
 const float Rate = 30f;
 const float Tolerance = 0.00001f;
 static string Id(EditorCurveBinding b) => b.path + "|" + b.propertyName;
 static Dictionary<EditorCurveBinding, AnimationCurve> Curves(AnimationClip clip) => AnimationUtility.GetCurveBindings(clip).ToDictionary(b => b, b => AnimationUtility.GetEditorCurve(clip, b));

 sealed class Plan : IDisposable
 {
  public AnimationClip target, original, candidate;
  public EditorCurveBinding[] bindings;
  public AnimationCurve[] output;
  public Dictionary<EditorCurveBinding, AnimationCurve> before;
  public HashSet<EditorCurveBinding> moved = new HashSet<EditorCurveBinding>();
  public int lastFrame, beforeKeyCount, fractionalKeyCount;
  public float integerValueError, relocatedValueError;
  public void Dispose() { if (candidate) Object.DestroyImmediate(candidate); }
 }
 static Plan Prepare()
 {
  if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play Mode first.");
  var target = BattleCharacterAssetPaths.Load<AnimationClip>(TargetPath);
  var original = BattleCharacterAssetPaths.Load<AnimationClip>(OriginalPath);
  if (!target || !original || Mathf.Abs(target.frameRate - Rate) > Tolerance) throw new Exception("Unexpected clip or sample rate.");
  var p = new Plan { target = target, original = original, before = Curves(target) };
  try
  {
   p.lastFrame = Mathf.RoundToInt(target.length * Rate);
   if (Mathf.Abs(target.length * Rate - p.lastFrame) > 0.0001f) throw new Exception("Non-integer clip end needs separate review.");
   if (AnimationUtility.GetObjectReferenceCurveBindings(target).Length != 0) throw new Exception("Object curves need separate review.");
   var baseline = Curves(original);
   if (!p.before.Keys.ToHashSet().SetEquals(baseline.Keys)) throw new Exception("Binding changes need review.");
   var expectedBones = new HashSet<string> { "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperArm", "LeftLowerArm" };
   foreach (var entry in p.before)
   {
    p.beforeKeyCount += entry.Value.length;
    foreach (var key in entry.Value.keys)
    {
     float frame = key.time * Rate;
     if (Mathf.Abs(frame - Mathf.Round(frame)) > 0.0001f) p.fractionalKeyCount++;
     if (Mathf.Abs(key.value - baseline[entry.Key].Evaluate(key.time)) <= Tolerance) continue;
     // The current inspection found exactly the user's six rotations at 28.5.
     // Abort rather than discard any other manual change made after that inspection.
     if (Mathf.Abs(frame - 28.5f) > 0.0001f || !entry.Key.propertyName.StartsWith("m_LocalRotation.") || !expectedBones.Contains(entry.Key.path.Split('/').Last()))
      throw new Exception("Additional manual edit needs review: " + Id(entry.Key) + " frame " + frame);
     p.moved.Add(entry.Key);
    }
   }
   if (p.moved.Count != 24 || p.moved.Select(b => b.path.Split('/').Last()).Distinct().Count() != 6) throw new Exception("Expected 24 edited channels on six bones.");
   p.bindings = p.before.Keys.ToArray();
   p.output = p.bindings.Select(binding =>
   {
    var source = p.before[binding];
    var keys = new Keyframe[p.lastFrame + 1];
    for (int frame = 0; frame <= p.lastFrame; frame++)
    {
     float time = frame / Rate;
     float value = frame == 29 && p.moved.Contains(binding) ? source.Evaluate(28.5f / Rate) : source.Evaluate(time);
     keys[frame] = new Keyframe(time, value);
    }
    var curve = new AnimationCurve(keys) { preWrapMode = source.preWrapMode, postWrapMode = source.postWrapMode };
    // Match the original authored clip's linear interpolation; avoid overshoot at moved keys.
    for (int i = 0; i < curve.length; i++)
    {
     AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
     AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
    }
    return curve;
   }).ToArray();
   p.candidate = Object.Instantiate(target);
   p.candidate.name = target.name;
   AnimationUtility.SetEditorCurves(p.candidate, p.bindings, p.output);
   Verify(p, p.candidate);
   return p;
  }
  catch { p.Dispose(); throw; }
 }
 static void Verify(Plan p, AnimationClip clip)
 {
  var actual = Curves(clip);
  if (!actual.Keys.ToHashSet().SetEquals(p.before.Keys)) throw new Exception("Changed bindings.");
  if (Mathf.Abs(clip.length - p.target.length) > Tolerance || clip.frameRate != Rate) throw new Exception("Changed timing.");
  float integerError = 0, movedError = 0;
  foreach (var binding in p.bindings)
  {
   var curve = actual[binding];
   if (curve.length != p.lastFrame + 1) throw new Exception("Unexpected key count: " + Id(binding));
   for (int frame = 0; frame <= p.lastFrame; frame++)
   {
    if (Mathf.Abs(curve.keys[frame].time * Rate - frame) > 0.0001f) throw new Exception("Fractional or duplicate key remains.");
    float expected = frame == 29 && p.moved.Contains(binding) ? p.before[binding].Evaluate(28.5f / Rate) : p.before[binding].Evaluate(frame / Rate);
    float error = Mathf.Abs(curve.Evaluate(frame / Rate) - expected);
    if (frame == 29 && p.moved.Contains(binding)) movedError = Mathf.Max(movedError, error);
    else integerError = Mathf.Max(integerError, error);
   }
  }
  if (integerError > Tolerance || movedError > Tolerance) throw new Exception("Pose values changed: " + integerError + ", " + movedError);
  p.integerValueError = integerError; p.relocatedValueError = movedError;
 }
 static object Summary(Plan p, string backup, bool applied) => new {
  applied, target = TargetPath, backup, frameRate = Rate, firstFrame = 0, lastFrame = p.lastFrame,
  duration = p.target.length, curves = p.bindings.Length, keysBefore = p.beforeKeyCount,
  fractionalKeysBefore = p.fractionalKeyCount, keysAfter = p.bindings.Length * (p.lastFrame + 1), fractionalKeysAfter = 0,
  restoredFromFrame = 28.5f, restoredToFrame = 29,
  restoredChannels = p.moved.Count, restoredBones = p.moved.Select(b => b.path.Split('/').Last()).Distinct().OrderBy(s => s).ToArray(),
  preservedIntegerKeyValueMaxError = p.integerValueError, restoredKeyValueMaxError = p.relocatedValueError,
  guid = AssetDatabase.AssetPathToGUID(TargetPath)
 };
 public static string Inspect() { using (var p = Prepare()) return JsonConvert.SerializeObject(Summary(p, null, false), Formatting.Indented); }
 public static string Apply()
 {
  using (var p = Prepare())
  {
   var windows = Resources.FindObjectsOfTypeAll<AnimationWindow>().Where(w => w.animationClip == p.target).ToArray();
   if (windows.Any(w => w.playing || w.recording)) throw new Exception("Stop recording/playback before cleanup.");
   var scene = SceneManager.GetActiveScene(); bool dirtyBefore = scene.isDirty;
   string originalJson = EditorJsonUtility.ToJson(p.original);
   string beforeJson = EditorJsonUtility.ToJson(p.target);
   const string backups = Folder + "/Backups";
   if (!AssetDatabase.IsValidFolder(backups)) AssetDatabase.CreateFolder(Folder, "Backups");
   string backupPath = AssetDatabase.GenerateUniqueAssetPath(backups + "/DebugFinisher_UserEdit_BeforeIntegerCleanup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".anim");
   var backup = Object.Instantiate(p.target);
   backup.name = Path.GetFileNameWithoutExtension(backupPath);
   backup.hideFlags = HideFlags.None;
   AssetDatabase.CreateAsset(backup, backupPath);
   AssetDatabase.SaveAssetIfDirty(backup);
   var backupCurves = Curves(backup);
   foreach (var b in p.bindings)
    if (!p.before[b].keys.SequenceEqual(backupCurves[b].keys)) throw new Exception("Backup verification failed.");
   Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
   Undo.SetCurrentGroupName("Prepare user finisher for integer-frame editing");
   Undo.RegisterCompleteObjectUndo(p.target, "Prepare user finisher for integer-frame editing");
   try
   {
    AnimationUtility.SetEditorCurves(p.target, p.bindings, p.output);
    Verify(p, p.target);
    if (EditorJsonUtility.ToJson(p.original) != originalJson) throw new Exception("Original clip changed unexpectedly.");
    if (scene.isDirty != dirtyBefore) throw new Exception("Scene dirty state changed unexpectedly.");
    EditorUtility.SetDirty(p.target);
    AssetDatabase.SaveAssetIfDirty(p.target);
    Undo.CollapseUndoOperations(group);
   }
   catch
   {
    EditorJsonUtility.FromJsonOverwrite(beforeJson, p.target);
    EditorUtility.SetDirty(p.target);
    AssetDatabase.SaveAssetIfDirty(p.target);
    throw;
   }
   // Keep the same clip selected and display the restored pose. No scene save or controller change.
   foreach (var window in windows)
   {
    window.time = 29f / Rate;
    window.Repaint();
   }
   SceneView.RepaintAll();
   var report = JsonConvert.SerializeObject(Summary(p, backupPath, true), Formatting.Indented);
   File.WriteAllText(ReportPath, report);
   return report;
  }
 }
}