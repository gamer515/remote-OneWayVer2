import bpy, os, json, math
from mathutils import Vector
root=os.path.join(os.path.dirname(os.path.abspath(__file__)),'DeskDismount')
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.render.fps=30
bpy.ops.import_scene.fbx(filepath=os.path.join(root,'Knight_DeskDismount.fbx'),anim_offset=0)
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH')
action=rig.animation_data.action
report={'roundtrip_import':True,'action':action.name,'frame_range':list(action.frame_range),
        'bones':len(rig.data.bones),'vertices':len(mesh.data.vertices),'frames':[]}
max_location_error=0.0
max_scale_error=0.0
for f in range(1,51):
    bpy.context.scene.frame_set(f)
    deps=bpy.context.evaluated_depsgraph_get()
    obj=mesh.evaluated_get(deps)
    verts=[obj.matrix_world @ v.co for v in obj.data.vertices]
    lo=[min(v[i] for v in verts) for i in range(3)]
    hi=[max(v[i] for v in verts) for i in range(3)]
    assert all(math.isfinite(x) for x in lo+hi)
    assert max(hi[i]-lo[i] for i in range(3))<3,'Unexpected deformation'
    for side in ['L','R']:
        for name in ['UpperLeg.'+side,'LowerLeg.'+side,'Foot.'+side]:
            p=rig.pose.bones[name]
            # Imported local animation must not translate or scale leg joints.
            max_location_error=max(max_location_error,p.location.length)
            max_scale_error=max(max_scale_error,max(abs(v-1) for v in p.scale))
    if f in [1,8,12,15,22,24,27,31,35,40,45,50]:
        report['frames'].append({'frame':f,'min':lo,'max':hi})
print('IMPORTED_RANGE',list(action.frame_range),'FPS',bpy.context.scene.render.fps)
assert all(abs(a-b)<.01 for a,b in zip(action.frame_range,[1,50]))
report['fps']=30
report['max_leg_local_translation']=max_location_error
report['max_leg_scale_error']=max_scale_error
assert max_location_error<.001, 'Leg joint translation stretches the mesh'
assert max_scale_error<.001, 'Leg scale changes'
with open(os.path.join(root,'validation.json'),'w') as out:json.dump(report,out,indent=2)
print('ROUNDTRIP_OK',json.dumps(report))
