exec(open('Assets/3DBattle/Authoring~/dwarf_fbx_inspect.py',encoding='utf-8-sig').read())
def see(n):
 if n.id==b'LayerElementNormal':
  print('NORMALMETA',[(c.id, str(c.props)[:180]) for c in n.elems])
 for c in n.elems:see(c)
see(root)
