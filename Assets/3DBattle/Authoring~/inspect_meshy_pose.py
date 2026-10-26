import bpy,os
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=os.path.abspath('Assets/3DBattle/Authoring~/MeshyInspect1.fbx'));bpy.context.view_layer.update()
a=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
for f in [0,1,10,30]:
 bpy.context.scene.frame_set(f)
 print('FRAME',f)
 for b in list(a.pose.bones)[:4]:print(b.name,'scale',list(b.scale),'loc',list(b.location),'restlength',b.bone.length)
print('ACTIONS',len(bpy.data.actions))
