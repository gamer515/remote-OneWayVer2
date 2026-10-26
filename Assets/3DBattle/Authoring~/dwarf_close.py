import bpy,os
from mathutils import Vector
s=bpy.context.scene;o=next(o for o in s.objects if o.type=='MESH');deps=bpy.context.evaluated_depsgraph_get();ev=o.evaluated_get(deps);me=ev.to_mesh();pts=[ev.matrix_world@v.co for v in me.vertices]
print('TRANSFORM',o.matrix_world[:],[(m.type,m.name) for m in o.modifiers]);print('EVALBOUNDS',[(min(v[i] for v in pts),max(v[i] for v in pts)) for i in range(3)])
s.render.engine='BLENDER_WORKBENCH';s.display.shading.light='STUDIO';s.display.shading.color_type='SINGLE';s.display.shading.show_cavity=True;s.render.resolution_x=900;s.render.resolution_y=900;s.render.resolution_percentage=100
lo=Vector([min(p[i] for p in pts) for i in range(3)]);hi=Vector([max(p[i] for p in pts) for i in range(3)]);center=(lo+hi)/2;h=hi.z-lo.z
cd=bpy.data.cameras.new('Review');cam=bpy.data.objects.new('Review',cd);s.collection.objects.link(cam);s.camera=cam;cd.type='ORTHO';cd.ortho_scale=h*.48
center.z=lo.z+h*.78
for name,di in [('front',(0,-1,0)),('side',(1,0,0)),('other',(-1,0,0))]:
 cam.location=center+Vector(di)*h*3;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();s.render.filepath=os.path.abspath('Assets/3DBattle/Authoring~/DwarfClose_'+name+'.png');bpy.ops.render.render(write_still=True)
