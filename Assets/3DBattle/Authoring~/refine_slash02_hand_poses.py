import bpy, os, math, json, shutil, datetime, ast
from mathutils import Vector, Quaternion, Matrix
HERE=os.path.dirname(os.path.abspath(__file__)); path=os.path.join(HERE,'KnightVoltCombo_UserKeys.blend')
backup=os.path.join(HERE,'Backups','BeforeSlash02HandPoses_'+datetime.datetime.now().strftime('%Y%m%d_%H%M%S')); os.makedirs(backup); shutil.copy2(path,backup)
rig=bpy.data.objects['HumanArmature']; scene=bpy.context.scene; action=bpy.data.actions['VoltSlash02']
def signature():
    return {(a.name,f.data_path,f.array_index,float(k.co.x)):float(k.co.y) for a in bpy.data.actions for l in a.layers for s in l.strips for bag in s.channelbags for f in bag.fcurves for k in f.keyframe_points}
before=signature()
tree=ast.parse(open(os.path.join(HERE,'author_user_slash01.py'),encoding='utf-8').read())
for node in tree.body:
    if isinstance(node,ast.FunctionDef) and node.name in ['update','aim','solve']:
        exec(compile(ast.Module(body=[node],type_ignores=[]),'<pose-math>','exec'))
rig.animation_data.action=bpy.data.actions['VoltSlash01']; scene.frame_set(0)
neutral={b.name:b.matrix.copy() for b in rig.pose.bones}; idle_palm=neutral['Palm.R'].to_quaternion()
rig.animation_data.action=action
legnames=['UpperLeg.L','LowerLeg.L','Foot.L','UpperLeg.R','LowerLeg.R','Foot.R']
checks=[]
for f,yaw in [(3,90),(4,35),(5,0)]:
    scene.frame_set(f); update()
    feet={n:rig.pose.bones[n].matrix.copy() for n in legnames}
    rig.animation_data.action=None
    body=rig.pose.bones['Body']; pos=body.head.copy()
    turn=Quaternion((0,0,1),math.radians(yaw)).to_matrix().to_4x4()
    m=turn@neutral['Body']; m.translation=pos; body.matrix=m
    rig.pose.bones['Hips'].rotation_quaternion=Quaternion((1,0,0),math.radians(30 if f<5 else 18))
    rig.pose.bones['Abdomen'].rotation_quaternion=Quaternion((1,0,0),math.radians(16 if f<5 else 8))
    rig.pose.bones['Torso'].rotation_quaternion=Quaternion((1,0,0,0))
    rig.pose.bones['Head'].rotation_quaternion=Quaternion((1,0,0),math.radians(-10))
    update()
    # Preserve the planted feet and established stance while turning the torso.
    for n in legnames: rig.pose.bones[n].matrix=feet[n]; update()
    for side in ['R','L']:
        for name in ['Shoulder.'+side,'UpperArm.'+side,'LowerArm.'+side,'Palm.'+side]:
            rig.pose.bones[name].rotation_quaternion=Quaternion((1,0,0,0))
    update()
    # Screen-right low in frames 3/4, screen-left high in frame 5, as marked.
    targets={3:{'R':(1.30,-3.12,1.35),'L':(1.45,-3.28,1.55)},
             4:{'R':(1.05,-3.40,1.28),'L':(1.25,-3.48,1.50)},
             5:{'R':(-1.25,-3.45,4.65),'L':(-.80,-3.55,4.50)}}[f]
    for side in ['R','L']:
        pole=Vector(((-1.4 if side=='R' else 1.4),-3.4,2.4 if f<5 else 3.4))
        solve('UpperArm.'+side,'LowerArm.'+side,targets[side],pole)
    blade=Vector((.12,-.3,-.95) if f<5 else (0,-.85,.527)).normalized()
    palm=rig.pose.bones['Palm.R']; m=(Vector((0,0,1)).rotation_difference(blade)@idle_palm).to_matrix().to_4x4(); m.translation=palm.head; palm.matrix=m; update()
    rig.animation_data.action=action
    for b in rig.pose.bones:
        for prop in ['location','rotation_quaternion','scale']: b.keyframe_insert(prop,frame=f,group=b.name)
    checks.append({'frame':f,'torsoYaw':yaw,'hands':{s:list(rig.pose.bones['Palm.'+s].head) for s in ['L','R']},'feetDrift':max((rig.pose.bones[n].head-feet[n].translation).length for n in legnames)})
after=signature()
assert before.keys()==after.keys()
assert all(v==after[k] for k,v in before.items() if not(k[0]=='VoltSlash02' and 3<=k[3]<=5))
assert max(c['feetDrift'] for c in checks)<.0001
scene.frame_set(5); scene.frame_start=0; scene.frame_end=5
action['authoring_notes']='Frames 0-5 are six reference poses; frame 5 is the intended final pose. Frames 6 onward are unrelated legacy keys. Retime and fill only on user request. Hand placement follows annotated front-view images.'
print('VERIFIED',json.dumps({'checks':checks,'allOtherKeysUnchanged':True,'backup':backup}))
bpy.context.preferences.filepaths.save_version=0; bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE,'KnightVoltCombo_Slash02Hands.blend'))
scene.render.engine='BLENDER_WORKBENCH'; scene.display.shading.light='STUDIO'; scene.display.shading.color_type='MATERIAL'; scene.display.shading.show_cavity=True
scene.display.shading.background_type='VIEWPORT'; scene.display.shading.background_color=(.1,.1,.1)
scene.render.resolution_x=640; scene.render.resolution_y=640; scene.render.resolution_percentage=100
cd=bpy.data.cameras.new('Review'); cam=bpy.data.objects.new('Review',cd); scene.collection.objects.link(cam); scene.camera=cam; cd.type='ORTHO'; cd.ortho_scale=7
for f in [3,4,5]:
    scene.frame_set(f)
    for label,position in [('Front',(0,-14,3.1)),('Side',(-12,-2,3.1))]:
        cam.location=position; cam.rotation_euler=(Vector((0,-2.7,2.7))-cam.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=os.path.join(HERE,f'Slash02_Refined{f}_{label}.png'); bpy.ops.render.render(write_still=True)

