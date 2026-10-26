exec(open('Assets/3DBattle/Authoring~/meshyrig_detail.py',encoding='utf-8-sig').read().split('for b in list')[0])
o=next(o for o in bpy.context.scene.objects if o.type=='MESH');root=a.data.bones['mixamorig:Hips'].head_local
for b in a.data.bones:
 if b.name in ['mixamorig:Hips','mixamorig:Head','mixamorig:LeftFoot','mixamorig:RightFoot','mixamorig:LeftHand','mixamorig:RightHand','mixamorig:Spine']:
  print(b.name,'relative',list((b.head_local-root)*100),'restmat',b.matrix_local.to_quaternion()[:]);g=o.vertex_groups.get(b.name);vs=[(o.matrix_world@v.co,k.weight) for v in o.data.vertices for k in v.groups if g and k.group==g.index];print('CENTER',list(sum((p*w for p,w in vs),__import__('mathutils').Vector())/sum(w for p,w in vs)))
