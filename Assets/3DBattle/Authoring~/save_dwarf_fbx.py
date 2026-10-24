import bpy,os,shutil,datetime,array
from io_scene_fbx import parse_fbx,encode_bin
here=os.path.abspath('Assets/3DBattle/Authoring~');src=os.path.abspath('Assets/3DBattle/low-poly+dwarf+3d+model/tripo_convert_cd4b7256-ff2a-47c3-acba-5c7a590c78ee 1.fbx')
o=next(o for o in bpy.context.scene.objects if o.type=='MESH')
with bpy.data.libraries.load(os.path.join(here,'DwarfInspect.blend')) as (a,b):b.meshes=a.meshes
orig=b.meshes[0];changed={v.index for v in o.data.vertices if v.co!=orig.vertices[v.index].co}
root,version=parse_fbx.parse(src)
def nodes(n):
 yield n
 for c in n.elems:yield from nodes(c)
vs=next(n for n in nodes(root) if n.id==b'Vertices');assert len(vs.props[0])==len(o.data.vertices)*3
for i in changed:
 for j in range(3):vs.props[0][3*i+j]=o.data.vertices[i].co[j]
# Recalculate only normals touching the edited patch, retaining all original indices.
affected=set(changed)
for f in o.data.polygons:
 if any(i in changed for i in f.vertices):affected.update(f.vertices)
o.data.attributes.remove(o.data.attributes['custom_normal']);o.data.update()
layer=next(n for n in nodes(root) if n.id==b'LayerElementNormal');ns=next(n for n in layer.elems if n.id==b'Normals');ni=next(n for n in layer.elems if n.id==b'NormalsIndex')
assert len(ni.props[0])==len(o.data.loops)
for loop in o.data.loops:
 if loop.vertex_index in affected:
  idx=ni.props[0][loop.index];normal=o.data.vertices[loop.vertex_index].normal
  for j in range(3):ns.props[0][idx*3+j]=normal[j]
methods={'C':'add_char','Y':'add_int16','I':'add_int32','L':'add_int64','F':'add_float32','D':'add_float64','R':'add_bytes','S':'add_string','i':'add_int32_array','l':'add_int64_array','f':'add_float32_array','d':'add_float64_array','b':'add_bool_array','c':'add_byte_array'}
def convert(n):
 out=encode_bin.FBXElem(n.id)
 for t,v in zip(n.props_type,n.props):getattr(out,methods[chr(t)])(v)
 out.elems=[convert(c) for c in n.elems];return out
candidate=os.path.join(here,'DwarfEyeFixed.fbx');encode_bin.write(candidate,convert(root),version)
check,_=parse_fbx.parse(candidate)
def eq(a,b):
 assert a.id==b.id and len(a.elems)==len(b.elems)
 if a.id not in [b'FileId',b'CreationTime']:
  assert a.props_type==b.props_type,(a.id,a.props_type,b.props_type)
  assert all(x==y for x,y in zip(a.props,b.props)),a.id
 for x,y in zip(a.elems,b.elems):eq(x,y)
eq(root,check)
backup=os.path.join(here,'Backups','BeforeDwarfEyeFix_'+datetime.datetime.now().strftime('%Y%m%d_%H%M%S'));os.makedirs(backup);shutil.copy2(src,backup)
print('VERIFIED nodes preserved; changed vertices',len(changed),'backup',backup)
# Import patched file afresh, verifying the actual deliverable visually.
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=candidate)
exec(open(os.path.join(here,'dwarf_close.py'),encoding='utf-8-sig').read().replace('DwarfClose_','DwarfFinal_'))



