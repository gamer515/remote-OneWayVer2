"""Export edited VoltSlash actions from KnightVoltCombo.blend for the Unity menu.
blender --background KnightVoltCombo.blend --python export_combo.py
"""
import bpy, json, os, sys

folder = os.path.dirname(os.path.abspath(__file__))
path = os.path.join(folder, 'combo_samples.json')
with open(path, encoding='utf-8') as stream:
    data = json.load(stream)
rig = bpy.data.objects['HumanArmature']
scene = bpy.context.scene
original_action = rig.animation_data.action
original_frame = scene.frame_current
clips = data['clips']
args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
only = args[args.index('--only')+1] if '--only' in args else None
try:
    for name in (['voltslashnew01'] if only == 'voltslashnew01' else ['VoltSlash%02d' % i for i in range(1,5)]):
        if only and name != only:
            continue
        action = bpy.data.actions[name]
        rig.animation_data.action = action
        start, end = [round(f) for f in action.frame_range]
        fps = scene.render.fps / scene.render.fps_base
        samples = []
        for frame in range(start, end + 1):
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            normalized = (frame-start)/max(1,action.get('travel_reference_frames',end-start))
            u = min(1,max(0,(normalized-action.get('travel_start',.15))/max(.001,action.get('travel_end',.62)-action.get('travel_start',.15))))
            travel = action.get('forward_travel',0) * u*u*(3-2*u)
            # UserKeys clip 02 begins at clip 01's advanced world-space pose.
            # The player controller has already moved there; do not add it twice.
            if name == 'VoltSlash02' and 'Source pose 5 - FINAL' in action.pose_markers:
                travel += bpy.data.actions['VoltSlash01'].get('forward_travel', 0)
            samples.append({'time': (frame-start)/fps, 'forwardTravel':travel, 'bones': [
                {'name': b.name, 'position': list(b.matrix.translation),
                 'rotation': list(b.matrix.to_quaternion())} for b in rig.pose.bones]})
        result = {'name': name, 'duration': (end-start)/fps, 'samples': samples}
        existing = next((i for i,c in enumerate(clips) if c['name']==name), None)
        if existing is None: clips.append(result)
        else: clips[existing] = result
    data['clips'] = clips
    with open(path, 'w', encoding='utf-8') as stream:
        json.dump(data, stream, separators=(',', ':'))
finally:
    rig.animation_data.action = original_action
    scene.frame_set(original_frame)
print('Exported ' + (only or 'four edited actions') + ' to ' + path)
