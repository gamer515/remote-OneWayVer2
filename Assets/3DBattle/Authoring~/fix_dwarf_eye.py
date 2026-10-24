import bpy,os,json
from mathutils import Vector,Matrix
from mathutils.bvhtree import BVHTree
s=bpy.context.scene;o=next(o for o in s.objects if o.type=='MESH');arm=next(o for o in s.objects if o.type=='ARMATURE');ev=o.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();pts=[ev.matrix_world@v.co for v in me.vertices];tree=BVHTree.FromPolygons(pts,[list(f.vertices) for f in me.polygons]);changes=[]
adj=[[] for v in pts]
for e in o.data.edges:
 a,b=e.vertices;adj[a].append(b);adj[b].append(a)
selected={i for i,p in enumerate(pts) if .705<p.z<.845 and abs(p.x-(.055+(.825-p.z)*.20))<.026 and p.y<-.07}
relaxed=[p.copy() for p in pts]
for iteration in range(600):
 updates={i:sum((relaxed[j] for j in adj[i]),Vector())/len(adj[i]) for i in selected}
 for i,p in updates.items():relaxed[i]=p
for i in selected:
 v=o.data.vertices[i];p=relaxed[i];x,y,z=p
 center=.055+(.825-z)*.20
 hits=[tree.ray_cast(Vector((xx,-1,z)),Vector((0,1,0)))[0] for xx in [center-.03,center+.03]]
 if any(h is None for h in hits):continue
 t=max(0,min(1,(x-(center-.03))/.06));p.y=hits[0].y*(1-t)+hits[1].y*t
 delta=p-pts[i]
 skin=Matrix(((0,0,0,0),)*4);total=0
 for g in v.groups:
  name=o.vertex_groups[g.group].name
  if name in arm.pose.bones:
   pb=arm.pose.bones[name];m=arm.matrix_world@pb.matrix@pb.bone.matrix_local.inverted()@arm.matrix_world.inverted()@o.matrix_world
   skin+=m*g.weight;total+=g.weight
 if total==0:continue
 v.co+=skin.to_3x3().inverted()@delta
 changes.append((v.index,delta.length))
o.data.update();bpy.context.view_layer.update()
print('CHANGED',len(changes),'MAX',max(d for _,d in changes))
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath('Assets/3DBattle/Authoring~/DwarfEyeFixed.blend'))
exec(open('Assets/3DBattle/Authoring~/dwarf_close.py',encoding='utf-8-sig').read().replace('DwarfClose_','DwarfFixed_'))
