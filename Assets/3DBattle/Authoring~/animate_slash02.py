import bpy, os, math, shutil, datetime, json, ast
from mathutils import Vector, Quaternion, Matrix
HERE=os.path.dirname(os.path.abspath(__file__)); path=os.path.join(HERE,'KnightVoltCombo_UserKeys.blend')
backup=os.path.join(HERE,'Backups','BeforeSlash02Animation_'+datetime.datetime.now().strftime('%Y%m%d_%H%M%S')); os.makedirs(backup); shutil.copy2(path,backup)
rig=bpy.data.objects['HumanArmature']; scene=bpy.context.scene; action=bpy.data.actions['VoltSlash02']; rig.animation_data.action=action
def signature(a):
    return [(f.data_path,f.array_index,[(tuple(k.co),tuple(k.handle_left),tuple(k.handle_right),k.interpolation) for k in f.keyframe_points]) for l in a.layers for s in l.strips for bag in s.channelbags for f in bag.fcurves]
other={a.name:signature(a) for a in bpy.data.actions if a!=action}
poses=[]; worlds=[]
for f in range(6):
    scene.frame_set(f)
    poses.append({b.name:b.matrix_basis.copy() for b in rig.pose.bones})
    worlds.append({b.name:b.matrix.copy() for b in rig.pose.bones})
for source in ['author_user_slash01.py','two_hand_slash02_keys.py']:
    tree=ast.parse(open(os.path.join(HERE,source),encoding='utf-8').read())
    for node in tree.body:
        if isinstance(node,ast.FunctionDef) and node.name in ['update','aim','solve']:
            exec(compile(ast.Module(body=[node],type_ignores=[]),'<pose-math>','exec'))
    if source=='author_user_slash01.py': solve_leg=solve
solve_arm=solve
for l in action.layers:
    for s in l.strips:
        for bag in s.channelbags:
            for fc in list(bag.fcurves): bag.fcurves.remove(fc)
for m in list(action.pose_markers): action.pose_markers.remove(m)
times=[0,5,11,17,21,30]; END=30; last={}; checks=[]
def mix(a,b,t):
    p,q,s=a.decompose(); p2,q2,s2=b.decompose()
    if q.dot(q2)<0:q2.negate()
    return Matrix.LocRotScale(p.lerp(p2,t),q.slerp(q2,t),s.lerp(s2,t))
for frame in range(END+1):
    segment=next((i for i in range(5) if frame<=times[i+1]),4)
    u=(frame-times[segment])/(times[segment+1]-times[segment])
    # Keep rotation flowing through the turn; ease the start and finish only.
    t=u
    if segment==0: t=u*u*(2-u)
    if segment==4: t=1-(1-u)**2
    scene.frame_set(frame); rig.animation_data.action=None
    for b in rig.pose.bones: b.matrix_basis=mix(poses[segment][b.name],poses[segment+1][b.name],t)
    update()
    exact=frame in times
    if not exact:
        for side in ['L','R']:
            n='Foot.'+side; m=mix(worlds[segment][n],worlds[segment+1][n],t)
            target=m.translation.copy()
            # Feet rotate around the planted left pivot; the right foot swings then lands.
            if side=='R' and frame<17: target.z+=.10*math.sin(math.pi*u)
            lower=rig.pose.bones['LowerLeg.'+side]; pole=lower.head.copy()
            solve_leg('UpperLeg.'+side,'LowerLeg.'+side,target,pole)
            m.translation=rig.pose.bones['LowerLeg.'+side].tail
            rig.pose.bones[n].matrix=m; update()
        if frame>=17:
            sword=bpy.data.objects['Sword Preview (authoring only)']
            blade=(sword.matrix_world.to_3x3()@Vector((0,0,1))).normalized()
            right=rig.pose.bones['Palm.R'].head.copy()
            rotations={s:rig.pose.bones['Palm.'+s].matrix.to_quaternion() for s in ['R','L']}
            for iteration in range(16):
                for side,offset in [('R',Vector((0,0,0))),('L',-blade*.4)]:
                    u=rig.pose.bones['UpperArm.'+side]; l=rig.pose.bones['LowerArm.'+side]; p=rig.pose.bones['Palm.'+side]
                    limit=((l.head-u.head).length+(p.head-l.head).length)*.97
                    delta=right+offset-u.head
                    if delta.length>limit:right=u.head+delta.normalized()*limit-offset
            for side,target in [('R',right),('L',right-blade*.4)]:
                palm=rig.pose.bones['Palm.'+side]; pole=rig.pose.bones['LowerArm.'+side].head.copy()
                solve_arm(side,target,pole)
                m=rotations[side].to_matrix().to_4x4(); m.translation=palm.head; palm.matrix=m; update()
    rig.animation_data.action=action
    for b in rig.pose.bones:
        q=b.rotation_quaternion.copy()
        if b.name in last and q.dot(last[b.name])<0:q.negate(); b.rotation_quaternion=q
        last[b.name]=q.copy()
        for prop in ['location','rotation_quaternion','scale']: b.keyframe_insert(prop,frame=frame,group=b.name)
    checks.append({'frame':frame,'leftFoot':list(rig.pose.bones['Foot.L'].head),'rightFoot':list(rig.pose.bones['Foot.R'].head),'gripDistance':(rig.pose.bones['Palm.R'].head-rig.pose.bones['Palm.L'].head).length})
for l in action.layers:
    for s in l.strips:
        for bag in s.channelbags:
            for fc in bag.fcurves:
                for k in fc.keyframe_points:k.interpolation='LINEAR'
for index,f in enumerate(times):action.pose_markers.new('Source pose '+str(index)+(' - FINAL' if index==5 else '')).frame=f
action['authoring_notes']='Original poses 0-5 mapped to 0,5,11,17,21,30. Source pose 5 is final. 60 fps, 0.5 seconds. No idle return.'
scene.render.fps=60; scene.render.fps_base=1; scene.frame_start=0; scene.frame_end=END
scene.frame_set(END); update()
finalDelta=max(abs(rig.pose.bones[n].matrix[r][c]-worlds[5][n][r][c]) for n in worlds[5] for r in range(4) for c in range(4))
assert finalDelta<.0001
assert all(signature(bpy.data.actions[n])==v for n,v in other.items())
assert max((Vector(c['leftFoot'])-worlds[0]['Foot.L'].translation).length for c in checks)<.001
assert max(abs(c['gripDistance']-.4) for c in checks[17:])<.001
print('VERIFIED',json.dumps({'mapping':times,'duration':END/60,'finalPoseMaxError':finalDelta,'otherActionsUnchanged':True,'backup':backup,'gripRangeAfter17':[min(c['gripDistance'] for c in checks[17:]),max(c['gripDistance'] for c in checks[17:])]}))
bpy.context.preferences.filepaths.save_version=0; bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE,'KnightVoltCombo_Slash02Animated.blend'))
with open(os.path.join(HERE,'Slash02_AnimationChecks.json'),'w') as f:json.dump(checks,f,indent=2)
scene.render.engine='BLENDER_WORKBENCH'; scene.display.shading.light='STUDIO'; scene.display.shading.color_type='MATERIAL'; scene.display.shading.show_cavity=True
scene.display.shading.background_type='VIEWPORT'; scene.display.shading.background_color=(.1,.1,.1)
scene.render.resolution_x=640; scene.render.resolution_y=640; scene.render.resolution_percentage=100
cd=bpy.data.cameras.new('Review'); cam=bpy.data.objects.new('Review',cd); scene.collection.objects.link(cam); scene.camera=cam; cd.type='ORTHO'; cd.ortho_scale=10
cam.location=(-9,-13,7); cam.rotation_euler=(Vector((.4,-1.9,2.5))-cam.location).to_track_quat('-Z','Y').to_euler()
out=os.path.join(HERE,'Slash02AnimatedFrames~'); os.makedirs(out,exist_ok=True)
for frame in range(END+1):
    scene.frame_set(frame); scene.render.filepath=os.path.join(out,f'{frame:03}.png'); bpy.ops.render.render(write_still=True)
