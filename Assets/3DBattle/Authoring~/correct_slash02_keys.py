import bpy, os, math, json, shutil, datetime, ast
from mathutils import Vector, Quaternion, Matrix
HERE=os.path.dirname(os.path.abspath(__file__)); path=os.path.join(HERE,'KnightVoltCombo_UserKeys.blend')
backup=os.path.join(HERE,'Backups','BeforeSlash02Keys_'+datetime.datetime.now().strftime('%Y%m%d_%H%M%S')); os.makedirs(backup); shutil.copy2(path,backup)
rig=bpy.data.objects['HumanArmature']; scene=bpy.context.scene; action=bpy.data.actions['VoltSlash02']; rig.animation_data.action=action
def signature():
    return {(a.name,f.data_path,f.array_index,float(k.co.x)):float(k.co.y) for a in bpy.data.actions for l in a.layers for s in l.strips for bag in s.channelbags for f in bag.fcurves for k in f.keyframe_points}
before=signature(); scene.frame_set(0)
base={b.name:b.matrix_basis.copy() for b in rig.pose.bones}; world={b.name:b.matrix.copy() for b in rig.pose.bones}
pivot=world['Foot.L'].translation.copy()
# Reuse pose math only, never run the animation generator.
tree=ast.parse(open(os.path.join(HERE,'author_user_slash01.py'),encoding='utf-8').read())
for node in tree.body:
    if isinstance(node,ast.FunctionDef) and node.name in ['update','aim','solve']:
        exec(compile(ast.Module(body=[node],type_ignores=[]),'<pose-math>','exec'))
# Calibrate blade orientation from the unchanged first clip's ready pose.
rig.animation_data.action=bpy.data.actions['VoltSlash01']; scene.frame_set(0)
idle_palm=rig.pose.bones['Palm.R'].matrix.to_quaternion(); rig.animation_data.action=action
checks=[]
for f,yaw in [(1,-75),(2,-175),(3,-300),(4,-360),(5,-360)]:
    scene.frame_set(f); rig.animation_data.action=None
    for b in rig.pose.bones: b.matrix_basis=base[b.name]
    update()
    turn=Quaternion((0,0,1),math.radians(yaw)).to_matrix().to_4x4()
    transform=Matrix.Translation(pivot)@turn@Matrix.Translation(-pivot)
    root=rig.pose.bones['Bone']; root.matrix=transform@world['Bone']; update()
    if f>=3:
        body=rig.pose.bones['Body']; m=body.matrix.copy()
        m.translation=Vector((-.02,-2.65,1.50 if f<5 else 1.65)); body.matrix=m
        if f==5:
            rig.pose.bones['Hips'].rotation_quaternion=Quaternion((1,0,0),math.radians(18))
            rig.pose.bones['Abdomen'].rotation_quaternion=Quaternion((1,0,0),math.radians(8))
        update()
    right=(transform@world['Foot.R']).translation
    if f>=3: right=Vector((-.65,-3.35,.025))
    else: right.z+=.15
    forward=turn.to_3x3()@Vector((0,-1,0))
    for side,target in [('L',pivot),('R',right)]:
        hip=rig.pose.bones['UpperLeg.'+side].head
        solve('UpperLeg.'+side,'LowerLeg.'+side,target,target+forward*3+Vector((0,0,.8)))
        foot=rig.pose.bones['Foot.'+side]; m=turn@world['Foot.'+side]; m.translation=rig.pose.bones['LowerLeg.'+side].tail; foot.matrix=m; update()
    if f>=3:
        hand={3:(-1.5,-3.0,1.25),4:(-.85,-3.75,1.05),5:(-.65,-3.85,3.65)}[f]
        solve('UpperArm.R','LowerArm.R',hand,(-1.5,-3.0,2.3))
        blade=Vector({3:(-.5,-.6,-.6),4:(0,-.5,-.866),5:(0,-.85,.527)}[f]).normalized()
        palm=rig.pose.bones['Palm.R']; m=(Vector((0,0,1)).rotation_difference(blade)@idle_palm).to_matrix().to_4x4(); m.translation=palm.head; palm.matrix=m; update()
        left=Vector((.85,-2.6,2.1)) if f<5 else Vector((.8,-3.35,3.15))
        solve('UpperArm.L','LowerArm.L',left,(1.4,-2.7,2.6))
    rig.animation_data.action=action
    for b in rig.pose.bones:
        for prop in ['location','rotation_quaternion','scale']: b.keyframe_insert(prop,frame=f,group=b.name)
    checks.append({'frame':f,'leftDrift':(rig.pose.bones['Foot.L'].head-pivot).length,'ankleGaps':{s:(rig.pose.bones['Foot.'+s].head-rig.pose.bones['LowerLeg.'+s].tail).length for s in ['L','R']}})
after=signature()
assert all(v==after[k] for k,v in before.items() if not(k[0]=='VoltSlash02' and 1<=k[3]<=5))
assert all(k in before or (k[0]=='VoltSlash02' and 1<=k[3]<=5) for k in after)
assert max(c['leftDrift'] for c in checks)<.001
print('VERIFIED',json.dumps({'checks':checks,'outsideKeysUnchanged':True,'backup':backup}))
scene.frame_set(5); bpy.context.preferences.filepaths.save_version=0; bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE,'KnightVoltCombo_Slash02Keys.blend'))
scene.render.engine='BLENDER_WORKBENCH'; scene.display.shading.light='STUDIO'; scene.display.shading.color_type='MATERIAL'; scene.display.shading.show_cavity=True
scene.display.shading.background_type='VIEWPORT'; scene.display.shading.background_color=(.1,.1,.1)
scene.render.resolution_x=640; scene.render.resolution_y=640; scene.render.resolution_percentage=100
cd=bpy.data.cameras.new('Review'); cam=bpy.data.objects.new('Review',cd); scene.collection.objects.link(cam); scene.camera=cam; cd.type='ORTHO'; cd.ortho_scale=7
for f in range(1,6):
    scene.frame_set(f); cam.location=(-9,-12,6); cam.rotation_euler=(Vector((0,-2,2.3))-cam.location).to_track_quat('-Z','Y').to_euler(); scene.render.filepath=os.path.join(HERE,f'Slash02_Key{f}.png'); bpy.ops.render.render(write_still=True)
cam.location=(-12,-2,3); cam.rotation_euler=(Vector((0,-2.7,2.5))-cam.location).to_track_quat('-Z','Y').to_euler(); scene.render.filepath=os.path.join(HERE,'Slash02_Key5_Side.png'); bpy.ops.render.render(write_still=True)

