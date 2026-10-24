import bpy, os, math, json, shutil, datetime
from mathutils import Vector, Quaternion
HERE=os.path.dirname(os.path.abspath(__file__))
path=os.path.join(HERE,'KnightVoltCombo_UserKeys.blend')
backup=os.path.join(HERE,'Backups','BeforeStanceSwap_'+datetime.datetime.now().strftime('%Y%m%d_%H%M%S'))
os.makedirs(backup); shutil.copy2(path,backup)
rig=bpy.data.objects['HumanArmature']; scene=bpy.context.scene
act=bpy.data.actions['VoltSlash01']; rig.animation_data.action=act; scene.frame_set(35)
def snapshot():
    return {(a.name,f.data_path,f.array_index,float(k.co.x)):tuple(k.co) for a in bpy.data.actions for l in a.layers for s in l.strips for bag in s.channelbags for f in bag.fcurves for k in f.keyframe_points}
before=snapshot()
center=rig.pose.bones['Body'].head.x
def mirror(v):
    v=v.copy(); v.x=2*center-v.x; return v
targets={s:mirror(rig.pose.bones['Foot.'+other].head) for s,other in [('L','R'),('R','L')]}
poles={s:mirror(rig.pose.bones['LowerLeg.'+other].head) for s,other in [('L','R'),('R','L')]}
rig.animation_data.action=None
def update(): bpy.context.view_layer.update()
def aim(b,target):
    m=b.matrix.copy(); p=m.translation.copy()
    q=(m.to_3x3()@Vector((0,1,0))).rotation_difference(target-p)
    m=q.to_matrix().to_4x4()@m; m.translation=p; b.matrix=m; update()
changed=[]
for side in ['L','R']:
    u=rig.pose.bones['UpperLeg.'+side]; l=rig.pose.bones['LowerLeg.'+side]; foot=rig.pose.bones['Foot.'+side]
    u.rotation_quaternion=Quaternion((1,0,0,0)); l.rotation_quaternion=Quaternion((1,0,0,0)); update()
    start=u.head.copy(); delta=targets[side]-start
    d=min(delta.length,(u.length+l.length)*.999); direction=delta.normalized()
    bend=poles[side]-start; bend=(bend-direction*bend.dot(direction)).normalized()
    along=(u.length*u.length-l.length*l.length+d*d)/(2*d)
    joint=start+direction*along+bend*math.sqrt(max(0,u.length*u.length-along*along))
    aim(u,joint); aim(l,targets[side])
    m=foot.bone.matrix_local.copy(); m.translation=l.tail; foot.matrix=m; update()
    changed.extend([u.name,l.name,foot.name])
rig.animation_data.action=act
for name in changed:
    b=rig.pose.bones[name]
    for prop in ['location','rotation_quaternion','scale']: b.keyframe_insert(prop,frame=35,group=name)
scene.frame_set(34); scene.frame_set(35)
after=snapshot()
allowed={f'pose.bones["{n}"].{p}' for n in changed for p in ['location','rotation_quaternion','scale']}
assert before.keys()==after.keys()
assert all(v==after[k] for k,v in before.items() if not (k[0]=='VoltSlash01' and k[3]==35 and k[1] in allowed))
assert rig.pose.bones['Foot.L'].head.y < rig.pose.bones['Foot.R'].head.y
print('VERIFIED',json.dumps({'changedBones':changed,'feet':{s:list(rig.pose.bones['Foot.'+s].head) for s in ['L','R']},'ankleGaps':{s:(rig.pose.bones['Foot.'+s].head-rig.pose.bones['LowerLeg.'+s].tail).length for s in ['L','R']},'otherKeysUnchanged':True,'backup':backup}))
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=path)
# Reuse only the preview section; changes above are already saved without cameras.
review=open(os.path.join(HERE,'review_user_frame35.py'),encoding='utf-8').read()
fix=True
exec(review[review.index("scene.render.engine='BLENDER_WORKBENCH'"):])
