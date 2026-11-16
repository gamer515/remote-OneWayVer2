import bpy, bmesh, json, math
from pathlib import Path
from mathutils import Vector
folder=Path(__file__).parent
data=json.loads((folder/'SlashBefore.json').read_text())
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
verts=[(v[2],v[0],v[1]) for v in data['vertices']]
faces=[data['triangles'][i:i+3] for i in range(0,len(data['triangles']),3)]
mesh=bpy.data.meshes.new('OriginalSlash');mesh.from_pydata(verts,[],faces);mesh.update()
original=bpy.data.objects.new('Before',mesh);bpy.context.collection.objects.link(original)
smooth=bpy.data.objects.new('SmoothedSlash',mesh.copy());bpy.context.collection.objects.link(smooth)
bpy.context.view_layer.objects.active=smooth;smooth.select_set(True)
bm=bmesh.new();bm.from_mesh(smooth.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=0.000001);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(smooth.data);bm.free()
mod=smooth.modifiers.new('Uniform surface topology','REMESH');mod.mode='VOXEL';mod.voxel_size=.003;mod.use_smooth_shade=True
bpy.ops.object.modifier_apply(modifier=mod.name)
mod=smooth.modifiers.new('Surface smoothing','SMOOTH');mod.factor=1;mod.iterations=1000
bpy.ops.object.modifier_apply(modifier=mod.name)
mod=smooth.modifiers.new('Reduce dense triangles','DECIMATE');mod.ratio=.35
bpy.ops.object.modifier_apply(modifier=mod.name)
for p in smooth.data.polygons:p.use_smooth=True
# Preserve the original size and center on all axes.
for axis in range(3):
 lo=min(v.co[axis] for v in mesh.vertices);hi=max(v.co[axis] for v in mesh.vertices)
 a=min(v.co[axis] for v in smooth.data.vertices);b=max(v.co[axis] for v in smooth.data.vertices)
 for v in smooth.data.vertices:v.co[axis]=lo+(v.co[axis]-a)*(hi-lo)/(b-a)
smooth.data.calc_loop_triangles()
result={'vertices':[[v.co.y,v.co.z,v.co.x] for v in smooth.data.vertices], 'triangles':[i for t in smooth.data.loop_triangles for i in t.vertices]}
(folder/'SlashSmoothed.json').write_text(json.dumps(result))
original.location.x=-.36;smooth.location.x=.36
scene=bpy.context.scene;scene.render.engine='BLENDER_WORKBENCH'
scene.display.shading.light='STUDIO';scene.display.shading.color_type='SINGLE';scene.display.shading.single_color=(.7,.7,.7)
scene.display.shading.show_shadows=True;scene.display.shading.show_cavity=True
scene.display.shading.cavity_type='BOTH';scene.display.shading.background_type='WORLD';scene.world.color=(.08,.08,.08)
camera_data=bpy.data.cameras.new('ComparisonCamera');camera=bpy.data.objects.new('ComparisonCamera',camera_data);bpy.context.collection.objects.link(camera)
camera.location=(.2,-3,1);camera.rotation_euler=(Vector((0,0,0))-camera.location).to_track_quat('-Z','Y').to_euler();camera_data.type='ORTHO';camera_data.ortho_scale=1.8;scene.camera=camera
scene.render.resolution_x=1100;scene.render.resolution_y=800;scene.render.resolution_percentage=100
scene.render.filepath=str(folder/'SlashSmoothingComparison.png')
bpy.ops.wm.save_as_mainfile(filepath=str(folder/'B2SlashSmoothed.blend'))
bpy.ops.render.render(write_still=True)
print('RESULT',len(mesh.vertices),len(smooth.data.vertices),len(smooth.data.loop_triangles))


