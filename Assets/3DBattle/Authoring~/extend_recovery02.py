import bpy, os, shutil, datetime, json, math, ast
from mathutils import Matrix, Vector
HERE=os.path.dirname(os.path.abspath(__file__)); path=os.path.join(HERE,'KnightVoltCombo_UserKeys.blend')
backup=os.path.join(HERE,'Backups','BeforeLongerRecovery02_'+datetime.datetime.now().strftime('%Y%m%d_%H%M%S')); os.makedirs(backup); shutil.copy2(path,backup)
rig=bpy.data.objects['HumanArmature']; scene=bpy.context.scene
def sig():
    return {(a.name,f.data_path,f.array_index,float(k.co.x)):float(k.co.y) for a in bpy.data.actions for l in a.layers for s in l.strips for bag in s.channelbags for f in bag.fcurves for k in f.keyframe_points}
before=sig()
rig.animation_data.action=bpy.data.actions['VoltSlash01']; scene.frame_set(0)
base={b.name:b.matrix_basis.copy() for b in rig.pose.bones}
tree=ast.parse(open(os.path.join(HERE,'author_user_slash01.py'),encoding='utf-8').read())
for node in tree.body:
    if isinstance(node,ast.FunctionDef) and node.name in ['update','aim','solve']:
        exec(compile(ast.Module(body=[node],type_ignores=[]),'<pose-math>','exec'))
def mix(a,b,t):
    p,q,s=a.decompose(); p2,q2,s2=b.decompose()
    if q.dot(q2)<0:q2.negate()
    return Matrix.LocRotScale(p.lerp(p2,t),q.slerp(q2,t),s.lerp(s2,t))
for name,hold,end in [('VoltSlash02',36,47)]:
    action=bpy.data.actions[name]; rig.animation_data.action=action; scene.frame_set(hold)
    source={b.name:b.matrix_basis.copy() for b in rig.pose.bones}; sw={b.name:b.matrix.copy() for b in rig.pose.bones}
    rig.animation_data.action=None
    for b in rig.pose.bones:b.matrix_basis=base[b.name]
    update(); root=rig.pose.bones['Bone']; m=root.matrix.copy(); m.translation.y-=1.65; root.matrix=m; update()
    dest={b.name:b.matrix_basis.copy() for b in rig.pose.bones}; dw={b.name:b.matrix.copy() for b in rig.pose.bones}
    for frame in range(hold+1,end+1):
        scene.frame_set(frame); rig.animation_data.action=None
        u=(frame-hold)/(end-hold); t=u*u*(3-2*u)
        for b in rig.pose.bones:b.matrix_basis=mix(source[b.name],dest[b.name],t)
        update()
        if frame<end:
            for side in ['L','R']:
                n='Foot.'+side; m=mix(sw[n],dw[n],t)
                target=m.translation.copy(); target.z+=.10*math.sin(math.pi*u)
                pole=rig.pose.bones['LowerLeg.'+side].head.copy()+Vector((0,-.3,0))
                solve('UpperLeg.'+side,'LowerLeg.'+side,target,pole)
                m.translation=rig.pose.bones['LowerLeg.'+side].tail; rig.pose.bones[n].matrix=m; update()
        rig.animation_data.action=action
        for b in rig.pose.bones:
            for prop in ['location','rotation_quaternion','scale']:b.keyframe_insert(prop,frame=frame,group=b.name)
    for l in action.layers:
        for s in l.strips:
            for bag in s.channelbags:
                for fc in bag.fcurves:
                    for k in fc.keyframe_points:
                        if k.co.x>hold:k.interpolation='LINEAR'
    action['chain_frame']=hold
    if name=='VoltSlash01':action['travel_reference_frames']=42
    action['recovery_notes']=f'Preserved frames 0-{hold}; idle recovery {hold+1}-{end}. Chain to next attack at frame {hold}.'
    action.pose_markers.new('Chain point').frame=hold; action.pose_markers.new('Idle recovery end').frame=end
after=sig(); assert all(after[k]==v for k,v in before.items() if not(k[0]=='VoltSlash02' and k[3]>36))
scene.frame_start=0; scene.frame_end=47; rig.animation_data.action=bpy.data.actions['VoltSlash02']; scene.frame_set(36)
bpy.context.preferences.filepaths.save_version=0; bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE,'KnightVoltCombo_Recovery02.blend'))
print('VERIFIED',json.dumps({'allExistingKeyValuesUnchanged':len(before),'backup':backup,'ranges':{n:list(bpy.data.actions[n].frame_range) for n in ['VoltSlash01','VoltSlash02']}}))

