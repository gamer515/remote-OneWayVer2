import bpy, os, math, shutil, datetime, json
from mathutils import Vector, Quaternion
HERE=os.path.dirname(os.path.abspath(__file__)); path=os.path.join(HERE,'KnightVoltCombo_UserKeys.blend')
backup=os.path.join(HERE,'Backups','BeforeTwoHandGrip_'+datetime.datetime.now().strftime('%Y%m%d_%H%M%S')); os.makedirs(backup); shutil.copy2(path,backup)
rig=bpy.data.objects['HumanArmature']; scene=bpy.context.scene; action=bpy.data.actions['VoltSlash02']
def sig():
    return {(a.name,fc.data_path,fc.array_index,float(k.co.x)):float(k.co.y) for a in bpy.data.actions for l in a.layers for s in l.strips for bag in s.channelbags for fc in bag.fcurves for k in fc.keyframe_points}
before=sig(); rig.animation_data.action=bpy.data.actions['VoltSlash01']; scene.frame_set(0); idleQ=rig.pose.bones['Palm.R'].matrix.to_quaternion()
rig.animation_data.action=action
def update(): bpy.context.view_layer.update()
def aim(b,target):
    m=b.matrix.copy(); p=m.translation.copy(); q=(m.to_3x3()@Vector((0,1,0))).rotation_difference(Vector(target)-p)
    m=q.to_matrix().to_4x4()@m; m.translation=p; b.matrix=m; update()
def solve(side,target,pole):
    u=rig.pose.bones['UpperArm.'+side]; l=rig.pose.bones['LowerArm.'+side]; palm=rig.pose.bones['Palm.'+side]
    start=u.head.copy(); l1=(l.head-start).length; l2=(palm.head-l.head).length
    v=target-start; d=min(v.length,(l1+l2)*.998); direction=v.normalized(); bend=pole-start; bend=(bend-direction*bend.dot(direction)).normalized()
    along=(l1*l1-l2*l2+d*d)/(2*d); joint=start+direction*along+bend*math.sqrt(max(0,l1*l1-along*along))
    aim(u,joint); aim(l,target)
changed=['UpperArm.R','LowerArm.R','Palm.R','UpperArm.L','LowerArm.L','Palm.L']; checks=[]
for f in range(1,6):
    scene.frame_set(f); update()
    body_before={n:rig.pose.bones[n].matrix.copy() for n in ['Body','Hips','Abdomen','Torso','Foot.L','Foot.R']}
    initial={s:rig.pose.bones['Palm.'+s].head.copy() for s in ['R','L']}
    sword=bpy.data.objects['Sword Preview (authoring only)']; blade=(sword.matrix_world.to_3x3()@Vector((0,0,1))).normalized()
    if f==3: blade=Vector((.65,.75,-.12)).normalized()
    if f==4: blade=Vector((.45,.87,-.20)).normalized()
    if f==5: blade=Vector((-.38,-.76,.526)).normalized()
    rig.animation_data.action=None
    shoulders={s:rig.pose.bones['UpperArm.'+s].head.copy() for s in ['R','L']}
    center=(shoulders['R']+shoulders['L'])*.5
    lengths={s:(rig.pose.bones['LowerArm.'+s].head-shoulders[s]).length+(initial[s]-rig.pose.bones['LowerArm.'+s].head).length for s in ['R','L']}
    right=initial['R'].copy()
    if f<=2:
        forward=rig.pose.bones['Body'].matrix.to_quaternion()@Vector((0,0,1))
        forward.z=0; forward.normalize()
        target=center+forward*.7+Vector((0,0,-.55))
        right=right.lerp(target,.48 if f==1 else .92)
    # Two wrists share one handle axis. R is nearest the blade, L toward pommel.
    spacing=.40
    for iteration in range(12):
        for side,offset in [('R',Vector((0,0,0))),('L',-blade*spacing)]:
            d=right+offset-shoulders[side]; limit=lengths[side]*.95
            if d.length>limit: right=shoulders[side]+d.normalized()*limit-offset
    left=right-blade*spacing
    if f<=2: left=initial['L'].lerp(left,.45 if f==1 else .88)
    for side,target in [('R',right),('L',left)]:
        pole=shoulders[side]+Vector((-1.0 if side=='R' else 1.0,-.3,-.7 if f<5 else -.2))
        solve(side,target,pole)
    qr=Vector((0,0,1)).rotation_difference(blade)@idleQ
    for side,q in [('R',qr),('L',qr@Quaternion((0,1,0),math.pi))]:
        palm=rig.pose.bones['Palm.'+side]; m=q.to_matrix().to_4x4(); m.translation=palm.head; palm.matrix=m; update()
    rig.animation_data.action=action
    for n in changed:
        b=rig.pose.bones[n]
        for p in ['location','rotation_quaternion','scale']: b.keyframe_insert(p,frame=f,group=n)
    r=rig.pose.bones['Palm.R'].head; l=rig.pose.bones['Palm.L'].head
    assert max((rig.pose.bones[n].head-m.translation).length for n,m in body_before.items())<.00001
    checks.append({'frame':f,'handsDistance':(r-l).length,'rightAhead':(r-l).dot(blade),'handleAxisError':((r-l)-blade*spacing).length})
after=sig(); allowed={f'pose.bones["{n}"].{p}' for n in changed for p in ['location','rotation_quaternion','scale']}
assert before.keys()==after.keys()
assert all(v==after[k] for k,v in before.items() if not(k[0]=='VoltSlash02' and 1<=k[3]<=5 and k[1] in allowed))
assert all(c['rightAhead']>.35 and c['handleAxisError']<.02 for c in checks[2:])
scene.frame_set(5); scene.frame_start=0; scene.frame_end=5
print('CHECK',json.dumps({'grip':checks,'bodyAndOtherKeysUnchanged':True,'backup':backup}))
bpy.context.preferences.filepaths.save_version=0; bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE,'KnightVoltCombo_TwoHandKeys.blend'))
source=open(os.path.join(HERE,'refine_slash02_hand_poses.py'),encoding='utf-8').read()
exec(source[source.index("scene.render.engine='BLENDER_WORKBENCH'"):].replace('Slash02_Refined','Slash02_TwoHand'))
