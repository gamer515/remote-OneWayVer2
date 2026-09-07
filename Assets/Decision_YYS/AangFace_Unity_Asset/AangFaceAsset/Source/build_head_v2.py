"""Anatomical stylized head v2, with open facial topology and ten morphs.
Pure geometry: no image-to-3D reconstruction or reference-image editing.
Requires numpy and scipy. Coordinates are RH, Y up, +Z forward, in meters.
"""
from pathlib import Path
import math,json,struct
import numpy as np
from scipy.interpolate import PchipInterpolator
from scipy.spatial import Delaunay

ROOT=Path(__file__).resolve().parents[1]
PI=math.pi
NAMES=['Smile','Angry','Sad','Surprised','BlinkLeft','BlinkRight','JawOpen','MouthWide','MouthPucker','BrowRaise']
MATERIALS=[('Skin','#f5caa2',.63),('Arrow','#83b8dc',.69),('Brows','#483226',.80),
 ('Sclera','#fcf9f0',.20),('Iris','#74848d',.32),('Pupil','#10141a',.14),('Glint','#ffffff',.08),
 ('Mouth','#4c2225',.68),('Teeth','#fff4dc',.38),('Tongue','#dc9391',.56),('Nostril','#86533c',.75)]
def rgb(h):return np.array([int(h[i:i+2],16)/255 for i in (1,3,5)],float)
BASECOLORS=[rgb(c) for _,c,_ in MATERIALS]
Y=np.array([-1.06,-1.01,-.92,-.80,-.65,-.45,-.20,.05,.30,.57,.80,1.0,1.15,1.24])
WIDTH=PchipInterpolator(Y,[.003,.15,.31,.425,.555,.67,.746,.78,.786,.76,.699,.57,.365,.003])
FRONT=PchipInterpolator(Y,[.29,.42,.53,.595,.623,.61,.59,.60,.61,.585,.52,.38,.20,-.045])
BACK=PchipInterpolator(Y,[.29,.12,-.07,-.23,-.35,-.51,-.68,-.75,-.775,-.735,-.60,-.41,-.25,-.045])
def width(y):return float(WIDTH(np.clip(y,Y[0],Y[-1])))
def fz(x,y):
    yy=np.clip(y,Y[0],Y[-1]);f=float(FRONT(yy));b=float(BACK(yy));w=width(yy)
    ratio=min(1.,abs(x)/max(w,1e-5));middle=(f+b)*.5
    zbase=middle+(f-middle)*max(0.,1-ratio*ratio)**.38
    z=zbase
    # Rounded cheek fat, recessed sockets, a nasal bridge, tip, alae and philtrum.
    for s in [-1,1]:
        z+=.064*math.exp(-((x-s*.44)/.24)**2-((y+.30)/.255)**2)
        z-=.082*math.exp(-((x-s*.38)/.205)**2-((y-EYE_Y)/.18)**2)
        z+=.067*math.exp(-((x-s*.109)/.057)**2-((y+.359)/.070)**2)
        z-=.031*math.exp(-((x-s*.105)/.031)**2-((y+.400)/.023)**2)
        z+=.009*math.exp(-((x-s*.035)/.017)**2-((y+.51)/.073)**2)
    z+=.073*math.exp(-(x/.101)**2-((y+.095)/.305)**2)
    z+=.157*math.exp(-(x/.132)**2-((y+.336)/.119)**2)
    z+=.025*math.exp(-(x/.23)**2-((y+.85)/.105)**2)
    z-=.005*math.exp(-(x/.024)**2-((y+.50)/.070)**2)
    return zbase+(z-zbase)*max(0.,1-ratio**6)

def skin_color(x,y,z):
    c=BASECOLORS[0].copy()
    cheek=math.exp(-((abs(x)-.46)/.20)**2-((y+.30)/.21)**2)*.17
    nose=math.exp(-(x/.19)**2-((y+.37)/.13)**2)*.12
    return c*(1-cheek-nose)+rgb('#eca58e')*(cheek+nose)

class Geo:
    def __init__(self,e=None):self.e=e or {};self.v=[];self.t=[];self.m=[];self.c=[]
    def val(self,k):return self.e.get(k,0.)
    def deform(self,p):
        x,y,z=p
        front=max(0,min(1,(z-.02)/.4))
        ja=self.val('JawOpen')+.48*self.val('Surprised')+.22*self.val('Smile')
        y-=.090*ja*max(0,min(1,(-y-.46)/.42))*math.exp(-(x/.68)**4)*front
        lift=.025*self.val('Smile')*math.exp(-((abs(x)-.43)/.25)**2-((y+.27)/.22)**2)*front
        y+=lift;z+=lift*.24
        return [x*.12,y*.12,z*.12]
    def add(self,p,mat=0,color=None):
        p=np.asarray(p,float)
        if color is None:color=skin_color(*p) if mat==0 else BASECOLORS[mat]
        self.v.append(self.deform(p));self.c.append(np.clip(color,0,1))
        return len(self.v)-1
    def tri(self,a,b,c,mat):self.t.append((a,b,c));self.m.append(mat)
    def grid(self,nu,nv,fn,mat=0,wrap=False,flip=False,cf=None):
        start=len(self.v);stride=nu if wrap else nu+1
        for j in range(nv+1):
            for i in range(stride):
                u=i/nu;v=j/nv;p=fn(u,v)
                self.add(p,mat,None if cf is None else cf(u,v,p))
        for j in range(nv):
            for i in range(nu):
                k=(i+1)%stride;a=start+j*stride+i;b=start+j*stride+k;c=start+(j+1)*stride+k;d=start+(j+1)*stride+i
                if flip:self.tri(a,c,b,mat);self.tri(a,d,c,mat)
                else:self.tri(a,b,c,mat);self.tri(a,c,d,mat)
    def disk(self,fn,mat=0,n=80,rings=12,cf=None):
        p=fn(0,0);center=self.add(p,mat,None if cf is None else cf(0,0,p));last=None
        for j in range(1,rings+1):
            ids=[]
            for i in range(n):
                r=j/rings;a=i/n*2*PI;p=fn(r,a)
                ids.append(self.add(p,mat,None if cf is None else cf(r,a,p)))
            for i in range(n):
                k=(i+1)%n
                if last is None:self.tri(center,ids[i],ids[k],mat)
                else:self.tri(last[i],ids[i],ids[k],mat);self.tri(last[i],ids[k],last[k],mat)
            last=ids
    def ellipsoid(self,center,scale,mat=0,nu=48,nv=30):
        def f(u,v):
            a=2*PI*u;b=PI*(.0001+.9998*v)
            return np.array(center)+np.array(scale)*[math.sin(b)*math.sin(a),math.cos(b),math.sin(b)*math.cos(a)]
        self.grid(nu,nv,f,mat,wrap=True,flip=True)
    def tube(self,points,radii,mat=0,sides=10,depth=1.):
        points=np.array(points);start=len(self.v)
        for j,p in enumerate(points):
            tangent=points[min(j+1,len(points)-1)]-points[max(0,j-1)];tangent/=np.linalg.norm(tangent)
            u=np.cross(tangent,[0,0,1]);u/=np.linalg.norm(u);v=np.cross(tangent,u)
            for i in range(sides):
                a=i/sides*2*PI;self.add(p+radii[j]*(u*math.cos(a)+v*math.sin(a)*depth),mat)
        for j in range(len(points)-1):
            for i in range(sides):
                k=(i+1)%sides;a=start+j*sides+i;b=start+j*sides+k;c=start+(j+1)*sides+k;d=start+(j+1)*sides+i
                self.tri(a,b,c,mat);self.tri(a,c,d,mat)

EYE_X=.378;EYE_Y=-.030;EYE_Z=.390;EYE_R=.248
EYE_OUT_X=.285;EYE_OUT_Y=.253
MOUTH_Y=-.665;MOUTH_OUT_X=.427;MOUTH_OUT_Y=.248

def eye_outer(s,a):return s*EYE_X+EYE_OUT_X*math.cos(a),EYE_Y+EYE_OUT_Y*math.sin(a)
def mouth_outer(a):return MOUTH_OUT_X*math.cos(a),MOUTH_Y+MOUTH_OUT_Y*math.sin(a)
def eye_z(x,y,s):return EYE_Z+math.sqrt(max(.00005,EYE_R**2-(x-s*EYE_X)**2-(y-EYE_Y)**2))
def excluded(x,y):
    for s in [-1,1]:
        if ((x-s*EYE_X)/EYE_OUT_X)**2+((y-EYE_Y)/EYE_OUT_Y)**2<.999999:return True
    return (x/MOUTH_OUT_X)**2+((y-MOUTH_Y)/MOUTH_OUT_Y)**2<.999999

def face_domain():
    points=[]
    for y in np.arange(-1.054,1.238,.026):
        for x in np.arange(-.795,.796,.026):
            if abs(x)<width(y)-.005 and not excluded(x,y):points.append((x,y))
    for y in np.arange(-.55,.38,.012):
        for x in np.arange(-.22,.225,.012):
            if not excluded(x,y):points.append((x,y))
    for y in np.linspace(-1.06,1.24,180):
        for s in [-1,1]:points.append((s*width(y),y))
    for s in [-1,1]:points += [eye_outer(s,a) for a in np.linspace(0,2*PI,128,endpoint=False)]
    points += [mouth_outer(a) for a in np.linspace(0,2*PI,160,endpoint=False)]
    xy=np.unique(np.round(points,9),axis=0);dt=Delaunay(xy)
    tri=[]
    for ids in dt.simplices:
        x,y=xy[ids].mean(axis=0)
        if abs(x)<=width(y)+1e-5 and not excluded(x,y):tri.append(ids)
    return xy,np.array(tri)

FACE_XY,FACE_T=face_domain()

def build(e=None):
    g=Geo(e);sm,an,sa,su=[g.val(k) for k in NAMES[:4]]
    jaw,wide,pucker,br=[g.val(k) for k in NAMES[6:]]
    # Actual openings in the head mesh. Eyeballs and the oral cavity sit behind them.
    for x,y in FACE_XY:g.add((x,y,fz(x,y)))
    for a,b,c in FACE_T:g.tri(int(a),int(b),int(c),0)
    def back(u,v):
        y=-1.06+2.30*v;a=PI*u;w=width(y);f=float(FRONT(y));b=float(BACK(y));zc=(f+b)*.5
        return -w*math.cos(a),y,zc-(zc-b)*math.sin(a)
    g.grid(68,179,back,0,flip=True)
    # Neck with a soft flare at the cut base, naturally tucked behind the chin.
    def neck(u,v):
        a=u*2*PI;y=-1.365+.65*v;rad=.265+.10*(1-v)**4-.020*math.sin(v*PI)
        return rad*math.sin(a),y,-.195+.88*rad*math.cos(a)
    g.grid(80,24,neck,0,wrap=True)
    g.disk(lambda r,a:(.365*r*math.cos(a),-1.365,-.195+.3212*r*math.sin(a)),0,n=80,rings=5)
    # Cartilage shells: a raised helix and a recessed concha, with a separate back shell.
    for s in [-1,1]:
        g.ellipsoid((s*.807,-.17,-.01),(.182,.280,.090),0,56,38)
        def ear(r,a):
            a=a if s>0 else -a
            x=s*(.848+.158*r*math.cos(a)-.016*r*math.sin(a))
            y=-.166+.269*r*math.sin(a)
            z=.025+.055*(1-r*r)+.103*math.exp(-((r-.80)/.145)**2)-.040*math.exp(-((r-.25)/.23)**2)
            return x,y,z
        def earcol(r,a,p):
            blend=.20*math.exp(-((r-.35)/.28)**2)
            return BASECOLORS[0]*(1-blend)+rgb('#c88463')*blend
        g.disk(ear,0,n=88,rings=24,cf=earcol)
        pts=[]
        for a in np.linspace(-1.27,1.85,48):
            pts.append((s*(.843+.066*math.cos(a)),-.175+.163*math.sin(a),.105+.016*math.cos(a)))
        g.tube(pts,[.011+.005*math.sin(i/(len(pts)-1)*PI) for i in range(len(pts))],0,10,.72)
        g.ellipsoid((s*.782,-.242,.107),(.032,.047,.032),0,30,24)
    # Forehead arrow sits just above the skin surface.
    def tip(u,v):
        y=.220+.462*v;x=(2*u-1)*(.00002+.398*v)
        return x,y,fz(x,y)+.0021
    g.grid(36,42,tip,1)
    # Trace the band over the crown in cylindrical longitude sections.
    ytop=1.24
    def band(u,v):
        x=(u*2-1)*.176;a=.480+(2.95-.480)*v
        cap_ys=np.linspace(1.24,.57,900)
        maxy=float(np.interp(abs(x),WIDTH(cap_ys),cap_ys))
        y=.18+(maxy-.18)*math.sin(a)
        if math.cos(a)>=0:z=fz(x,y)+.0023
        else:
            w=width(y);f=float(FRONT(y));b=float(BACK(y));zc=(f+b)*.5
            z=zc-(zc-b)*math.sqrt(max(.000001,1-(x/w)**2))-.0023
        return x,y+.0005,z
    g.grid(26,160,band,1)
    # Spherical eyes, iris detail and morphing upper/lower eyelid surfaces.
    for s in [-1,1]:
        blink=g.val('BlinkLeft' if s>0 else 'BlinkRight')
        # A hidden depth corrective keeps the rigid-looking visible eye from piercing
        # the lid during a linearly interpolated blink (no XY squash).
        eye_depth_scale=1-.68*blink
        def surface_eye(x,y):return EYE_Z-.040*blink+(eye_z(x,y,s)-EYE_Z)*eye_depth_scale
        g.ellipsoid((s*EYE_X,EYE_Y,EYE_Z-.040*blink),(EYE_R,EYE_R,EYE_R*eye_depth_scale),3,72,46)
        def inner(a):
            u=math.cos(a);sin=math.sin(a);x=s*EYE_X+.227*u
            amp=.198 if sin>=0 else .164
            y=EYE_Y+amp*sin+.012*s*u
            # Upper lids express emotion without changing the size of the eyeball.
            if sin>0:y-=an*.060*(1-s*u)*sin;y+=su*.022*sin;y-=sm*.025*sin
            else:y+=sm*.037*(-sin);y-=su*.012*(-sin)
            closed=EYE_Y-.046+.020*u*u
            y=y*(1-blink)+closed*blink
            z=(eye_z(x,y,s)+.006)*(1-blink)+(fz(x,y)+.015)*blink
            return x,y,z
        def eyelid(u,v):
            a=2*PI*u;x0,y0,z0=inner(a);x1,y1=eye_outer(s,a)
            t=v*v*(3-2*v)
            x=x0*(1-t)+x1*t;y=y0*(1-t)+y1*t
            z=fz(x,y)+(z0-fz(x0,y0))*(1-v)**3
            z+=(.007 if math.sin(a)>0 else .003)*math.sin(PI*v)**2
            # Keep covering skin outside the rigid eyeball as the aperture closes.
            sphere_sq=EYE_R**2-(x-s*EYE_X)**2-(y-EYE_Y)**2
            if sphere_sq>0:
                cover=EYE_Z-.040*blink+math.sqrt(sphere_sq)*eye_depth_scale+.010
                z=max(z,cover)
            return x,y,z
        def lidcol(u,v,p):
            tint=.24*math.exp(-(v/.14)**2)
            return skin_color(*p)*(1-tint)+rgb('#dba080')*tint
        g.grid(128,15,eyelid,0,wrap=True,flip=True,cf=lidcol)
        # A fine, tapered lash line along the upper lid.
        angles=np.linspace(.015,PI-.015,66);pts=[inner(a) for a in angles]
        g.tube([(x,y,z+.002) for x,y,z in pts],[.003+.005*math.sin(PI*i/(len(pts)-1))**.5 for i in range(len(pts))],2,8,.62)
        # Iris is a curved cap on the sphere; color striae are real vertex colors.
        def iris(r,a):
            x=s*EYE_X+.146*r*math.cos(a);y=EYE_Y+.032+.146*r*math.sin(a)
            return x,y,surface_eye(x,y)+.0013
        def iriscol(r,a,p):
            if r>.91:return rgb('#283740')*(.90+.1*math.sin(a*31))
            spokes=.83+.10*math.sin(a*61+8*r)+.07*math.sin(a*97-13*r)
            radial=.83+.17*math.sin((r-.35)*PI*1.1)
            return rgb('#829098')*spokes*radial
        g.disk(iris,4,n=128,rings=16,cf=iriscol)
        g.disk(lambda r,a:(s*EYE_X+.079*r*math.cos(a),EYE_Y+.032+.079*r*math.sin(a),surface_eye(s*EYE_X+.079*r*math.cos(a),EYE_Y+.032+.079*r*math.sin(a))+.0020),5,n=80,rings=5)
        for dx,dy,rr in [(-.034,.050,.019),(.040,-.034,.007)]:
            g.disk(lambda r,a,dx=dx,dy=dy,rr=rr:(s*EYE_X+dx+rr*r*math.cos(a),EYE_Y+dy+rr*r*math.sin(a),surface_eye(s*EYE_X+dx+rr*r*math.cos(a),EYE_Y+dy+rr*r*math.sin(a))+.003),6,n=32,rings=3)
        # Sculpted, tapered brows with subtle strand coloration.
        def brow(u,v):
            x=s*(.142+.452*u)
            y=.312+.079*math.sin(PI*u)-.035*u
            y+=an*(-.113*(1-u)+.033*u)+sa*(.134*(1-u)-.031*u)+su*.108+br*.109
            thick=.010+.032*math.sin(PI*u)**.6
            y+=(2*v-1)*thick
            return x,y,fz(x,y)+.005+.014*math.sin(PI*v)*math.sin(PI*u)**.4
        def browcol(u,v,p):return rgb('#473125')*(.82+.11*math.sin(v*19+u*32)+.07*math.sin(v*37-u*16))
        g.grid(72,10,brow,2,flip=(s<0),cf=browcol)
    # Recessed nostril hollows under a sculpted rounded nose tip.
    for s in [-1,1]:
        def nostril(r,a):
            x=s*.103+.024*r*math.cos(a);y=-.402+.012*r*math.sin(a)
            return x,y,fz(x,y)+.001
        g.disk(nostril,10,n=40,rings=5)
    # Lips form the boundary of a genuine opening into the mouth cavity.
    mw=.290+.044*sm+.072*wide-.112*pucker-.128*su
    mh=.004+.112*sm+.203*jaw+.202*su+.026*pucker
    cy=-.650-.016*sm-.024*jaw-.006*su
    curve=.044+.072*sm-.075*sa-.036*an
    def mouth_inner(a):
        u=math.cos(a);si=math.sin(a);x=mw*u
        y=cy+curve*u*u+mh*si
        if si>0:y+=.006*math.exp(-(u/.19)**2)
        z=fz(x,y)+.010+.060*pucker*(1-u*u)
        return x,y,z
    def lips(u,v):
        a=2*PI*u;x0,y0,z0=mouth_inner(a);x1,y1=mouth_outer(a)
        t=v*v*(3-2*v);x=x0*(1-t)+x1*t;y=y0*(1-t)+y1*t
        volume=(.018 if math.sin(a)>0 else .026)*math.exp(-((v-.15)/.15)**2)*(1-v)
        z=fz(x,y)+(z0-fz(x0,y0))*(1-v)**3+volume
        return x,y,z
    def lipcol(u,v,p):
        # A soft tint concentrated at the vermilion, fading into surrounding skin.
        tint=.43*math.exp(-((v-.12)/.14)**2)
        return skin_color(*p)*(1-tint)+rgb('#d99679')*tint
    g.grid(160,18,lips,0,wrap=True,flip=True,cf=lipcol)
    def cavity(r,a):
        x,y,z=mouth_inner(a)
        return x*r,cy+(y-cy)*r,z-.018-.245*(1-r)**.72
    g.disk(cavity,7,n=160,rings=16)
    # Individual rounded tooth crowns, attached to an upper dental arch.
    for i in range(8):
        x=(i-3.5)*.064;u=x/.29
        y=-.594+.029*u*u+.011*sm+.100*su+.100*jaw
        z=fz(x,-.62)-.039-.025*u*u
        # Rounded rectangular crowns, rather than ellipsoid beads.
        def tooth(u,v,x=x,y=y,z=z):
            a=2*PI*u;b=PI*(.0001+.9998*v)
            sx=math.sin(b)*math.sin(a);sy=math.cos(b);sz=math.sin(b)*math.cos(a)
            return (x+.0325*math.copysign(abs(sx)**.42,sx),
                    y+.039*math.copysign(abs(sy)**.48,sy),
                    z+.021*math.copysign(abs(sz)**.42,sz))
        g.grid(24,20,tooth,8,wrap=True,flip=True)
    g.ellipsoid((0,cy-mh*.68,fz(0,cy)-.093),(.13,max(.014,mh*.19),.036),9,48,26)
    return np.array(g.v,np.float32),np.array(g.t,np.uint32),np.array(g.m,np.uint16),np.array(g.c,np.float32)

def normals(v,t):
    v=np.asarray(v,np.float64);n=np.zeros_like(v);faces=np.cross(v[t[:,1]]-v[t[:,0]],v[t[:,2]]-v[t[:,0]])
    for i in range(3):np.add.at(n,t[:,i],faces)
    # Average coincident seam normals without changing vertex order or morph correspondence.
    _,inverse=np.unique(np.round(v,7),axis=0,return_inverse=True)
    summed=np.zeros((inverse.max()+1,3));np.add.at(summed,inverse,n);n=summed[inverse]
    size=np.linalg.norm(n,axis=1);bad=size<1e-14;n[bad]=[0,0,1];size[bad]=1
    return (n/size[:,None]).astype(np.float32)

def linear(a):
    a=np.asarray(a);return np.where(a<=.04045,a/12.92,((a+.055)/1.055)**2.4)

def export():
    for p in ['Assets/AangFace/Data','Source']: (ROOT/p).mkdir(exist_ok=True,parents=True)
    v,t,m,c=build();n=normals(v,t);deltas=[];ndeltas=[];shapes=[]
    for name in NAMES:
        vv,tt,mm,cc=build({name:1.});assert np.array_equal(t,tt) and np.array_equal(m,mm)
        d=vv-v;dn=normals(vv,t)-n;idx=np.flatnonzero((abs(d).max(axis=1)>1e-8)|(abs(dn).max(axis=1)>1e-6))
        shapes.append({'name':name,'indices':idx.tolist(),'delta':np.round(d[idx].astype(float).ravel(),7).tolist(),'normalDelta':np.round(dn[idx].astype(float).ravel(),6).tolist()})
        deltas.append(d);ndeltas.append(dn)
        print('Morph:',name,flush=True)
    mats=[{'name':name,'color':rgb(color).tolist()+[1.],'roughness':rough} for name,color,rough in MATERIALS]
    data={'name':'AangFace','version':2,'coordinateSystem':'RH Y-up face +Z','meters':True,
          'positions':np.round(v.astype(float).ravel(),7).tolist(),'normals':np.round(n.astype(float).ravel(),6).tolist(),
          'colors':np.round(c.astype(float).ravel(),5).tolist(),'materials':mats,
          'submeshes':[{'triangles':t[m==i].ravel().tolist()} for i in range(len(mats))],'blendShapes':shapes}
    (ROOT/'Assets/AangFace/Data/AangFaceMesh.json').write_text(json.dumps(data,separators=(',',':')))
    np.savez_compressed(ROOT/'Source/head_geometry.npz',vertices=v,triangles=t,materials=m,colors=c,normals=n,deltas=deltas,normal_deltas=ndeltas)
    binary=bytearray();views=[];access=[]
    def acc(a,kind='VEC3',ctype=5126,target=34962):
        while len(binary)%4:binary.append(0)
        offset=len(binary);raw=a.tobytes();binary.extend(raw)
        views.append({'buffer':0,'byteOffset':offset,'byteLength':len(raw),'target':target})
        item={'bufferView':len(views)-1,'componentType':ctype,'count':len(a),'type':kind}
        if kind=='VEC3':item.update(min=a.min(axis=0).tolist(),max=a.max(axis=0).tolist())
        access.append(item);return len(access)-1
    positions=acc(v);norm=acc(n);color=acc(linear(c).astype('<f4'))
    targets=[{'POSITION':acc(d),'NORMAL':acc(dn)} for d,dn in zip(deltas,ndeltas)]
    primitives=[]
    for i in range(len(mats)):
        index=acc(t[m==i].ravel().astype('<u4'),'SCALAR',5125,34963)
        primitives.append({'attributes':{'POSITION':positions,'NORMAL':norm,'COLOR_0':color},'indices':index,'material':i,'targets':targets})
    gltf={'asset':{'version':'2.0','generator':'AangFace v2 anatomical parametric mesh'},'scene':0,'scenes':[{'nodes':[0]}],
          'nodes':[{'name':'AangFace','mesh':0}],
          'meshes':[{'name':'AangFace','primitives':primitives,'weights':[0.]*len(NAMES),'extras':{'targetNames':NAMES}}],
          'materials':[{'name':ma['name'],'pbrMetallicRoughness':{'baseColorFactor':[1.,1.,1.,1.],'metallicFactor':0.,'roughnessFactor':ma['roughness']},'doubleSided':False} for ma in mats],
          'bufferViews':views,'accessors':access,'buffers':[{'byteLength':len(binary)}]}
    js=json.dumps(gltf,separators=(',',':')).encode();js+=b' '*((-len(js))%4);binary+=b'\0'*((-len(binary))%4)
    out=struct.pack('<4sII',b'glTF',2,28+len(js)+len(binary))+struct.pack('<I4s',len(js),b'JSON')+js+struct.pack('<I4s',len(binary),b'BIN\0')+binary
    (ROOT/'AangFace.glb').write_bytes(out)
    stats={'version':2,'vertices':len(v),'triangles':len(t),'materials':len(mats),'blendShapes':NAMES,
      'heightMeters':float(np.ptp(v[:,1])),'boundsMeters':{'min':v.min(axis=0).tolist(),'max':v.max(axis=0).tolist()},
      'improvements':['Reshaped chin and cranial profile','Actual eye apertures with spherical eyeballs and covering lids','Sculpted lips, mouth cavity and individual teeth','Detailed irises and soft vertex color transitions','Recessed ear cartilage'],
      'limitations':['Stylized hand-constructed approximation, not exact image reconstruction','Partial overlapping anatomy; no full production retopology','Unity Editor execution and C# compilation unavailable']}
    (ROOT/'Source/validation.json').write_text(json.dumps(stats,indent=2));print(json.dumps(stats,indent=2),flush=True)
    return data

if __name__=='__main__':export()
