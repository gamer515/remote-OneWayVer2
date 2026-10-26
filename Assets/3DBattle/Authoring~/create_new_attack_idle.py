import bpy, os, shutil, datetime, json
HERE=os.path.dirname(os.path.abspath(__file__))
path=os.path.join(HERE,'KnightVoltCombo_UserKeys.blend')
name='voltslashnew01'
assert bpy.data.actions.get(name) is None, 'Action already exists; refusing to overwrite it.'
def signature(a):
    return [(f.data_path,f.array_index,[(tuple(k.co),tuple(k.handle_left),tuple(k.handle_right),k.interpolation) for k in f.keyframe_points]) for l in a.layers for s in l.strips for bag in s.channelbags for f in bag.fcurves]
before={a.name:signature(a) for a in bpy.data.actions}
backup=os.path.join(HERE,'Backups','BeforeNewAttack_'+datetime.datetime.now().strftime('%Y%m%d_%H%M%S'))
os.makedirs(backup); shutil.copy2(path,backup)
rig=bpy.data.objects['HumanArmature']; scene=bpy.context.scene
rig.animation_data.action=bpy.data.actions['VoltSlash01']; scene.frame_set(0); bpy.context.view_layer.update()
pose={b.name:b.matrix_basis.copy() for b in rig.pose.bones}
action=bpy.data.actions.new(name); action.use_fake_user=True; rig.animation_data.action=action
scene.frame_set(0)
for bone in rig.pose.bones:
    bone.matrix_basis=pose[bone.name]
    for prop in ['location','rotation_quaternion','scale']: bone.keyframe_insert(prop,frame=0,group=bone.name)
action.pose_markers.new('Ready pose').frame=0
bpy.context.view_layer.objects.active=rig
for obj in bpy.context.selected_objects: obj.select_set(False)
rig.select_set(True)
assert all(signature(bpy.data.actions[n])==s for n,s in before.items())
assert list(action.frame_range)==[0.0,0.0]
scene.frame_set(1); scene.frame_set(0); bpy.context.view_layer.update()
error=max(abs(b.matrix_basis[r][c]-pose[b.name][r][c]) for b in rig.pose.bones for r in range(4) for c in range(4))
assert error<.0001
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE,'KnightVoltCombo_NewAttackReady.blend'))
print('VERIFIED',json.dumps({'action':action.name,'range':list(action.frame_range),'existingActionsUnchanged':True,'poseError':error,'backup':backup}))
