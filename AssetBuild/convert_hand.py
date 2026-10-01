import bpy
import json
from pathlib import Path

source = r'C:\Users\유영선\Downloads\three-finger-hand_Free\three-finger-hand_Free.blend'
dest = Path(r'C:\remote-OneWayVer2\AssetBuild\ThreeFingerHand')
dest.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=source, use_scripts=False)
report = {'objects': [{'name': o.name, 'type': o.type} for o in bpy.context.scene.objects],
          'actions': [a.name for a in bpy.data.actions],
          'missing_images': [im.filepath for im in bpy.data.images if im.source == 'FILE' and not im.packed_file and not Path(bpy.path.abspath(im.filepath)).exists()]}
bpy.ops.export_scene.fbx(filepath=str(dest / 'three-finger-hand_Free.fbx'),
    use_selection=False, object_types={'MESH', 'ARMATURE', 'EMPTY'},
    axis_forward='-Z', axis_up='Y', add_leaf_bones=False,
    bake_anim=True, path_mode='COPY', embed_textures=True)
(dest / 'conversion_report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print('CONVERSION_REPORT', json.dumps(report, ensure_ascii=False))
