import bpy,glob,json,os
from mathutils import Vector
for path in glob.glob(os.path.abspath('Assets/3DBattle/Authoring~')+'/MeshyInspect*.fbx',recursive=True):
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=path);bpy.context.view_layer.update()
 print('FILE',path)
 def dims(o,evaluated):
  ob=o.evaluated_get(bpy.context.evaluated_depsgraph_get()) if evaluated else o
  vs=[ob.matrix_world@v.co for v in ob.data.vertices]
  return {'size':[max(v[i] for v in vs)-min(v[i] for v in vs) for i in range(3)],'center':[(max(v[i] for v in vs)+min(v[i] for v in vs))/2 for i in range(3)]}
 for o in bpy.context.scene.objects:
  if o.type=='MESH':print('MESH',o.name,len(o.data.vertices),'raw',dims(o,False),'deformed',dims(o,True),'groups',len(o.vertex_groups))
 for a in [o for o in bpy.context.scene.objects if o.type=='ARMATURE']:
  print('ARMATURE',a.name,'scale',list(a.scale),'bones',len(a.data.bones),'action',a.animation_data.action.name if a.animation_data and a.animation_data.action else None)
  a.data.pose_position='REST';bpy.context.view_layer.update()
  for o in bpy.context.scene.objects:
   if o.type=='MESH':print('REST',o.name,dims(o,True))
  a.data.pose_position='POSE'
  if a.animation_data:a.animation_data.action=None
  for b in a.pose.bones:b.matrix_basis.identity()
  bpy.context.view_layer.update()
  for o in bpy.context.scene.objects:
   if o.type=='MESH':print('RESETPOSE',o.name,dims(o,True))


