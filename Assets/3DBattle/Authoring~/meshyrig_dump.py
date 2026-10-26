import os
from io_scene_fbx import parse_fbx
r,v=parse_fbx.parse(os.path.abspath('Assets/3DBattle/Authoring~/MeshyInspect1.fbx'))
objs=next(n for n in r.elems if n.id==b'Objects')
for n in objs.elems:
 if n.id==b'Model':
  p=next((x for x in n.elems if x.id==b'Properties70'),None)
  print('MODEL',n.props[:3],[(x.props[0],x.props[4:]) for x in p.elems if x.props[0] in [b'Lcl Translation',b'Lcl Rotation',b'Lcl Scaling',b'PreRotation',b'PostRotation']] if p else [])
 if n.id==b'Deformer' and n.props[2]==b'Cluster' and 'Hips' in str(n.props):
  print('CLUSTER',n.props,[(c.id,list(c.props[0]) if c.id in [b'Transform',b'TransformLink'] else '') for c in n.elems])
 if n.id==b'AnimationCurveNode':print('CURVENODE',n.props[:3])
