import bpy
import json
from pathlib import Path

source = r'C:\Users\유영선\Downloads\three-finger-hand_Paid\three-finger-hand_Paid.blend'
dest = Path(r'C:\remote-OneWayVer2\AssetBuild\ThreeFingerHandPaid')
dest.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=source, use_scripts=False)
report = {
    'objects': [{'name': o.name, 'type': o.type} for o in bpy.context.scene.objects],
    'actions': [{'name': a.name, 'frame_range': list(a.frame_range)} for a in bpy.data.actions],
    'nla_tracks': [{'object': o.name, 'tracks': [{'name': t.name, 'strips': [{'name': s.name, 'action': s.action.name if s.action else None} for s in t.strips]} for t in o.animation_data.nla_tracks]} for o in bpy.context.scene.objects if o.animation_data],
    'missing_images': [im.filepath for im in bpy.data.images if im.source == 'FILE' and not im.packed_file and not Path(bpy.path.abspath(im.filepath)).exists()]
}
bpy.ops.export_scene.fbx(filepath=str(dest / 'three-finger-hand_Paid.fbx'),
    use_selection=False, object_types={'MESH', 'ARMATURE', 'EMPTY'},
    axis_forward='-Z', axis_up='Y', add_leaf_bones=False,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=True,
    path_mode='COPY', embed_textures=True)
(dest / 'conversion_report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print('CONVERSION_REPORT', json.dumps(report, ensure_ascii=False))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(dest / 'three-finger-hand_Paid.fbx'))
print('FBX_VERIFY', json.dumps({'meshes': [o.name for o in bpy.context.scene.objects if o.type == 'MESH'], 'rigs': [{'name': o.name, 'bones': len(o.data.bones)} for o in bpy.context.scene.objects if o.type == 'ARMATURE'], 'actions': [a.name for a in bpy.data.actions]}, ensure_ascii=False))
