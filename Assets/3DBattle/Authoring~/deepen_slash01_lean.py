import bpy, os, shutil, datetime, math, json
from mathutils import Vector, Quaternion
HERE=os.path.dirname(os.path.abspath(__file__))
path=os.path.join(HERE,'KnightVoltCombo_UserKeys.blend')
backup=os.path.join(HERE,'Backups','BeforeDeeperLean_'+datetime.datetime.now().strftime('%Y%m%d_%H%M%S'))
os.makedirs(backup); shutil.copy2(path,backup)
rig=bpy.data.objects['HumanArmature']; scene=bpy.context.scene
action=bpy.data.actions['VoltSlash01']; rig.animation_data.action=action
start,END=map(round,action.frame_range)
def signature():
    return {(a.name,f.data_path,f.array_index,float(k.co.x)):float(k.co.y) for a in bpy.data.actions for l in a.layers for s in l.strips for bag in s.channelbags for f in bag.fcurves for k in f.keyframe_points}
before=signature(); poses={}; feet={}; palms={}
for f in range(start,END+1):
    scene.frame_set(f)
    poses[f]={b.name:b.matrix_basis.copy() for b in rig.pose.bones}
    feet[f]={n:rig.pose.bones[n].matrix.copy() for n in ['Foot.L','Foot.R']}
    palms[f]=rig.pose.bones['Palm.R'].matrix.copy()
def rotate(name,degrees):
    b=rig.pose.bones[name]; m=b.matrix.copy(); p=m.translation.copy()
    m=Quaternion((1,0,0),math.radians(degrees)).to_matrix().to_4x4()@m
    m.translation=p; b.matrix=m; bpy.context.view_layer.update()
changed=['Hips','Abdomen','Head','Palm.R']; max_drift=0
for f in range(start,END+1):
    scene.frame_set(f); rig.animation_data.action=None
    for b in rig.pose.bones: b.matrix_basis=poses[f][b.name]
    bpy.context.view_layer.update()
    t=(f-start)/max(1,END-start); u=max(0,min(1,(t-.18)/.64)); weight=u*u*(3-2*u)
    rotate('Hips',10*weight); rotate('Abdomen',6*weight); rotate('Head',-8*weight)
    # Keep the blade's existing orientation while its grip follows the leaning torso.
    palm=rig.pose.bones['Palm.R']; m=palms[f].copy(); m.translation=palm.head; palm.matrix=m
    bpy.context.view_layer.update()
    rig.animation_data.action=action
    for n in changed:
        b=rig.pose.bones[n]
        if b.rotation_quaternion.dot(poses[f][n].to_quaternion())<0: b.rotation_quaternion.negate()
        for prop in ['location','rotation_quaternion','scale']: b.keyframe_insert(prop,frame=f,group=n)
    for n,m in feet[f].items(): max_drift=max(max_drift,(rig.pose.bones[n].head-m.translation).length)
after=signature(); allowed={f'pose.bones["{n}"].{p}' for n in changed for p in ['location','rotation_quaternion','scale']}
assert before.keys()==after.keys()
assert all(v==after[k] for k,v in before.items() if not(k[0]=='VoltSlash01' and k[1] in allowed))
assert max_drift<.00001
scene.frame_set(END); bpy.context.view_layer.update()
sword=bpy.data.objects['Sword Preview (authoring only)']; axis=sword.matrix_world.to_3x3()@Vector((0,0,1)); axis.normalize()
print('CHECK',json.dumps({'range':[start,END],'extraLeanDegrees':16,'feetMaxDrift':max_drift,'bladeAxis':list(axis),'otherActionsAndLegKeysUnchanged':True,'backup':backup}))
bpy.context.preferences.filepaths.save_version=0; bpy.ops.wm.save_as_mainfile(filepath=path)
source=open(os.path.join(HERE,'author_user_slash01.py'),encoding='utf-8').read()
exec(source[source.index("scene.render.engine='BLENDER_WORKBENCH'"):])
