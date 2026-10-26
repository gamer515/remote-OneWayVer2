"""Replace only VoltSlash01 using the user's five pose references.
Reads the saved original; writes a separate, editable reference version.
Coordinates: Blender -Y forward, -X character right, Z up.
"""
import bpy, json, math, os
from mathutils import Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, 'KnightVoltCombo.blend'))
bpy.context.preferences.filepaths.save_version = 0
rig = bpy.data.objects['HumanArmature']
scene = bpy.context.scene
scene.render.fps = 60
scene.render.fps_base = 1
idle = next(a for a in bpy.data.actions if a.name.endswith('|Idle_swordRight'))
rig.animation_data.action = idle
scene.frame_set(int(idle.frame_range[0]))
bpy.context.view_layer.update()
base = {b.name: b.matrix_basis.copy() for b in rig.pose.bones}
world = {b.name: b.matrix.copy() for b in rig.pose.bones}
rig.animation_data.action = None
old = bpy.data.actions.get('VoltSlash01')
if old:
    old.name = 'VoltSlash01_PreReference_Backup'
    old.use_fake_user = True
action = bpy.data.actions.new('VoltSlash01')
action.use_fake_user = True
rig.animation_data.action = action

def smooth(a, b, t):
    u = min(1.0, max(0.0, (t-a)/(b-a)))
    return u*u*(3-2*u)

def curve(t, knots):
    for i in range(len(knots)-1):
        a, va = knots[i]
        b, vb = knots[i+1]
        if t <= b:
            u = smooth(a,b,t)
            if isinstance(va, tuple):
                return Vector(va).lerp(Vector(vb), u)
            return va+(vb-va)*u
    return Vector(knots[-1][1]) if isinstance(knots[-1][1],tuple) else knots[-1][1]

def rotate(name, axis, degrees):
    b = rig.pose.bones[name]
    m = b.matrix.copy()
    pos = m.translation.copy()
    m = Quaternion(axis, math.radians(degrees)).to_matrix().to_4x4() @ m
    m.translation = pos
    b.matrix = m
    bpy.context.view_layer.update()

def aim(name, target):
    b = rig.pose.bones[name]
    m = b.matrix.copy()
    pos = m.translation.copy()
    direction = Vector(target)-pos
    q = (m.to_3x3() @ Vector((0,1,0))).rotation_difference(direction)
    m = q.to_matrix().to_4x4() @ m
    m.translation = pos
    b.matrix = m
    bpy.context.view_layer.update()

def solve(upper_name, lower_name, target, pole):
    upper, lower = rig.pose.bones[upper_name], rig.pose.bones[lower_name]
    start = upper.matrix.translation.copy()
    target = Vector(target)
    delta = target-start
    l1 = (world[lower_name].translation-world[upper_name].translation).length
    l2 = lower.bone.length
    distance = min(delta.length, (l1+l2)*.995)
    direction = delta.normalized()
    bend = Vector(pole)-start
    bend = (bend-direction*bend.dot(direction)).normalized()
    along = (l1*l1-l2*l2+distance*distance)/(2*distance)
    joint = start+direction*along+bend*math.sqrt(max(0,l1*l1-along*along))
    aim(upper_name,joint)
    aim(lower_name,target)
    return lower.tail.copy()

END = 54  # .9 seconds: readable anticipation, fast slash, held finish, recovery.
last = {}
samples = []
checks = []
for f in range(END+1):
    t=f/END
    scene.frame_set(f)
    # Detach evaluation while constructing each pose, then key the result.
    rig.animation_data.action = None
    for b in rig.pose.bones:
        b.rotation_mode='QUATERNION'
        b.matrix_basis=base[b.name]
    bpy.context.view_layer.update()
    travel=1.55*smooth(.15,.62,t)
    recovery=smooth(.82,1,t)
    crouch=curve(t,[(0,0),(.24,-.45),(.40,-.56),(.58,-.50),(.76,-.35),(1,0)])
    hip_yaw=curve(t,[(0,0),(.27,22),(.40,10),(.60,-35),(.78,-27),(1,0)])
    chest_yaw=curve(t,[(0,0),(.27,24),(.40,14),(.60,-18),(.78,-12),(1,0)])
    lean=curve(t,[(0,0),(.25,7),(.46,15),(.65,8),(1,0)])
    body=rig.pose.bones['Body']
    m=body.matrix.copy()
    m.translation+=Vector((0,-travel,crouch))
    body.matrix=m
    bpy.context.view_layer.update()
    rotate('Body',(0,0,1),hip_yaw)
    rotate('Abdomen',(0,0,1),chest_yaw*.55)
    rotate('Torso',(0,0,1),chest_yaw*.45)
    rotate('Abdomen',(1,0,0),lean)
    rotate('Head',(0,0,1),-(hip_yaw+chest_yaw)*.75)
    # Feet remain planted outside their explicit lift/step windows.
    feet={}
    for side in ['L','R']:
        ankle=world['Foot.'+side].translation.copy()
        if side=='L':
            step=smooth(.06,.25,t)
            ankle.y-=.85*step
            ankle.z+=.17*math.sin(math.pi*min(1,max(0,(t-.06)/.19)))
            ankle.x+=.11*step*(1-recovery)
        else:
            step=smooth(.31,.61,t)
            ankle.y-=2.00*step
            ankle.z+=.29*math.sin(math.pi*min(1,max(0,(t-.31)/.30)))
            ankle.x-=.12*step*(1-recovery)
        final=world['Foot.'+side].translation+Vector((0,-travel,0))
        ankle=ankle.lerp(final,recovery)
        if 0<recovery<1: ankle.z+=.08*math.sin(math.pi*recovery)
        pole=Vector((ankle.x,-travel-2.5,.8))
        actual=solve('UpperLeg.'+side,'LowerLeg.'+side,ankle,pole)
        foot=rig.pose.bones['Foot.'+side]
        m=world['Foot.'+side].copy()
        m.translation=actual
        foot.matrix=m
        bpy.context.view_layer.update()
        rotate('Foot.'+side,(0,0,1),hip_yaw*(.18 if side=='L' else .35))
        feet[side]=list(actual)
    hand=curve(t,[(0,tuple(world['Palm.R'].translation)),
        (.24,(1.0,-.85,3.30)),(.34,(.88,-1.05,3.20)),
        (.45,(.15,-1.80,3.10)),(.59,(-1.72,-.65,2.95)),
        (.76,(-1.85,-.12,2.80)),(1,tuple(world['Palm.R'].translation))])
    hand+=Vector((0,-travel,crouch*.65))
    solve('UpperArm.R','LowerArm.R',hand,(-1.35,-travel-.7,2.65+crouch))
    # Blade initially upright, points behind on the left, cuts horizontally,
    # and settles extended to the character's right.
    blade=curve(t,[(0,(0,0,1)),(.24,(.18,1,.02)),(.34,(.85,.5,.03)),
        (.45,(1,-.1,0)),(.59,(-.75,-.65,-.02)),(.76,(-1,.05,-.12)),(1,(0,0,1))]).normalized()
    palm=rig.pose.bones['Palm.R']
    m=(Vector((0,0,1)).rotation_difference(blade) @ world['Palm.R'].to_quaternion()).to_matrix().to_4x4()
    m.translation=palm.matrix.translation
    palm.matrix=m
    bpy.context.view_layer.update()
    left_hand=curve(t,[(0,tuple(world['Palm.L'].translation)),(.25,(1.05,.02,1.95)),
        (.58,(1.35,.35,2.10)),(.78,(1.0,.1,2.0)),(1,tuple(world['Palm.L'].translation))])
    left_hand+=Vector((0,-travel,crouch*.5))
    solve('UpperArm.L','LowerArm.L',left_hand,(1.8,-travel+.15,2.85+crouch))
    # The first and last authored poses match idle exactly (with root advance).
    if f in [0,END]:
        for b in rig.pose.bones: b.matrix_basis=base[b.name]
        bpy.context.view_layer.update()
        root=rig.pose.bones['Bone']
        m=root.matrix.copy(); m.translation.y-=travel; root.matrix=m
        bpy.context.view_layer.update()
    rig.animation_data.action=action
    for b in rig.pose.bones:
        q=b.rotation_quaternion.copy()
        if b.name in last and last[b.name].dot(q)<0: q.negate(); b.rotation_quaternion=q
        last[b.name]=q.copy()
        b.keyframe_insert('location',frame=f,group=b.name)
        b.keyframe_insert('rotation_quaternion',frame=f,group=b.name)
        b.keyframe_insert('scale',frame=f,group=b.name)
    bpy.context.view_layer.update()
    samples.append({'time':f/60,'forwardTravel':travel,'bones':[
        {'name':b.name,'position':list(b.matrix.translation),'rotation':list(b.matrix.to_quaternion())} for b in rig.pose.bones]})
    checks.append({'frame':f,'travel':travel,'left':feet['L'],'right':feet['R']})

for slot in action.slots:
    for layer in action.layers:
        for strip in layer.strips:
            bag=strip.channelbag(slot)
            if bag:
                for fc in bag.fcurves:
                    for k in fc.keyframe_points: k.interpolation='LINEAR'
for label,frame in [('01 Ready',0),('02 Left foot - windup',14),('03 Sweep',24),('04 Right foot - contact',32),('05 Finish',41),('Return',54)]:
    action.pose_markers.new(label).frame=frame

with open(os.path.join(HERE,'combo_samples.json'),encoding='utf-8') as stream: data=json.load(stream)
data['clips'][0]={'name':'VoltSlash01','duration':END/60,'samples':samples}
with open(os.path.join(HERE,'combo_samples.json'),'w',encoding='utf-8') as stream: json.dump(data,stream,separators=(',',':'))
with open(os.path.join(HERE,'reference_slash_checks.json'),'w',encoding='utf-8') as stream: json.dump(checks,stream,indent=2)
# Custom property documents the extraction curve for subsequent manual exports.
action['forward_travel']=1.55
action['travel_start']=.15
action['travel_end']=.62
scene.frame_start=0; scene.frame_end=END; scene.frame_set(0)
bpy.context.view_layer.objects.active=rig
for o in bpy.context.selected_objects: o.select_set(False)
rig.select_set(True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE,'KnightVoltCombo_ReferenceSlash.blend'))
print('REFERENCE_SLASH_READY: 55 frames, .9 seconds; original .blend preserved; only clip 01 samples replaced.')
