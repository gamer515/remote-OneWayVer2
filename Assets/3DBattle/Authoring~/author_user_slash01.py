import bpy, math, os, json, shutil, datetime
from mathutils import Vector, Quaternion, Matrix
HERE=os.path.dirname(os.path.abspath(__file__))
path=os.path.join(HERE,'KnightVoltCombo_UserKeys.blend')
backup=os.path.join(HERE,'Backups','BeforeDynamicSlash_'+datetime.datetime.now().strftime('%Y%m%d_%H%M%S'))
os.makedirs(backup); shutil.copy2(path,backup)
rig=bpy.data.objects['HumanArmature']; scene=bpy.context.scene; action=bpy.data.actions['VoltSlash01']
rig.animation_data.action=action
def update(): bpy.context.view_layer.update()
def signature(a):
    return [(f.data_path,f.array_index,[(tuple(k.co),tuple(k.handle_left),tuple(k.handle_right),k.interpolation) for k in f.keyframe_points]) for l in a.layers for s in l.strips for bag in s.channelbags for f in bag.fcurves]
others={a.name:signature(a) for a in bpy.data.actions if a!=action}
scene.frame_set(35); user35={b.name:b.matrix_basis.copy() for b in rig.pose.bones}
scene.frame_set(0); base={b.name:b.matrix_basis.copy() for b in rig.pose.bones}; world={b.name:b.matrix.copy() for b in rig.pose.bones}
def smooth(a,b,t):
    u=max(0,min(1,(t-a)/(b-a))); return u*u*(3-2*u)
def curve(t,knots):
    for (a,x),(b,y) in zip(knots,knots[1:]):
        if t<=b:
            u=smooth(a,b,t)
            return Vector(x).lerp(Vector(y),u) if isinstance(x,(tuple,Vector)) else x+(y-x)*u
    return Vector(knots[-1][1]) if isinstance(knots[-1][1],(tuple,Vector)) else knots[-1][1]
def rotate(name,axis,angle):
    b=rig.pose.bones[name]; m=b.matrix.copy(); p=m.translation.copy()
    m=Quaternion(axis,math.radians(angle)).to_matrix().to_4x4()@m; m.translation=p; b.matrix=m; update()
def aim(b,target):
    m=b.matrix.copy(); p=m.translation.copy()
    q=(m.to_3x3()@Vector((0,1,0))).rotation_difference(Vector(target)-p)
    m=q.to_matrix().to_4x4()@m; m.translation=p; b.matrix=m; update()
def solve(un,ln,target,pole):
    u=rig.pose.bones[un]; l=rig.pose.bones[ln]; start=u.head.copy(); v=Vector(target)-start
    d=min(v.length,(u.length+l.length)*.999); direction=v.normalized()
    bend=Vector(pole)-start; bend=(bend-direction*bend.dot(direction)).normalized()
    along=(u.length*u.length-l.length*l.length+d*d)/(2*d)
    joint=start+direction*along+bend*math.sqrt(max(0,u.length*u.length-along*along))
    aim(u,joint); aim(l,Vector(target)); return l.tail.copy()
# Clear only this action's curves, keeping its identity and the other actions intact.
for layer in action.layers:
    for strip in layer.strips:
        for bag in strip.channelbags:
            for fc in list(bag.fcurves): bag.fcurves.remove(fc)
for marker in list(action.pose_markers): action.pose_markers.remove(marker)
END=42; last={}; checks=[]
for frame in range(END+1):
    t=frame/END; scene.frame_set(frame); rig.animation_data.action=None
    for b in rig.pose.bones: b.matrix_basis=base[b.name]
    update()
    travel=1.65*smooth(.10,.85,t)
    lower=curve(t,[(0,0),(.25,-.40),(.56,-.58),(.82,-.66),(1,-.66)])
    body=rig.pose.bones['Body']; m=body.matrix.copy(); m.translation+=Vector((0,-travel,lower)); body.matrix=m; update()
    rotate('Body',(0,0,1),curve(t,[(0,0),(.30,18),(.50,8),(.80,0),(1,0)]))
    # Distribute the forward lean to avoid folding a single waist joint.
    lean=smooth(.1,.85,t)
    rig.pose.bones['Hips'].rotation_quaternion=base['Hips'].to_quaternion().slerp(Quaternion((1,0,0),math.radians(24)),lean)
    rig.pose.bones['Abdomen'].rotation_quaternion=base['Abdomen'].to_quaternion().slerp(Quaternion((1,0,0),math.radians(12)),lean)
    rig.pose.bones['Torso'].rotation_quaternion=base['Torso'].to_quaternion()
    rig.pose.bones['Head'].rotation_quaternion=base['Head'].to_quaternion().slerp(Quaternion((1,0,0),math.radians(-12)),lean)
    update()
    rotate('Torso',(0,0,1),curve(t,[(0,0),(.30,22),(.55,0),(.80,0),(1,0)]))
    # Right foot steps first and remains planted. Left foot then passes it.
    for side,a,b,advance,x in [('R',.04,.28,.62,-.82),('L',.36,.82,2.12,.82)]:
        u=smooth(a,b,t); ankle=world['Foot.'+side].translation.copy()
        ankle.y-=advance*u; ankle.x+=(x-ankle.x)*u
        phase=max(0,min(1,(t-a)/(b-a))); ankle.z+=.22*math.sin(math.pi*phase)
        solve('UpperLeg.'+side,'LowerLeg.'+side,ankle,(ankle.x,ankle.y-3,1))
        foot=rig.pose.bones['Foot.'+side]; m=world['Foot.'+side].copy(); m.translation=rig.pose.bones['LowerLeg.'+side].tail; foot.matrix=m; update()
    shoulder=rig.pose.bones['UpperArm.R'].head.copy()
    finish=shoulder+Vector((-1.97,-.06,0))
    hand=curve(t,[(0,tuple(world['Palm.R'].translation)),(.28,(.85,-travel-.8,3.12)),(.43,(.65,-travel-1.15,2.97)),(.59,(-.35,-travel-1.65,2.85)),(.80,tuple(finish)),(1,tuple(finish))])
    solve('UpperArm.R','LowerArm.R',hand,(-1.3,-travel-.4,2.1))
    blade=curve(t,[(0,(0,0,1)),(.28,(.1,1,.04)),(.43,(.9,.4,.02)),(.59,(.6,-.8,0)),(.80,(-1,0,0)),(1,(-1,0,0))]).normalized()
    palm=rig.pose.bones['Palm.R']; m=(Vector((0,0,1)).rotation_difference(blade)@world['Palm.R'].to_quaternion()).to_matrix().to_4x4(); m.translation=palm.head; palm.matrix=m; update()
    # The free hand counterbalances behind the torso.
    target=Vector((1.05,-travel+.20,2.25+lower*.35))
    lh=world['Palm.L'].translation.lerp(target,smooth(0,.6,t))
    solve('UpperArm.L','LowerArm.L',lh,(1.5,-travel+.25,2.8+lower*.3))
    if frame==0:
        for b in rig.pose.bones: b.matrix_basis=base[b.name]
        update()
    rig.animation_data.action=action
    for b in rig.pose.bones:
        q=b.rotation_quaternion.copy()
        if b.name in last and q.dot(last[b.name])<0: q.negate(); b.rotation_quaternion=q
        last[b.name]=q.copy()
        for prop in ['location','rotation_quaternion','scale']: b.keyframe_insert(prop,frame=frame,group=b.name)
    checks.append({'frame':frame,'feet':{s:list(rig.pose.bones['Foot.'+s].head) for s in ['L','R']},'gaps':{s:(rig.pose.bones['Foot.'+s].head-rig.pose.bones['LowerLeg.'+s].tail).length for s in ['L','R']}})
for l in action.layers:
    for s in l.strips:
        for bag in s.channelbags:
            for fc in bag.fcurves:
                for k in fc.keyframe_points: k.interpolation='LINEAR'
for name,f in [('Ready',0),('Right step - windup',12),('Horizontal cut',25),('Left foot planted',35),('Finish - chain to VoltSlash02',42)]: action.pose_markers.new(name).frame=f
action['forward_travel']=1.65; action['travel_start']=.10; action['travel_end']=.85
action['notes']='Right foot then left foot; no idle recovery. Final pose based on user frame 35.'
scene.render.fps=60; scene.render.fps_base=1; scene.frame_start=0; scene.frame_end=END
# Correct the existing authoring-only sword proxy's bone attachment at the idle grip.
scene.frame_set(0); update(); sword=bpy.data.objects['Sword Preview (authoring only)']; palm=rig.pose.bones['Palm.R']
desired=Matrix.Translation(palm.head+Vector((0,0,.9)))
parent=rig.matrix_world@palm.matrix@Matrix.Translation((0,palm.length,0))
sword.matrix_parent_inverse=Matrix.Identity(4); sword.matrix_basis=parent.inverted()@desired; sword.hide_render=False
scene.frame_set(END); update()
assert all(signature(bpy.data.actions[n])==sig for n,sig in others.items())
assert checks[12]['feet']['R'][1]<checks[12]['feet']['L'][1]
assert checks[-1]['feet']['L'][1]<checks[-1]['feet']['R'][1]
assert max(v for c in checks[1:] for v in c['gaps'].values())<.0001
blade_axis=sword.matrix_world.to_3x3()@Vector((0,0,1)); blade_axis.normalize()
assert abs(blade_axis.z)<.001 and blade_axis.x<-.99
print('VALIDATED',json.dumps({'range':list(action.frame_range),'otherActionsUnchanged':True,'finalBladeAxis':list(blade_axis),'backup':backup,'step12':checks[12],'finish':checks[-1]}))
bpy.context.preferences.filepaths.save_version=0; bpy.ops.wm.save_as_mainfile(filepath=path)
with open(os.path.join(HERE,'DynamicSlash01_checks.json'),'w') as f: json.dump(checks,f,indent=2)
# Render an authoring preview, without saving preview cameras into the file.
scene.render.engine='BLENDER_WORKBENCH'; scene.display.shading.light='STUDIO'; scene.display.shading.color_type='MATERIAL'; scene.display.shading.show_shadows=True; scene.display.shading.show_cavity=True
scene.display.shading.background_type='VIEWPORT'; scene.display.shading.background_color=(.10,.10,.10)
scene.render.resolution_x=640; scene.render.resolution_y=640; scene.render.resolution_percentage=100
cd=bpy.data.cameras.new('ReviewCamera'); cam=bpy.data.objects.new('ReviewCamera',cd); scene.collection.objects.link(cam); scene.camera=cam; cd.type='ORTHO'; cd.ortho_scale=8.0
def camera(pos):
    cam.location=pos; cam.rotation_euler=(Vector((-.7,-1.2,2.4))-cam.location).to_track_quat('-Z','Y').to_euler()
camera((-.7,-14,2.4)); scene.frame_set(END); scene.render.filepath=os.path.join(HERE,'DynamicSlash01_FinishFront.png'); bpy.ops.render.render(write_still=True)
camera((-9,-12,7)); scene.render.filepath=os.path.join(HERE,'DynamicSlash01_FinishAngle.png'); bpy.ops.render.render(write_still=True)
frames=os.path.join(HERE,'DynamicSlash01Frames~'); os.makedirs(frames,exist_ok=True)
for frame in range(END+1):
    scene.frame_set(frame); scene.render.filepath=os.path.join(frames,f'{frame:03}.png'); bpy.ops.render.render(write_still=True)
