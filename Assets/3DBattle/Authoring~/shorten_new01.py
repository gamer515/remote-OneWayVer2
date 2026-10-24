import bpy, os, shutil, datetime, json
here = os.path.dirname(os.path.abspath(__file__))
action = bpy.data.actions['voltslashnew01']
rig = bpy.data.objects['HumanArmature']
scene = bpy.context.scene
def curves(a):
    return [f for l in a.layers for s in l.strips for b in s.channelbags for f in b.fcurves]
def signature(a):
    return [(f.data_path, f.array_index, [(tuple(k.co), tuple(k.handle_left), tuple(k.handle_right), k.interpolation) for k in f.keyframe_points]) for f in curves(a)]
assert tuple(action.frame_range) == (0.0, 34.0), tuple(action.frame_range)
backup = os.path.join(here, 'Backups', 'BeforeShortenNew01_' + datetime.datetime.now().strftime('%Y%m%d_%H%M%S'))
os.makedirs(backup)
for relative in ['Authoring~/KnightVoltCombo_UserKeys.blend', 'Authoring~/combo_samples.json', 'Scripts/BattlePlayerController.cs', 'Animations/voltslashnew01.anim', 'Animations/KnightBattle.controller']:
    shutil.copy2(os.path.join(os.path.dirname(here), relative), backup)
others = {a.name: signature(a) for a in bpy.data.actions if a != action}
rig.animation_data.action = action
old = [0, 6, 24, 28, 34]
new = [0, 6, 16, 19, 22]
poses = []
for frame in old:
    scene.frame_set(frame)
    poses.append({b.name: b.matrix.copy() for b in rig.pose.bones})
def remap(t):
    i = next((i for i in range(4) if t <= old[i+1]), 3)
    return new[i] + (t-old[i]) * (new[i+1]-new[i])/(old[i+1]-old[i])
for f in curves(action):
    for k in f.keyframe_points:
        x, left, right = k.co.x, k.handle_left.x, k.handle_right.x
        k.co.x = remap(x)
        k.handle_left.x = remap(left)
        k.handle_right.x = remap(right)
    f.update()
for marker in action.pose_markers:
    marker.frame = round(remap(marker.frame))
error = 0
for frame, pose in zip(new, poses):
    scene.frame_set(frame)
    error = max(error, max(abs(b.matrix[r][c]-pose[b.name][r][c]) for b in rig.pose.bones for r in range(4) for c in range(4)))
assert error < 0.0001, error
assert all(signature(bpy.data.actions[n]) == s for n, s in others.items())
scene.frame_start = 0
scene.frame_end = 22
scene.frame_set(22)
action['authoring_notes'] = 'Shortened: source poses at 0,6,16,19,22. Repeat begins at 6. Preparation 10 frames; downward strike 6 frames. Original poses retained.'
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(here, 'KnightVoltCombo_New01Shortened.blend'))
print('VERIFIED', json.dumps({'frames': new, 'poseError': error, 'otherActionsUnchanged': True, 'backup': backup}))
