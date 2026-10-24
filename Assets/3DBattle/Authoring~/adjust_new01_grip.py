import bpy, os, math, shutil, datetime, ast, json
from mathutils import Vector, Quaternion
HERE=os.path.dirname(os.path.abspath(__file__)); path=os.path.join(HERE,'KnightVoltCombo_UserKeys.blend')
backup=os.path.join(HERE,'Backups','BeforeNew01Grip_'+datetime.datetime.now().strftime('%Y%m%d_%H%M%S')); os.makedirs(backup); shutil.copy2(path,backup)
rig=bpy.data.objects['HumanArmature']; scene=bpy.context.scene; action=bpy.data.actions['voltslashnew01']
def sig():
    return {(a.name,fc.data_path,fc.array_index,float(k.co.x)):float(k.co.y) for a in bpy.data.actions for l in a.layers for s in l.strips for bag in s.channelbags for fc in bag.fcurves for k in fc.keyframe_points}
before=sig(); rig.animation_data.action=bpy.data.actions['VoltSlash01']; scene.frame_set(0); idleQ=rig.pose.bones['Palm.R'].matrix.to_quaternion()
tree=ast.parse(open(os.path.join(HERE,'two_hand_slash02_keys.py'),encoding='utf-8').read())
for node in tree.body:
    if isinstance(node,ast.FunctionDef) and node.name in ['update','aim','solve']:exec(compile(ast.Module(body=[node],type_ignores=[]),'<pose-math>','exec'))
rig.animation_data.action=action
def aim_wrist(lower,target,side):
    m=lower.matrix.copy(); p=m.translation.copy()
    q=(rig.pose.bones['Palm.'+side].head-p).rotation_difference(Vector(target)-p)
    m=q.to_matrix().to_4x4()@m; m.translation=p; lower.matrix=m; update()
solve_node=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='solve')
exec(ast.unparse(solve_node).replace('aim(l, target)', 'aim_wrist(l, target, side)'))
mesh=bpy.data.objects['Knight']; indices={s:mesh.vertex_groups['Foot.'+s].index for s in ['L','R']}
footverts={s:[v.index for v in mesh.data.vertices if any(g.group==indices[s] and g.weight>.25 for g in v.groups)] for s in ['L','R']}
def floorcheck():
    evaluated=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get()); geometry=evaluated.to_mesh()
    result={s:min((evaluated.matrix_world@geometry.vertices[i].co).z for i in footverts[s]) for s in ['L','R']}
    evaluated.to_mesh_clear(); return result
scene.frame_set(0); baseline=floorcheck(); checks=[]
changed=['UpperArm.R','LowerArm.R','Palm.R','UpperArm.L','LowerArm.L','Palm.L']
for f,d in [(1,(0,1,.06)),(2,(0,.25,.968)),(3,(0,-.8,.6)),(4,(0,-1,0))]:
    scene.frame_set(f); update()
    feetBefore=floorcheck(); legBefore={b.name:b.matrix.copy() for b in rig.pose.bones if 'Leg' in b.name or 'Foot' in b.name}
    rig.animation_data.action=None
    blade=Vector(d).normalized(); qr=Vector((0,0,1)).rotation_difference(blade)@idleQ
    right=rig.pose.bones['Palm.R']; grip=right.head.copy()
    for iteration in range(12):
        for side,offset in [('R',Vector((0,0,0))),('L',-blade*.4)]:
            u=rig.pose.bones['UpperArm.'+side]; l=rig.pose.bones['LowerArm.'+side]; p=rig.pose.bones['Palm.'+side]
            limit=((l.head-u.head).length+(p.head-l.head).length)*.97
            delta=grip+offset-u.head
            if delta.length>limit: grip=u.head+delta.normalized()*limit-offset
    solve('R',grip,rig.pose.bones['LowerArm.R'].head.copy())
    m=qr.to_matrix().to_4x4(); m.translation=right.head; right.matrix=m; update()
    target=right.head-blade*.4; pole=rig.pose.bones['LowerArm.L'].head.copy()
    solve('L',target,pole)
    left=rig.pose.bones['Palm.L']; m=(qr@Quaternion((0,1,0),math.pi)).to_matrix().to_4x4(); m.translation=left.head; left.matrix=m; update()
    rig.animation_data.action=action
    for n in changed:
        for p in ['location','rotation_quaternion','scale']:rig.pose.bones[n].keyframe_insert(p,frame=f,group=n)
    assert max(abs(rig.pose.bones[n].matrix[r][c]-m[r][c]) for n,m in legBefore.items() for r in range(4) for c in range(4))<.00001
    checks.append({'frame':f,'footMeshMinimumZ':feetBefore,'bladeAxis':list(blade),'gripError':(left.head-target).length})
after=sig(); allowed={f'pose.bones["{n}"].{p}' for n in changed for p in ['location','rotation_quaternion','scale']}
assert all(after[k]==v for k,v in before.items() if not(k[0]==action.name and 1<=k[3]<=4 and k[1] in allowed))
assert all(c['gripError']<.01 for c in checks), checks
print('VERIFIED',json.dumps({'baselineFootMeshZ':baseline,'checks':checks,'legsAndOtherActionsUnchanged':True,'backup':backup}))
scene.frame_start=0; scene.frame_end=4; scene.frame_set(1); bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE,'KnightVoltCombo_New01Grip.blend'))
with open(os.path.join(HERE,'New01GripChecks.json'),'w') as f:json.dump({'baseline':baseline,'frames':checks},f,indent=2)
scene.render.engine='BLENDER_WORKBENCH'; scene.display.shading.light='STUDIO'; scene.display.shading.color_type='MATERIAL'; scene.display.shading.show_cavity=True
scene.display.shading.background_type='VIEWPORT'; scene.display.shading.background_color=(.1,.1,.1)
scene.render.resolution_x=640; scene.render.resolution_y=640; scene.render.resolution_percentage=100
cd=bpy.data.cameras.new('Review'); cam=bpy.data.objects.new('Review',cd); scene.collection.objects.link(cam); scene.camera=cam; cd.type='ORTHO'; cd.ortho_scale=7
for f in [1,2,3,4]:
    scene.frame_set(f); cam.location=(-10,-.5,3); cam.rotation_euler=(Vector((0,-.5,2.5))-cam.location).to_track_quat('-Z','Y').to_euler(); scene.render.filepath=os.path.join(HERE,f'New01Grip_{f}.png'); bpy.ops.render.render(write_still=True)

