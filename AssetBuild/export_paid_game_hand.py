import bpy
import json
from pathlib import Path

dest = Path(r'C:\remote-OneWayVer2\AssetBuild\ThreeFingerHandPaid')
bpy.ops.wm.open_mainfile(filepath=r'C:\Users\유영선\Downloads\three-finger-hand_Paid\three-finger-hand_Paid.blend', use_scripts=False)
rig = bpy.data.objects['hand_game-rig']
hand = bpy.data.objects['Hand']
bpy.ops.object.select_all(action='DESELECT')
rig.hide_set(False)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.nla.bake(frame_start=1, frame_end=121, step=1, only_selected=False,
    visual_keying=True, clear_constraints=True, use_current_action=False, bake_types={'POSE'})
rig.animation_data.action.name = 'HandAnimation'
hand.select_set(True)
hand.hide_set(False)
bpy.ops.export_scene.fbx(filepath=str(dest / 'three-finger-hand_Paid_Unity.fbx'),
    use_selection=True, object_types={'MESH', 'ARMATURE'}, axis_forward='-Z', axis_up='Y',
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False,
    bake_anim_use_nla_strips=False, path_mode='COPY', embed_textures=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(dest / 'three-finger-hand_Paid_Unity.fbx'))
rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
action = rig.animation_data.action
assert action is not None
bpy.context.scene.frame_set(1)
start = [tuple(v for row in b.matrix for v in row) for b in rig.pose.bones]
changed = False
for frame in (15, 30, 60, 90, 121):
    bpy.context.scene.frame_set(frame)
    end = [tuple(v for row in b.matrix for v in row) for b in rig.pose.bones]
    changed |= any(abs(x-y)>1e-5 for a,b in zip(start,end) for x,y in zip(a,b))
assert changed, 'Exported animation has no bone movement'
print('VERIFIED_GAME_FBX', json.dumps({'bones':len(rig.data.bones), 'action':action.name, 'frame_range':list(action.frame_range), 'bone_motion':changed}))
