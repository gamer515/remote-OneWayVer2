import bpy, math, os, json, sys
from mathutils import Vector, Quaternion
HERE=os.path.dirname(os.path.abspath(__file__))
rig=bpy.data.objects['HumanArmature']; scene=bpy.context.scene
scene.frame_set(35)
act=rig.animation_data.action
def update(): bpy.context.view_layer.update()
def aim(name,target):
    b=rig.pose.bones[name]; m=b.matrix.copy(); p=m.translation.copy()
    q=(m.to_3x3()@Vector((0,1,0))).rotation_difference(Vector(target)-p)
    m=q.to_matrix().to_4x4()@m; m.translation=p; b.matrix=m; update()
def solve(up,lo,target,pole):
    u=rig.pose.bones[up]; l=rig.pose.bones[lo]; start=u.head.copy()
    v=Vector(target)-start; d=min(v.length,(u.length+l.length)*.999)
    direction=v.normalized(); bend=Vector(pole)-start
    bend=(bend-direction*bend.dot(direction)).normalized()
    along=(u.length*u.length-l.length*l.length+d*d)/(2*d)
    joint=start+direction*along+bend*math.sqrt(max(0,u.length*u.length-along*along))
    aim(up,joint); aim(lo,Vector(target))
def snapshot():
    return {(a.name,f.data_path,f.array_index,float(k.co.x)):float(k.co.y) for a in bpy.data.actions for layer in a.layers for strip in layer.strips for bag in strip.channelbags for f in bag.fcurves for k in f.keyframe_points}
before=snapshot()
fix='--fix' in sys.argv
if fix:
    targets={s:rig.pose.bones['Foot.'+s].matrix.copy() for s in ['L','R']}
    hand=rig.pose.bones['Palm.R'].head.copy()
    rig.animation_data.action=None
    # Keep the user's lowered body and strike placement; remove stacked hip twists.
    rig.pose.bones['Body'].rotation_quaternion=Quaternion((1,0,0,0))
    rig.pose.bones['Hips'].rotation_quaternion=Quaternion((1,0,0,0))
    rig.pose.bones['Abdomen'].rotation_quaternion=Quaternion((1,0,0),math.radians(18))
    rig.pose.bones['Torso'].rotation_quaternion=Quaternion((1,0,0,0))
    rig.pose.bones['Head'].rotation_quaternion=Quaternion((1,0,0),math.radians(-12))
    update()
    body=rig.pose.bones['Body']; m=body.matrix.copy(); m.translation+=Vector((0,.35,.4)); body.matrix=m; update()
    changed=['Body','Hips','Abdomen','Torso','Head']
    for s in ['L','R']:
        target=targets[s].translation
        for n in ['UpperLeg.'+s,'LowerLeg.'+s]: rig.pose.bones[n].rotation_quaternion=Quaternion((1,0,0,0))
        update()
        solve('UpperLeg.'+s,'LowerLeg.'+s,target,(target.x,target.y-3,1))
        foot=rig.pose.bones['Foot.'+s]
        # These feet are separate root bones; explicitly join them to the shin.
        m=foot.bone.matrix_local.copy(); m.translation=rig.pose.bones['LowerLeg.'+s].tail
        foot.matrix=m; update()
        changed+=['UpperLeg.'+s,'LowerLeg.'+s,'Foot.'+s]
    for n in ['Shoulder.R','UpperArm.R','LowerArm.R']: rig.pose.bones[n].rotation_quaternion=Quaternion((1,0,0,0))
    update()
    solve('UpperArm.R','LowerArm.R',hand,(-1.5,-1.5,1.7))
    # A neutral wrist keeps the palm continuous with the forearm.
    rig.pose.bones['Palm.R'].rotation_quaternion=Quaternion((1,0,0,0))
    changed+=['Shoulder.R','UpperArm.R','LowerArm.R','Palm.R']
    update()
    rig.animation_data.action=act
    for name in changed:
        b=rig.pose.bones[name]
        for prop in ['location','rotation_quaternion','scale']:
            b.keyframe_insert(prop,frame=35,group=name)
    scene.frame_set(34); scene.frame_set(35); update()
    after=snapshot()
    outside=[k for k in before if k[0]!='VoltSlash01' or k[3]!=35]
    assert all(before[k]==after[k] for k in outside)
    assert set(before)==set(after)
    print('CHECK',json.dumps({'outsideFrame35KeysUnchanged':len(outside),'changedBones':changed,'ankleGaps':{s:(rig.pose.bones['Foot.'+s].head-rig.pose.bones['LowerLeg.'+s].tail).length for s in ['L','R']}}))
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE,'KnightVoltCombo_UserKeys_Frame35_Draft.blend'))
# Preview setup is in memory only, never saved to the authoring file.
scene.render.engine='BLENDER_WORKBENCH'
bpy.data.objects['Sword Preview (authoring only)'].hide_render=True
scene.display.shading.light='STUDIO'; scene.display.shading.color_type='MATERIAL'
scene.display.shading.show_shadows=True; scene.display.shading.show_cavity=True
scene.display.shading.background_type='VIEWPORT'; scene.display.shading.background_color=(.12,.12,.12)
scene.render.resolution_x=800; scene.render.resolution_y=800; scene.render.resolution_percentage=100
camdata=bpy.data.cameras.new('ReviewCamera'); cam=bpy.data.objects.new('ReviewCamera',camdata); scene.collection.objects.link(cam); scene.camera=cam
camdata.type='ORTHO'; camdata.ortho_scale=6.5
for label,pos in [('Front',(0,-12,3.2)),('Angle',(-9,-10,6))]:
    cam.location=pos; cam.rotation_euler=(Vector((0,-1.7,2))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=os.path.join(HERE,'Frame35_'+('After' if fix else 'Before')+'_'+label+'.png')
    bpy.ops.render.render(write_still=True)
