import os
from io_scene_fbx import parse_fbx
r,v=parse_fbx.parse(os.path.abspath('Assets/3DBattle/Authoring~/MeshyInspect1.fbx'));obs=next(n for n in r.elems if n.id==b'Objects');nodes={n.props[0]:n for n in obs.elems};cons=next(n for n in r.elems if n.id==b'Connections').elems;uid=next(i for i,n in nodes.items() if n.id==b'Model' and n.props[1].startswith(b'target_character'))
for c in cons:
 if len(c.props)>3 and c.props[2]==uid:
  print('TARGET',c.props)
  cn=c.props[1]
  for cc in cons:
   if cc.props[2]==cn and nodes.get(cc.props[1]) and nodes[cc.props[1]].id==b'AnimationCurve':
    vs=next(n.props[0] for n in nodes[cc.props[1]].elems if n.id==b'KeyValueFloat');print(cc.props[3],min(vs),max(vs))
