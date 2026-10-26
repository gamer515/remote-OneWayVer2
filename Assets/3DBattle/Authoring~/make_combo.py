"""Author four original sword attacks on the existing knight rig in Blender.
Run with Blender --background --factory-startup --python <this file>.
Only reads shared assets; all outputs stay in Assets/3DBattle.
"""
import bpy, json, math, os
from mathutils import Quaternion, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '../../..'))
OUT = os.path.join(ROOT, 'Assets/3DBattle/Animations')
SOURCE = os.path.join(ROOT, 'Assets/3DBattle/Authoring~')
os.makedirs(SOURCE, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT, 'Assets/UnityAssets/KnightAsset/KnightCharacter.fbx'))
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
idle = next(a for a in bpy.data.actions if a.name.endswith('|Idle_swordRight'))
rig.animation_data.action = idle
bpy.context.scene.frame_set(int(idle.frame_range[0]))
bpy.context.view_layer.update()
base = {b.name: b.matrix_basis.copy() for b in rig.pose.bones}
base_world = {b.name: b.matrix.copy() for b in rig.pose.bones}
print('MESHES=' + json.dumps([o.name for o in bpy.data.objects if o.type == 'MESH']))

def snapshot():
    return [{'name': b.name, 'position': list(b.matrix.translation),
             'rotation': list(b.matrix.to_quaternion())} for b in rig.pose.bones]

reference = snapshot()
rig.animation_data.action = None
for b in rig.pose.bones:
    b.rotation_mode = 'QUATERNION'

def rotate_world(name, axis, degrees):
    b = rig.pose.bones[name]
    m = b.matrix.copy()
    p = m.translation.copy()
    m = Quaternion(axis, math.radians(degrees)).to_matrix().to_4x4() @ m
    m.translation = p
    b.matrix = m
    bpy.context.view_layer.update()

def aim(name, target):
    b = rig.pose.bones[name]
    m = b.matrix.copy()
    current = m.to_3x3() @ Vector((0, 1, 0))
    direction = Vector(target) - m.translation
    q = current.rotation_difference(direction)
    p = m.translation.copy()
    m = q.to_matrix().to_4x4() @ m
    m.translation = p
    b.matrix = m
    bpy.context.view_layer.update()

def arm(side, hand, elbow_hint):
    upper = rig.pose.bones['UpperArm.' + side]
    lower = rig.pose.bones['LowerArm.' + side]
    shoulder = upper.matrix.translation.copy()
    hand = Vector(hand)
    delta = hand - shoulder
    length1, length2 = upper.bone.length, lower.bone.length
    distance = min(delta.length, (length1 + length2) * .98)
    forward = delta.normalized()
    bend = Vector(elbow_hint) - shoulder
    bend = (bend - forward * bend.dot(forward)).normalized()
    along = (length1**2 - length2**2 + distance**2) / (2 * distance)
    elbow = shoulder + forward * along + bend * math.sqrt(max(.001, length1**2 - along**2))
    aim(upper.name, elbow)
    aim(lower.name, hand)

# Each tuple: normalized time, torso yaw, forward lean, right hand XYZ,
# body crouch, left leg step, right leg step, full-body spin.
# Anticipation, fast contact, follow-through, held silhouette, recovery.
ATTACKS = [
 ('VoltSlash01', .48, [
  (0,0,0,None,0,0,0,0),(.18,-32,-7,(-1.85,.12,3.65),-.08,-9,12,0),
  (.38,22,12,(.55,-1.68,2.95),-.15,12,-12,0),
  (.49,36,15,(1.30,-.95,2.65),-.18,17,-15,0),
  (.68,24,8,(.85,-.85,2.85),-.10,10,-8,0),(1,0,0,None,0,0,0,0)]),
 ('VoltSlash02', .50, [
  (0,0,0,None,0,0,0,0),(.18,35,6,(1.15,-.55,3.15),-.13,12,-8,0),
  (.39,-24,6,(-1.25,-1.65,3.55),-.06,-12,13,0),
  (.52,-40,-6,(-1.90,-.65,3.95),.02,-18,15,0),
  (.72,-20,0,(-1.55,-.7,3.6),-.04,-10,8,0),(1,0,0,None,0,0,0,0)]),
 ('VoltSlash03', .65, [
  (0,0,0,None,0,0,0,0),(.15,-28,12,(-1.9,-.15,2.7),-.25,-20,18,-30),
  (.30,-12,8,(-1.8,-.7,3.1),-.12,-12,20,-110),
  (.46,18,5,(-1.5,-1.35,3.55),.08,15,-18,-240),
  (.60,30,-8,(-1.0,-1.4,4.2),.13,20,-12,-360),
  (.77,14,5,(-.9,-1.0,3.7),-.12,8,-8,-360),(1,0,0,None,0,0,0,-360)]),
 ('VoltSlash04', .80, [
  (0,0,0,None,0,0,0,0),(.18,-20,-12,(-.85,-.1,4.8),-.18,-16,16,0),
  (.34,-10,-18,(-.6,-.45,4.95),.20,-12,22,0),
  (.48,24,24,(-.35,-1.8,2.85),-.30,25,-22,0),
  (.57,32,28,(.25,-1.25,2.15),-.38,28,-24,0),
  (.76,16,12,(-.5,-1.1,2.8),-.18,16,-12,0),(1,0,0,None,0,0,0,0)])
]

scene = bpy.context.scene
scene.render.fps = 60
exports = []
blade_directions = [
    [None,(-.7,.1,.9),(1,-.2,-.25),(1,.1,-.7),(.6,-.2,-.5),None],
    [None,(.9,-.1,.2),(-1,-.2,.25),(-.8,.1,.8),(-.5,-.2,.7),None],
    [None,(-1,0,-.2),(-1,-.2,.1),(-1,-.3,.3),(-.4,-.2,1),(-.4,-.3,.7),None],
    [None,(0,.15,1),(0,.2,1),(0,-.8,-.65),(.1,-.3,-1),(.2,-.5,-.3),None]
]
for attack_index, (name, duration, poses) in enumerate(ATTACKS):
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    rig.animation_data.action = action
    end = round(duration * 60)
    previous_pose = {}
    for pose_index, (time, yaw, lean, hand, crouch, left_step, right_step, spin) in enumerate(poses):
        for b in rig.pose.bones:
            b.matrix_basis = base[b.name]
        bpy.context.view_layer.update()
        # Spine counter-rotation and a grounded staggered stance.
        rotate_world('Hips', (0,0,1), yaw * .25)
        rotate_world('Abdomen', (0,0,1), yaw * .35)
        rotate_world('Torso', (0,0,1), yaw * .40)
        rotate_world('Abdomen', (1,0,0), lean)
        rotate_world('Head', (0,0,1), -yaw * .55)
        rotate_world('UpperLeg.L', (1,0,0), left_step)
        rotate_world('LowerLeg.L', (1,0,0), -abs(left_step) * .65)
        rotate_world('UpperLeg.R', (1,0,0), right_step)
        rotate_world('LowerLeg.R', (1,0,0), -abs(right_step) * .65)
        if hand:
            arm('R', hand, (-2.1,-.25,3.0))
            arm('L', (1.0,-.45,2.85 + crouch), (1.8,.2,3.0))
            # Explicit wrist follow-through makes the blade cut across the target,
            # rather than carrying an upright sword through every hand position.
            palm = rig.pose.bones['Palm.R']
            location = palm.matrix.translation.copy()
            delta = Vector((0,0,1)).rotation_difference(Vector(blade_directions[attack_index][pose_index]).normalized())
            m = (delta @ base_world['Palm.R'].to_quaternion()).to_matrix().to_4x4()
            m.translation = location
            palm.matrix = m
            bpy.context.view_layer.update()
        b = rig.pose.bones['Body']
        m = b.matrix.copy()
        m.translation.z += crouch
        b.matrix = m
        bpy.context.view_layer.update()
        rotate_world('Bone', (0,0,1), spin)
        # Keep feet attached to the actual ankles (the supplied rig has detached foot controls).
        for side in ['L','R']:
            foot = rig.pose.bones['Foot.' + side]
            m = foot.matrix.copy()
            m.translation = rig.pose.bones['LowerLeg.' + side].tail
            foot.matrix = m
        bpy.context.view_layer.update()
        for b in rig.pose.bones:
            frame = round(time * end)
            q = b.rotation_quaternion.copy()
            if b.name in previous_pose and previous_pose[b.name].dot(q) < 0:
                q.negate()
                b.rotation_quaternion = q
            previous_pose[b.name] = q.copy()
            b.keyframe_insert('location', frame=frame, group=b.name)
            b.keyframe_insert('rotation_quaternion', frame=frame, group=b.name)
            b.keyframe_insert('scale', frame=frame, group=b.name)
    # Blender quaternion keys need hemisphere continuity across the full spin.
    for slot in action.slots:
        for layer in action.layers:
            for strip in layer.strips:
                bag = strip.channelbag(slot)
                if bag:
                    for curve in bag.fcurves:
                        for key in curve.keyframe_points:
                            key.interpolation = 'LINEAR'
    samples = []
    for f in range(end+1):
        scene.frame_set(f)
        bpy.context.view_layer.update()
        samples.append({'time':f/60.0, 'bones':snapshot()})
    exports.append({'name':name,'duration':end/60.0,'samples':samples})
    for label, normalized in [('Windup',.18),('Contact',.4),('Recover',.75)]:
        action.pose_markers.new(label).frame = round(normalized*end)

with open(os.path.join(SOURCE,'combo_samples.json'),'w',encoding='utf-8') as f:
    json.dump({'reference':reference,'clips':exports},f,separators=(',',':'))
rig.animation_data.action = bpy.data.actions['VoltSlash01']
scene.frame_start = 0
scene.frame_end = 29
scene.frame_set(0)
# A visible sword proxy on the grip is included for authoring; game equipment
# continues to use the existing battle prefab's sword.
bpy.ops.mesh.primitive_cube_add(size=1)
sword = bpy.context.object
sword.name = 'Sword Preview (authoring only)'
sword.dimensions = (.12,.055,1.8)
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
palm = rig.pose.bones['Palm.R']
grip = rig.matrix_world @ palm.matrix.translation
sword.location = grip + Vector((0,0,.9))
world = sword.matrix_world.copy()
sword.parent = rig
sword.parent_type = 'BONE'
sword.parent_bone = 'Palm.R'
sword.matrix_world = world
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SOURCE,'KnightVoltCombo.blend'))
print('AUTHORED: four 60fps sword attacks; source and sampled poses saved.')
