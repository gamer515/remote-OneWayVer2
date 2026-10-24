import os
from io_scene_fbx import parse_fbx
roots=[parse_fbx.parse(os.path.abspath('Assets/3DBattle/Authoring~/'+p))[0] for p in ['MeshyInspect1.fbx','MeshyRigFixed.fbx']]
def selected(root):return {n.props[0]:n for n in next(x for x in root.elems if x.id==b'Objects').elems if n.id in [b'AnimationStack',b'AnimationLayer',b'AnimationCurve',b'AnimationCurveNode',b'Geometry',b'Material',b'Texture',b'Video']}
a,b=map(selected,roots);assert a==b;print('VERIFIED: all animation, geometry, material, texture and video records preserved:',len(a))
