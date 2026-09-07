from pathlib import Path
import struct,sys,subprocess
import numpy as np
from PIL import Image,ImageDraw,ImageFont
from build_head_v2 import ROOT,NAMES,normals,linear
HERE=Path(__file__).parent
z=np.load(ROOT/'Source/head_geometry.npz')
v,t,m,c,d=[z[n] for n in ['vertices','triangles','materials','colors','deltas']]
SETTINGS=[('NEUTRAL',{},0),('THREE QUARTER',{},-.53),('SMILE',{'Smile':1},0),('ANGRY',{'Angry':1},0),('SURPRISED',{'Surprised':1},0),('EYES CLOSED',{'BlinkLeft':1,'BlinkRight':1},0)]
quick='--quick' in sys.argv
selected=SETTINGS[:2] if quick else SETTINGS
images=[]
for name,weights,yaw in selected:
    vv=v.copy()
    for key,w in weights.items():vv+=d[NAMES.index(key)]*w
    n=normals(vv,t)
    fn=HERE/(name.lower().replace(' ','_')+'.bin')
    fn.write_bytes(struct.pack('<II',len(v),len(t))+np.column_stack((vv,n,linear(c))).astype('<f4').tobytes()+np.column_stack((t,m)).astype('<u4').tobytes())
    ppm=fn.with_suffix('.ppm');size=(540,610) if quick else (860,960)
    subprocess.run([str(HERE/'ray_render'),str(fn),str(ppm),str(size[0]),str(size[1]),str(yaw),'0' if quick else '1'],check=True)
    im=Image.open(ppm).convert('RGB');im.save(HERE/(name.lower().replace(' ','_')+'.png'))
    images.append((name,im.resize((440,490),Image.Resampling.LANCZOS)))
if quick:
    out=Image.new('RGB',(880,490));out.paste(images[0][1],(0,0));out.paste(images[1][1],(440,0));out.save(HERE/'quick.png')
else:
    out=Image.new('RGB',(1368,1142),'#f2efe9');draw=ImageDraw.Draw(out)
    font='/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'
    title=ImageFont.truetype(font,29);small=ImageFont.truetype(font,12);label=ImageFont.truetype(font,13)
    draw.text((27,18),'AANG FACE / REVISED 3D MODEL',font=title,fill='#354956')
    draw.text((29,60),'Actual mesh render  |  Spherical eyes, sculpted lids and lips  |  10 morph targets',font=small,fill='#7e898e')
    for i,(name,im) in enumerate(images):
        x=12+(i%3)*452;y=88+(i//3)*518;out.paste(im,(x,y));draw.text((x+13,y+492),name,font=label,fill='#586b76')
    draw.text((27,1120),'Geometry and facial colors are included in the asset. Appearance in Unity depends on scene lighting.',font=small,fill='#7d878b')
    out.save(ROOT/'ActualModelPreview.png')
print('Render complete.',flush=True)
