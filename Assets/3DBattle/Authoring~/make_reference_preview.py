from pathlib import Path
from PIL import Image, ImageDraw
import json, math

folder=Path(__file__).resolve().parent
frames=[]
for path in sorted((folder/'ReferenceFrames~').glob('*.png')):
    image=Image.open(path).convert('RGB')
    canvas=Image.new('RGB',(400,432),(11,16,22))
    canvas.paste(image,(0,32))
    draw=ImageDraw.Draw(canvas)
    draw.text((12,10),'VoltSlash01 | reference poses | 0.90s',fill=(225,233,242))
    frames.append(canvas)
durations=[20 if i%3 else 10 for i in range(len(frames))]
durations[-1]=500
frames[0].save(folder/'ReferenceSlash.gif',save_all=True,append_images=frames[1:],duration=durations,loop=0,disposal=2)
frames[0].save(folder/'ReferenceSlash_Slow.gif',save_all=True,append_images=frames[1:],duration=[50]*(len(frames)-1)+[600],loop=0,disposal=2)
checks=json.loads((folder/'reference_slash_checks.json').read_text())
def drift(side, start, end):
    ref=checks[start][side]
    return max(math.dist(ref,c[side]) for c in checks[start:end+1])
print('Planted left foot drift, frames 15-41 (Blender units):',drift('left',15,41))
print('Planted right foot drift, frames 0-16 (Blender units):',drift('right',0,16))
print('Planted right foot drift, frames 34-43 (Blender units):',drift('right',34,43))
print('Frame14 left/right forward:',-checks[14]['left'][1],-checks[14]['right'][1])
print('Frame32 left/right forward:',-checks[32]['left'][1],-checks[32]['right'][1])
print('Saved animated previews.')
