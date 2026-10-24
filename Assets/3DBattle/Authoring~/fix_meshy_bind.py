import bpy,os,array
from mathutils import Matrix,Vector
from io_scene_fbx import parse_fbx,encode_bin
p=os.path.abspath('Assets/3DBattle/Authoring~/MeshyInspect1.fbx');bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=p);bpy.context.view_layer.update();arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE');rootpos=arm.data.bones['mixamorig:Hips'].head_local.copy()
r,version=parse_fbx.parse(p);obs=next(n for n in r.elems if n.id==b'Objects')
def mat(vals):return Matrix([vals[i:i+4] for i in range(0,16,4)]).transposed()
def vals(m):return array.array('d',[m[row][col] for col in range(4) for row in range(4)])
axis=Matrix(((1,0,0,0),(0,0,1,0),(0,-1,0,0),(0,0,0,1)))
newlinks={}
for n in obs.elems:
 if n.id!=b'Deformer' or n.props[2]!=b'Cluster':continue
 name=n.props[1].split(b'\0')[0].decode();b=arm.data.bones.get(name)
 if b is None:continue
 oldlink=next(x for x in n.elems if x.id==b'TransformLink');oldtransform=next(x for x in n.elems if x.id==b'Transform');meshworld=mat(oldlink.props[0])@mat(oldtransform.props[0])
 desired=b.matrix_local.to_quaternion().to_matrix().to_4x4();desired.translation=rootpos+(b.head_local-rootpos)*100
 link=axis@desired
 for i in range(3):
  for j in range(3):link[i][j]*=10000
 link.translation*=100
 oldlink.props[0]=vals(link);oldtransform.props[0]=vals(link.inverted()@meshworld);newlinks[name]=link
# Make the static imported pose match the repaired bind pose. Animation curves stay intact.
import math
models={n.props[0]:n for n in obs.elems if n.id==b'Model'}
connections=next(n for n in r.elems if n.id==b'Connections')
parents={c.props[1]:c.props[2] for c in connections.elems if c.props[0]==b'OO' and c.props[1] in models}
byid={uid:newlinks[n.props[1].split(b'\0')[0].decode()] for uid,n in models.items() if n.props[1].split(b'\0')[0].decode() in newlinks}
rawarm=axis@arm.matrix_world
for row in range(3):
 for col in range(4):rawarm[row][col]*=100
for uid,link in byid.items():
 parent=byid.get(parents.get(uid),rawarm)
 local=parent.inverted()@link;loc,rot,scale=local.decompose()
 properties=next(n for n in models[uid].elems if n.id==b'Properties70')
 replacements={b'Lcl Translation':list(loc),b'Lcl Rotation':[math.degrees(v) for v in rot.to_euler('XYZ')],b'Lcl Scaling':list(scale)}
 for prop in properties.elems:
  if prop.props[0] in replacements:prop.props[4:]=replacements[prop.props[0]]
for pose in [n for n in obs.elems if n.id==b'Pose']:
 for node in [n for n in pose.elems if n.id==b'PoseNode']:
  uid=next(n.props[0] for n in node.elems if n.id==b'Node')
  if uid in byid:next(n for n in node.elems if n.id==b'Matrix').props[0]=vals(byid[uid])
methods={'B':'add_bool','C':'add_char','Y':'add_int16','I':'add_int32','L':'add_int64','F':'add_float32','D':'add_float64','R':'add_bytes','S':'add_string','i':'add_int32_array','l':'add_int64_array','f':'add_float32_array','d':'add_float64_array','b':'add_bool_array','c':'add_byte_array'}
def convert(n):
 out=encode_bin.FBXElem(n.id)
 for t,v in zip(n.props_type,n.props):getattr(out,methods[chr(t)])(v)
 out.elems=[convert(c) for c in n.elems];return out
encode_bin.write(os.path.abspath('Assets/3DBattle/Authoring~/MeshyRigFixed.fbx'),convert(r),version)
print('FIXED LINKS',len(newlinks))
