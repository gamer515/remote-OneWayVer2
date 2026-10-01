import bpy
bpy.ops.import_scene.fbx(filepath=r'C:\remote-OneWayVer2\AssetBuild\ThreeFingerHand\three-finger-hand_Free.fbx')
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
rigs = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
assert meshes and rigs, 'Missing mesh or rig'
print('FBX_VERIFIED', [(o.name, len(o.data.vertices)) for o in meshes], [(o.name, len(o.data.bones)) for o in rigs])
