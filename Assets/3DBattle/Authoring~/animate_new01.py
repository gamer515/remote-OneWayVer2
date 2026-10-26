import bpy, os, math, shutil, datetime, json, ast
from mathutils import Matrix, Vector
HERE=os.path.dirname(os.path.abspath(__file__)); path=os.path.join(HERE,'KnightVoltCombo_UserKeys.blend')
backup=os.path.join(HERE,'Backups','BeforeAnimateNew01_'+datetime.datetime.now().strftime('%Y%m%d_%H%M%S')); os.makedirs(backup); shutil.copy2(path,backup)
rig=bpy.data.objects['HumanArmature']; scene=bpy.context.scene; action=bpy.data.actions['voltslashnew01']; rig.animation_data.action=action
def signature(a):
    return [(f.data_path,f.array_index,[(tuple(k.co),tuple(k.handle_left),tuple(k.handle_right),k.interpolation) for k in f.keyframe_points]) for l in a.layers for s in l.strips for bag in s.channelbags for f in bag.fcurves]
others={a.name:signature(a) for a in bpy.data.actions if a!=action}
poses=[]; worlds=[]
for f in range(5):
    scene.frame_set(f); poses.append({b.name:b.matrix_basis.copy() for b in rig.pose.bones}); worlds.append({b.name:b.matrix.copy() for b in rig.pose.bones})
tree=ast.parse(open(os.path.join(HERE,'two_hand_slash02_keys.py'),encoding='utf-8').read())
for node in tree.body:
    if isinstance(node,ast.FunctionDef) and node.name in ['update','aim','solve']:exec(compile(ast.Module(body=[node],type_ignores=[]),'<math>','exec'))
def aim_wrist(lower,target,side):
    m=lower.matrix.copy(); p=m.translation.copy(); q=(rig.pose.bones['Palm.'+side].head-p).rotation_difference(Vector(target)-p)
    m=q.to_matrix().to_4x4()@m; m.translation=p; lower.matrix=m; update()
node=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='solve')
exec(ast.unparse(node).replace('aim(l, target)','aim_wrist(l, target, side)'))
for l in action.layers:
    for s in l.strips:
        for bag in s.channelbags:
            for fc in list(bag.fcurves):bag.fcurves.remove(fc)
for marker in list(action.pose_markers):action.pose_markers.remove(marker)
def mix(a,b,t):
    p,q,s=a.decompose(); p2,q2,s2=b.decompose()
    if q.dot(q2)<0:q2.negate()
    return Matrix.LocRotScale(p.lerp(p2,t),q.slerp(q2,t),s.lerp(s2,t))
times=[0,6,24,28,34]; last={}; maxLegDrift=0; checks=[]
for frame in range(35):
    i=next(i for i in range(4) if frame<=times[i+1]); u=(frame-times[i])/(times[i+1]-times[i])
    t=u*u*(3-2*u) if i==0 else (u*u if i==1 else (1-(1-u)**2 if i==3 else u))
    scene.frame_set(frame); rig.animation_data.action=None
    for b in rig.pose.bones:b.matrix_basis=mix(poses[i][b.name],poses[i+1][b.name],t)
    update()
    if frame>=6 and frame not in times:
        # Hold the user's established leg placement throughout the strike.
        for n in ['UpperLeg.L','LowerLeg.L','Foot.L','UpperLeg.R','LowerLeg.R','Foot.R']:
            rig.pose.bones[n].matrix=worlds[1][n]; update()
        blade=(bpy.data.objects['Sword Preview (authoring only)'].matrix_world.to_3x3()@Vector((0,0,1))).normalized()
        right=rig.pose.bones['Palm.R'].head.copy(); rotations={s:rig.pose.bones['Palm.'+s].matrix.to_quaternion() for s in ['R','L']}
        for iteration in range(12):
            for side,offset in [('R',Vector((0,0,0))),('L',-blade*.4)]:
                a=rig.pose.bones['UpperArm.'+side]; b=rig.pose.bones['LowerArm.'+side]; p=rig.pose.bones['Palm.'+side]
                limit=((b.head-a.head).length+(p.head-b.head).length)*.97; delta=right+offset-a.head
                if delta.length>limit:right=a.head+delta.normalized()*limit-offset
        for side,target in [('R',right),('L',right-blade*.4)]:
            solve(side,target,rig.pose.bones['LowerArm.'+side].head.copy())
            palm=rig.pose.bones['Palm.'+side]; m=rotations[side].to_matrix().to_4x4(); m.translation=palm.head; palm.matrix=m; update()
    rig.animation_data.action=action
    for b in rig.pose.bones:
        q=b.rotation_quaternion.copy()
        if b.name in last and q.dot(last[b.name])<0:q.negate(); b.rotation_quaternion=q
        last[b.name]=q.copy()
        for prop in ['location','rotation_quaternion','scale']:b.keyframe_insert(prop,frame=frame,group=b.name)
    if frame>=6:
        maxLegDrift=max(maxLegDrift,max((rig.pose.bones[n].head-worlds[1][n].translation).length for n in ['UpperLeg.L','LowerLeg.L','Foot.L','UpperLeg.R','LowerLeg.R','Foot.R']))
    checks.append({'frame':frame,'grip':(rig.pose.bones['Palm.R'].head-rig.pose.bones['Palm.L'].head).length})
for l in action.layers:
    for s in l.strips:
        for bag in s.channelbags:
            for fc in bag.fcurves:
                for k in fc.keyframe_points:k.interpolation='LINEAR'
for index,f in enumerate(times):action.pose_markers.new('Source '+str(index)+(' FINAL' if index==4 else '')).frame=f
assert maxLegDrift<.0001
assert all(signature(bpy.data.actions[n])==v for n,v in others.items())
scene.frame_set(34); update(); error=max(abs(rig.pose.bones[n].matrix[r][c]-worlds[4][n][r][c]) for n in worlds[4] for r in range(4) for c in range(4)); assert error<.0001
scene.render.fps=60; scene.render.fps_base=1; scene.frame_start=0; scene.frame_end=34
action['authoring_notes']='Source poses 0,1,2,3,4 mapped to frames 0,6,24,28,34. 1-to-2 takes 18 frames; 2-to-4 takes 10. Final source pose 4; no recovery. Leg placement preserved from pose 1 onward.'
bpy.context.preferences.filepaths.save_version=0; bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE,'KnightVoltCombo_New01Animated.blend'))
print('VERIFIED',json.dumps({'mapping':times,'legDrift':maxLegDrift,'finalPoseError':error,'otherActionsUnchanged':True,'backup':backup}))
scene.render.engine='BLENDER_WORKBENCH'; scene.display.shading.light='STUDIO'; scene.display.shading.color_type='MATERIAL'; scene.display.shading.show_cavity=True
scene.display.shading.background_type='VIEWPORT'; scene.display.shading.background_color=(.1,.1,.1)
scene.render.resolution_x=640; scene.render.resolution_y=640; scene.render.resolution_percentage=100
cd=bpy.data.cameras.new('Review'); cam=bpy.data.objects.new('Review',cd); scene.collection.objects.link(cam); scene.camera=cam; cd.type='ORTHO'; cd.ortho_scale=7
cam.location=(-10,-3,4); cam.rotation_euler=(Vector((0,-.5,2.5))-cam.location).to_track_quat('-Z','Y').to_euler()
out=os.path.join(HERE,'New01AnimatedFrames~'); os.makedirs(out,exist_ok=True)
for f in range(35):
    scene.frame_set(f); scene.render.filepath=os.path.join(out,f'{f:03}.png'); bpy.ops.render.render(write_still=True)
