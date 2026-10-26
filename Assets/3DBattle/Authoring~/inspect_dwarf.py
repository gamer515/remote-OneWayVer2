import bpy, os, json
from mathutils import Vector
bpy.ops.wm.read_factory_settings(use_empty=True)
p=os.path.abspath('Assets/3DBattle/low-poly+dwarf+3d+model/tripo_convert_cd4b7256-ff2a-47c3-acba-5c7a590c78ee 1.fbx')
bpy.ops.import_scene.fbx(filepath=p)
bpy.context.view_layer.update()
for o in bpy.context.scene.objects:
 print('OBJECT',o.name,o.type, list(o.dimensions), list(o.location))
 if o.type=='MESH': print('MESH',len(o.data.vertices),len(o.data.polygons),'BOUNDS', [list(min((o.matrix_world@v.co)[i] for v in o.data.vertices) for i in range(3)),list(max((o.matrix_world@v.co)[i] for v in o.data.vertices) for i in range(3))])
bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath('Assets/3DBattle/Authoring~/DwarfInspect.blend'))
s=bpy.context.scene;s.render.engine='BLENDER_WORKBENCH';s.display.shading.light='STUDIO';s.display.shading.color_type='SINGLE';s.display.shading.show_cavity=True
s.render.resolution_x=800;s.render.resolution_y=800;s.render.resolution_percentage=100
meshes=[o for o in s.objects if o.type=='MESH'];pts=[o.matrix_world@v.co for o in meshes for v in o.data.vertices];lo=Vector([min(p[i] for p in pts) for i in range(3)]);hi=Vector([max(p[i] for p in pts) for i in range(3)]);center=(lo+hi)/2;h=hi.z-lo.z
cd=bpy.data.cameras.new('Review');cam=bpy.data.objects.new('Review',cd);s.collection.objects.link(cam);s.camera=cam;cd.type='ORTHO';cd.ortho_scale=h*1.1
for name,dir in [('front',(0,-1,0)),('side',(1,0,0))]:
 cam.location=center+Vector(dir)*h*3;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();s.render.filepath=os.path.abspath('Assets/3DBattle/Authoring~/Dwarf_'+name+'.png');bpy.ops.render.render(write_still=True)

