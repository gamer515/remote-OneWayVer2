import bpy,os
from io_scene_fbx import parse_fbx
p=os.path.abspath('Assets/3DBattle/Authoring~/MeshyInspect1.fbx');bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=p);bpy.context.view_layer.update();a=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
print('ARMMATRIX',a.matrix_world)
for b in list(a.pose.bones)[:6]:print('BONE',b.name,'rest',list(b.bone.head_local),'pose',list(b.head),'basisScale',list(b.scale))
for act in list(bpy.data.actions)[:1]:
 print('ACT',act.name,list(act.frame_range),len(act.slots))
 for l in act.layers:
  for s in l.strips:
   for bag in s.channelbags:
    for f in list(bag.fcurves)[:13]:print(f.data_path,f.array_index,len(f.keyframe_points),[list(k.co) for k in list(f.keyframe_points)[:2]])
r,v=parse_fbx.parse(p);obs=next(n for n in r.elems if n.id==b'Objects');cl=next(n for n in obs.elems if n.id==b'Deformer' and n.props[2]==b'Cluster');print('CLUSTER',cl.props,[(c.id,list(c.props[0])) for c in cl.elems if c.id in [b'Transform',b'TransformLink']])
