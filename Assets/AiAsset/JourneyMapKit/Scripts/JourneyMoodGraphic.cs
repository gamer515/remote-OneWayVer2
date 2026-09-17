using UnityEngine;
using UnityEngine.UI;

namespace JourneyMapKit
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class JourneyMoodGraphic : MaskableGraphic
    {
        [SerializeField,Range(-1,1)] float mood=.65f;
        public float Mood => mood;
        public void SetMood(float value) { mood=Mathf.Clamp(value,-1,1);SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();Rect r=GetPixelAdjustedRect();
            float left=r.xMin+18,right=r.xMax-50,mid=r.center.y;
            Color ink=new Color(.40f,.35f,.25f,.65f);
            JourneyFrameGraphic.Line(vh,new Vector2(left,mid),new Vector2(right,mid),1,ink);
            for(int i=0;i<32;i++)
            {
                float x=Mathf.Lerp(left,right,i/31f);
                JourneyFrameGraphic.Line(vh,new Vector2(x,mid-3),new Vector2(x,mid+3),.7f,ink);
            }
            Vector2 prev=Vector2.zero;
            for(int i=0;i<=100;i++)
            {
                float t=i/100f;float wave=Mathf.Sin(t*5*Mathf.PI)*r.height*.10f + mood*t*r.height*.08f;
                Vector2 p=new Vector2(Mathf.Lerp(left,right,t),mid+wave);
                if(i>0) JourneyFrameGraphic.Line(vh,prev,p,2.5f,wave>=0?new Color(.24f,.46f,.28f):new Color(.62f,.29f,.21f));
                prev=p;
            }
            Vector2 c=new Vector2(r.xMax-28,mid);float radius=Mathf.Min(16,r.height*.2f);
            Color face=Color.Lerp(new Color(.68f,.36f,.24f),new Color(.28f,.53f,.30f),(mood+1)*.5f);
            for(int i=0;i<48;i++)
            {
                float a=i*Mathf.PI*2/48,b=(i+1)*Mathf.PI*2/48;
                JourneyFrameGraphic.Line(vh,c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,c+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,2,face);
            }
            foreach(float x in new[]{-.35f,.35f}) JourneyFrameGraphic.Line(vh,c+new Vector2(x*radius,.28f*radius),c+new Vector2(x*radius,.40f*radius),2,face);
            for(int i=0;i<16;i++)
            {
                float a=Mathf.Lerp(-.55f,.55f,i/16f),b=Mathf.Lerp(-.55f,.55f,(i+1)/16f);
                JourneyFrameGraphic.Line(vh,c+new Vector2(a*radius,-.2f*radius-mood*(1-a*a*3)*radius*.3f),c+new Vector2(b*radius,-.2f*radius-mood*(1-b*b*3)*radius*.3f),1.8f,face);
            }
        }
    }
}
