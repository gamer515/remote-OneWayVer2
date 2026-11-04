"""Build an isolated, baked desk dismount study using the existing knight rig.
Run with Blender --background --python this_file.py.
"""
import bpy, math, os, sys
from mathutils import Vector, Matrix

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(ROOT, 'DeskDismount')
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT, 'KnightCharacter_RigFixed_ColorFixed.blend'))
rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
for o in list(bpy.context.scene.objects):
    if o not in (rig, mesh): bpy.data.objects.remove(o, do_unlink=True)
rig.animation_data_clear()
mesh.animation_data_clear()
for p in rig.pose.bones:
    for c in list(p.constraints): p.constraints.remove(c)
    p.matrix_basis = Matrix.Identity(4)
    p.rotation_mode = 'QUATERNION'
bpy.context.view_layer.update()
rest = {b.name:b.matrix_local.copy() for b in rig.data.bones}
scene = bpy.context.scene
scene.render.fps = 30
scene.frame_start, scene.frame_end = 1, 50
scene.name='Knight_DeskDismount_Smooth'

def aim(name, head, tail):
    b = rig.data.bones[name]
    q = (b.tail_local-b.head_local).rotation_difference(Vector(tail)-Vector(head))
    rig.pose.bones[name].matrix = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ rest[name].to_quaternion().to_matrix().to_4x4()
    bpy.context.view_layer.update()

def limb(upper, lower, target, pole):
    a = rig.pose.bones[upper].head.copy()
    target = Vector(target)
    l1 = rig.data.bones[upper].length
    l2 = rig.data.bones[lower].length
    delta = target-a
    d = min(delta.length, (l1+l2)*0.985)
    axis = delta.normalized()
    along = (l1*l1-l2*l2+d*d)/(2*d)
    bend = Vector(pole)-a
    bend = (bend-axis*bend.dot(axis)).normalized()
    knee = a+axis*along+bend*math.sqrt(max(0,l1*l1-along*along))
    aim(upper,a,knee)
    # Use the actual lower head, preserving the model's joint offsets.
    reachable = a+axis*d
    aim(lower,rig.pose.bones[lower].head.copy(),reachable)

def lerp(a,b,t): return a+(b-a)*t

# frame, forward travel, pelvis lift, torso lean degrees, left/right ankle forward,
# left/right ankle height, hand release (0 = desk support, 1 = relaxed).
keys = [
 (1,0,1.12,0, .94,.94, 2.0,2.0,0),
 (16,0,1.12,0, .94,.94, 2.0,2.0,0),
 (23,.10,1.10,5, 1.02,.97, 1.80,1.91,0),
 (30,.28,1.03,12, 1.15,1.0, 1.35,1.65,0),
 (43,.78,.62,17, 1.47,1.25, .30,.80,.2),
 (48,.98,.25,19, 1.50,1.46, .10,.42,.45),
 (54,1.16,-.17,19, 1.5,1.67, 0,.12,.8),
 (62,1.26,-.34,16, 1.5,1.72, 0,0,1),
 (70,1.34,-.16,9, 1.5,1.72, 0,0,1),
 (79,1.40,.01,3, 1.5,1.72, 0,0,1),
 (90,1.40,0,0, 1.5,1.72, 0,0,1),
 (100,1.40,0,0, 1.5,1.72, 0,0,1),
]
# Retain all twelve poses on integer frames across the requested 50-frame clip.
keys=[(round(1+(k[0]-1)*49/99),*k[1:]) for k in keys]
# Monotone cubic Hermite paths preserve continuous velocity across poses and
# avoid the old stop/start smoothstep at every reference pose.
def tangent(k,j):
    if k==0 or k==len(keys)-1: return 0.0
    h0=keys[k][0]-keys[k-1][0];h1=keys[k+1][0]-keys[k][0]
    d0=(keys[k][j]-keys[k-1][j])/h0
    d1=(keys[k+1][j]-keys[k][j])/h1
    if d0*d1<=0:return 0.0
    w0=2*h1+h0;w1=h1+2*h0
    return (w0+w1)/(w0/d0+w1/d1)

for f in range(1,51):
    scene.frame_set(f)
    source_frame=f
    i = next((i for i in range(len(keys)-1) if keys[i][0] <= source_frame <= keys[i+1][0]),len(keys)-2)
    a,b = keys[i],keys[i+1]
    h=b[0]-a[0];t=(source_frame-a[0])/h
    forward,lift,lean,lf,rf,lz,rz,release=[
        (2*t**3-3*t*t+1)*a[j]+(t**3-2*t*t+t)*h*tangent(i,j)
        +(-2*t**3+3*t*t)*b[j]+(t**3-t*t)*h*tangent(i+1,j)
        for j in range(1,9)]
    for p in rig.pose.bones: p.matrix_basis=Matrix.Identity(4)
    pivot=rig.data.bones['Body'].head_local
    rotation=Matrix.Rotation(math.radians(lean),4,'X')
    body=Matrix.Translation(Vector((0,-forward,lift))) @ Matrix.Translation(pivot) @ rotation @ Matrix.Translation(-pivot)
    rig.pose.bones['Body'].matrix=body @ rest['Body']
    bpy.context.view_layer.update()
    for side,fw,z in [('L',lf,lz),('R',rf,rz)]:
        foot='Foot.'+side
        ankle=rig.data.bones[foot].head_local.copy()
        ankle.y-=fw; ankle.z+=z
        limb('UpperLeg.'+side,'LowerLeg.'+side,ankle,(ankle.x,-4,1.5))
        # Keep the foot at its actual inherited ankle joint. Forcing an
        # unreachable target position here pulled the boot mesh apart.
        actual_ankle=rig.pose.bones[foot].head.copy()
        rig.pose.bones[foot].matrix=Matrix.Translation(actual_ankle) @ rest[foot].to_quaternion().to_matrix().to_4x4()
        bpy.context.view_layer.update()
    for side,sign in [('L',1),('R',-1)]:
        support=Vector((sign*.95,.42,2.36))
        relaxed=Vector((sign*1.02,-forward-.13,1.65+lift))
        wrist=support.lerp(relaxed,release)
        # Elbows point away from the torso with a small backward bend.
        limb('UpperArm.'+side,'LowerArm.'+side,wrist,(sign*2.5,.9,2.9+lift))
        palm='Palm.'+side
        h=rig.pose.bones[palm].head.copy()
        hand_direction=Vector((0,-.30,-.04)).lerp(Vector((sign*.035,-.02,-.30)),release)
        aim(palm,h,h+hand_direction)
    for p in rig.pose.bones:
        p.scale=(1,1,1)
        if f>1:
            previous=previous_quats[p.name]
            if p.rotation_quaternion.dot(previous)<0:p.rotation_quaternion.negate()
        p.keyframe_insert('location',frame=f,group=p.name)
        p.keyframe_insert('rotation_quaternion',frame=f,group=p.name)
        p.keyframe_insert('scale',frame=f,group=p.name)
    previous_quats={p.name:p.rotation_quaternion.copy() for p in rig.pose.bones}
rig.animation_data.action.name='Knight_DeskDismount'

# Present in metres: the source knight is about 5.6 source units tall.
factor=1.8/5.5988364
rig.scale*=factor
if mesh.parent != rig:
    mesh.scale*=factor
    mesh.location*=factor
for slot in mesh.material_slots:
    m=slot.material
    if m:
        color=tuple(m.diffuse_color)
        m.use_nodes=True
        shader=m.node_tree.nodes.get('Principled BSDF')
        if shader:
            shader.inputs['Base Color'].default_value=color
            shader.inputs['Metallic'].default_value=.45 if 'Armor' in m.name else 0
            shader.inputs['Roughness'].default_value=.45

def material(name,color):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1)
    m.use_nodes=True
    m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(*color,1)
    m.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.7
    return m
wood=material('Preview desk walnut',(.24,.105,.045))
ground=material('Preview floor',(.20,.23,.28))
def cube(name,loc,scale,mat):
    bpy.ops.mesh.primitive_cube_add(size=1,location=Vector(loc)*factor)
    o=bpy.context.object; o.name=name; o.dimensions=Vector(scale)*factor
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    o.data.materials.append(mat)
    bevel=o.modifiers.new('Soft edges','BEVEL'); bevel.width=.012; bevel.segments=3
    o.modifiers.new('Normals','WEIGHTED_NORMAL')
    return o
cube('PREVIEW_DeskTop',(0,1.12,2.22),(3.5,2.6,.20),wood)
for x in [-1.4,1.4]:
    for y in [.25,2.05]: cube('PREVIEW_DeskLeg',(x,y,1.05),(.17,.17,2.1),wood)
cube('PREVIEW_Floor',(0,0,-.08),(200,200,.12),ground)
if scene.world is None: scene.world=bpy.data.worlds.new('Preview World')
scene.world.color=(.25,.25,.25)
def track(obj,point): obj.rotation_euler=(Vector(point)-obj.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(3.5,-4.8,2.7))
camera=bpy.context.object; track(camera,(0,-.05,.95)); camera.data.type='ORTHO'; camera.data.ortho_scale=2.8
scene.camera=camera
for loc,power,size in [((2,-3,5),650,4),((-3,-1,3),450,3),((0,4,4),700,3)]:
    bpy.ops.object.light_add(type='AREA',location=loc)
    light=bpy.context.object;light.data.energy=power;light.data.shape='DISK';light.data.size=size;track(light,(0,0,1))
scene.render.engine='CYCLES';scene.cycles.samples=16
scene.render.resolution_x=720; scene.render.resolution_y=720;scene.render.resolution_percentage=100
for f,label in [(1,'Seated'),(15,'Weight shift'),(22,'Leave desk'),(27,'First contact'),(31,'Absorb landing'),(45,'Standing')]:
    scene.timeline_markers.new(label,frame=f)

scene.frame_set(1)
bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True);mesh.select_set(True);bpy.context.view_layer.objects.active=rig
fbx=os.path.join(OUT,'Knight_DeskDismount.fbx')
bpy.ops.export_scene.fbx(filepath=fbx,use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,
    bake_anim=True,bake_anim_use_all_bones=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=False,
    bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y')
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'Knight_DeskDismount.blend'))
for frame in ([] if '--skip-stills' in sys.argv else [k[0] for k in keys]):
    scene.frame_set(frame)
    scene.render.filepath=os.path.join(OUT,'pose_%03d.png'%frame)
    bpy.ops.render.render(write_still=True)
print('DESK_DISMOUNT_COMPLETE',OUT)
