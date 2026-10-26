import bpy,json
from mathutils import Vector
o=next(o for o in bpy.context.scene.objects if o.type=='MESH');ev=o.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();p=[ev.matrix_world@v.co for v in me.vertices];adj=[[] for v in p]
for e in o.data.edges:
 a,b=e.vertices;adj[a].append(b);adj[b].append(a)
seen=set();groups=[]
for i in range(len(p)):
 if i in seen:continue
 q=[i];seen.add(i);g=[]
 while q:
  k=q.pop();g.append(k)
  for j in adj[k]:
   if j not in seen:seen.add(j);q.append(j)
 groups.append(g)
print('COMPONENTS',len(groups))
for g in sorted(groups,key=len,reverse=True):
 print(len(g),[(round(min(p[k][i] for k in g),4),round(max(p[k][i] for k in g),4)) for i in range(3)])
