import bpy,os
from io_scene_fbx import parse_fbx
p=os.path.abspath('Assets/3DBattle/low-poly+dwarf+3d+model/tripo_convert_cd4b7256-ff2a-47c3-acba-5c7a590c78ee 1.fbx');root,version=parse_fbx.parse(p)
def walk(n):
 if n.id==b'Vertices':print('FBXCO',list(n.props[0][:9]))
 for c in n.elems:walk(c)
walk(root)
o=next(o for o in bpy.context.scene.objects if o.type=='MESH');print('BLENDCO',[tuple(v.co) for v in o.data.vertices[:3]]);print('ATTRS',[a.name for a in o.data.attributes])
