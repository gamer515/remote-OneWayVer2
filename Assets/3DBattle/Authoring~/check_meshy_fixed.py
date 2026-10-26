import bpy,os
from mathutils import Vector
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=os.path.abspath('Assets/3DBattle/Authoring~/MeshyRigFixed.fbx'));bpy.context.view_layer.update();s=bpy.context.scene;arm=next(o for o in s.objects if o.type=='ARMATURE');o=next(o for o in s.objects if o.type=='MESH')
s.render.engine='BLENDER_WORKBENCH';s.display.shading.light='STUDIO';s.display.shading.color_type='MATERIAL';s.display.shading.show_cavity=True;s.render.resolution_x=700;s.render.resolution_y=700;s.render.resolution_percentage=100
cam=bpy.data.objects.new('Review',bpy.data.cameras.new('Review'));s.collection.objects.link(cam);s.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=2.3;cam.location=(2,-4,1.7);cam.rotation_euler=(Vector((0,0,.85))-cam.location).to_track_quat('-Z','Y').to_euler()
for track in arm.animation_data.nla_tracks:track.mute=True
for idx,action in enumerate(bpy.data.actions):
 arm.animation_data.action=action;arm.animation_data.action_slot=action.slots[0]
 for f in [action.frame_range[0],sum(action.frame_range)/2,action.frame_range[1]]:
  s.frame_set(int(f));bpy.context.view_layer.update();ev=o.evaluated_get(bpy.context.evaluated_depsgraph_get());pts=[ev.matrix_world@v.co for v in ev.data.vertices];size=[max(p[i] for p in pts)-min(p[i] for p in pts) for i in range(3)]
  print('CHECK',action.name,int(f),[round(x,3) for x in size])
  if idx==0 and f==action.frame_range[0]:s.render.filepath=os.path.abspath('Assets/3DBattle/Authoring~/MeshyFixedPreview.png');bpy.ops.render.render(write_still=True)
