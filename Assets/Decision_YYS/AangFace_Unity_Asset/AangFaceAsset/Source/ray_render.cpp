#include <algorithm>
#include <cmath>
#include <cstdint>
#include <fstream>
#include <iostream>
#include <numeric>
#include <vector>
#include <limits>
#include <omp.h>
using namespace std;
struct V{float x,y,z;V(float a=0,float b=0,float c=0):x(a),y(b),z(c){}float operator[](int a)const{return a==0?x:a==1?y:z;}V operator+(V b)const{return{x+b.x,y+b.y,z+b.z};}V operator-(V b)const{return{x-b.x,y-b.y,z-b.z};}V operator*(float s)const{return{x*s,y*s,z*s};}V operator*(V s)const{return{x*s.x,y*s.y,z*s.z};}V operator/(float s)const{return *this*(1/s);}};
float dot(V a,V b){return a.x*b.x+a.y*b.y+a.z*b.z;}V cross(V a,V b){return{a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x};}V unit(V a){return a/max(1e-16f,sqrt(dot(a,a)));}V minv(V a,V b){return{min(a.x,b.x),min(a.y,b.y),min(a.z,b.z)};}V maxv(V a,V b){return{max(a.x,b.x),max(a.y,b.y),max(a.z,b.z)};}
float rnd(uint32_t &s){s^=s<<13;s^=s>>17;s^=s<<5;return (s&0xffffff)/16777216.f;}
struct Vertex{V p,n,c;};struct Tri{uint32_t a,b,c,mat;};struct Node{V lo,hi;int left=-1,right=-1,start,count;};struct Ray{V o,d,inv;Ray(V p,V dir):o(p),d(dir),inv(1/(dir.x+1e-25f),1/(dir.y+1e-25f),1/(dir.z+1e-25f)){} };struct Hit{float t,u,v;int id=-1;};
vector<Vertex> vert;vector<Tri>tri;vector<int>order;vector<Node>nodes;
V center(int id){auto&t=tri[id];return(vert[t.a].p+vert[t.b].p+vert[t.c].p)/3;}
int build(int start,int count){int index=nodes.size();nodes.push_back({});V lo(1e9,1e9,1e9),hi(-1e9,-1e9,-1e9),clo=lo,chi=hi;
for(int i=start;i<start+count;i++){auto&t=tri[order[i]];for(auto k:{t.a,t.b,t.c}){lo=minv(lo,vert[k].p);hi=maxv(hi,vert[k].p);}V c=center(order[i]);clo=minv(clo,c);chi=maxv(chi,c);}
nodes[index].lo=lo-V(1e-6,1e-6,1e-6);nodes[index].hi=hi+V(1e-6,1e-6,1e-6);nodes[index].start=start;nodes[index].count=count;
if(count>7){V extent=chi-clo;int axis=extent.y>extent.x?1:0;if(extent.z>extent[axis])axis=2;int mid=start+count/2;nth_element(order.begin()+start,order.begin()+mid,order.begin()+start+count,[axis](int a,int b){return center(a)[axis]<center(b)[axis];});int left=build(start,mid-start),right=build(mid,start+count-mid);nodes[index].left=left;nodes[index].right=right;}
return index;}
bool box(const Ray&r,const Node&n,float maxdist){float near=0,far=maxdist;for(int a=0;a<3;a++){float l=(n.lo[a]-r.o[a])*r.inv[a],h=(n.hi[a]-r.o[a])*r.inv[a];if(l>h)swap(l,h);near=max(near,l);far=min(far,h);if(far<near)return false;}return true;}
bool trace(const Ray&r,Hit&hit,float far,bool cull=false,bool any=false){hit.t=far;int stack[80],sp=0;stack[sp++]=0;bool found=false;
while(sp){auto&node=nodes[stack[--sp]];if(!box(r,node,hit.t))continue;if(node.left>=0){stack[sp++]=node.left;stack[sp++]=node.right;continue;}
for(int k=node.start;k<node.start+node.count;k++){int id=order[k];auto&t=tri[id];V a=vert[t.a].p,e1=vert[t.b].p-a,e2=vert[t.c].p-a;V p=cross(r.d,e2);float det=dot(e1,p);if(cull?det<1e-12f:abs(det)<1e-12f)continue;float inv=1/det;V tv=r.o-a;float u=dot(tv,p)*inv;if(u<0||u>1)continue;V q=cross(tv,e1);float v=dot(r.d,q)*inv;if(v<0||u+v>1)continue;float d=dot(e2,q)*inv;if(d<1e-5f||d>=hit.t)continue;hit={d,u,v,id};found=true;if(any)return true;}}
return found;}
float occlusion(V p,V n,uint32_t&seed,int samples){V up=abs(n.y)<.95f?V(0,1,0):V(1,0,0);V tangent=unit(cross(up,n)),bitangent=cross(n,tangent);float occ=0;
for(int i=0;i<samples;i++){float u=(i+rnd(seed))/samples,a=rnd(seed)*6.2831853f;V dir=tangent*(sqrt(u)*cos(a))+bitangent*(sqrt(u)*sin(a))+n*sqrt(1-u);Hit h;if(trace(Ray(p+n*.00014f,dir),h,.055f,false,false))occ+=pow(max(0.f,1-h.t/.055f),.6f);}
return 1-.80f*occ/samples;}
V shade(const Ray&r,const Hit&h,uint32_t &seed,int quality){auto&t=tri[h.id];float w=1-h.u-h.v;V p=r.o+r.d*h.t;V n=unit(vert[t.a].n*w+vert[t.b].n*h.u+vert[t.c].n*h.v);if(dot(n,r.d)>0)n=n*-1;
V color=vert[t.a].c*w+vert[t.b].c*h.u+vert[t.c].c*h.v;float ao=occlusion(p,n,seed,quality?12:3);float hemi=.33f+.10f*max(-.6f,n.y);V result=color*(hemi*ao);V key(-.37f,.47f,.50f),fill(.6f,.3f,.8f);int samples=quality?5:1;
for(int i=0;i<samples;i++){V lp=key+V((rnd(seed)-.5f)*.28f,(rnd(seed)-.5f)*.25f,(rnd(seed)-.5f)*.10f);V dir=unit(lp-p);float dist=sqrt(dot(lp-p,lp-p));Hit sh;float vis=trace(Ray(p+n*.00014f,dir),sh,dist,false,true)?0:1;
float wrap=t.mat==0?.14f:0.f;float diffuse=max(0.f,(dot(n,dir)+wrap)/(1+wrap));result=result+color*(diffuse*.77f*vis/samples);
V halfv=unit(dir-r.d);float expn=(t.mat>=3&&t.mat<=6)?95.f:33.f;float spec=pow(max(0.f,dot(n,halfv)),expn)*((t.mat>=3&&t.mat<=6)?.30f:.052f);result=result+V(1,1,1)*(spec*vis/samples);}
float fd=max(0.f,dot(n,unit(fill)));result=result+color*(.12f*fd*sqrt(ao));
float rim=max(0.f,dot(n,unit(V(.6f,.5f,-.8f))));result=result+color*(.10f*rim*ao);
if(t.mat==6)result=V(1,1,1);return result;}
float srgb(float v){v=max(0.f,min(1.f,v));return v<=.0031308f?v*12.92f:1.055f*pow(v,1/2.4f)-.055f;}
int main(int argc,char**argv){if(argc<5)return 1;ifstream in(argv[1],ios::binary);uint32_t nv,nt;in.read((char*)&nv,4);in.read((char*)&nt,4);vert.resize(nv);tri.resize(nt);in.read((char*)vert.data(),nv*sizeof(Vertex));in.read((char*)tri.data(),nt*sizeof(Tri));if(!in){cerr<<"Bad binary mesh\n";return 2;}
int W=atoi(argv[3]),H=atoi(argv[4]);float yaw=argc>5?atof(argv[5]):0,pitch=.018;int quality=argc>6?atoi(argv[6]):1;
float cy=cos(yaw),sy=sin(yaw),cx=cos(pitch),sx=sin(pitch);auto rot=[&](V p){V q(cy*p.x+sy*p.z,p.y,-sy*p.x+cy*p.z);return V(q.x,cx*q.y-sx*q.z,sx*q.y+cx*q.z);};for(auto&v:vert){v.p=rot(v.p);v.n=rot(v.n);}
order.resize(nt);iota(order.begin(),order.end(),0);nodes.reserve(nt/3);build(0,nt);cerr<<"BVH "<<nodes.size()<<" nodes, threads "<<min(8,omp_get_max_threads())<<"\n";omp_set_num_threads(min(8,omp_get_max_threads()));
vector<unsigned char>pixels(W*H*3);float scale=H*2.80f;
#pragma omp parallel for schedule(dynamic,4)
for(int y=0;y<H;y++){for(int x=0;x<W;x++){uint32_t seed=uint32_t(x*1973+y*9277+89173)|1;float wx=(x+.5f-W*.5f)/scale,wy=(H*.495f-y-.5f)/scale;Ray r(V(wx,wy,1),V(0,0,-1));Hit hit;V col;
if(trace(r,hit,5,true)){col=shade(r,hit,seed,quality);}else{float g=float(y)/H;float shadow=.045f*exp(-pow((x-W*.5f)/(W*.22f),2)-pow((y-H*.96f)/(H*.018f),2));col=V(.887f-.028f*g-shadow,.876f-.026f*g-shadow,.846f-.022f*g-shadow);}
pixels[(y*W+x)*3]=uint8_t(srgb(col.x)*255+.5f);pixels[(y*W+x)*3+1]=uint8_t(srgb(col.y)*255+.5f);pixels[(y*W+x)*3+2]=uint8_t(srgb(col.z)*255+.5f);}}
ofstream out(argv[2],ios::binary);out<<"P6\n"<<W<<" "<<H<<"\n255\n";out.write((char*)pixels.data(),pixels.size());cout<<argv[2]<<"\n";
}
