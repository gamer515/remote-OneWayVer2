using UnityEngine;
using UnityEngine.UI;

namespace JourneyMapKit
{
    [AddComponentMenu("Journey Map/Ink Frame"), RequireComponent(typeof(CanvasRenderer))]
    public sealed class JourneyFrameGraphic : MaskableGraphic
    {
        [Min(.25f)] public float lineWidth = 1.2f;
        [Min(0)] public float inset = 4f;
        public bool cornerOrnaments = true;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width < 20 || r.height < 20) return;
            Ring(vh,r,inset,lineWidth,color);
            Color inner=color; inner.a*=.58f;
            Ring(vh,r,inset+5f,lineWidth*.65f,inner);
            if (!cornerOrnaments) return;
            foreach (float x in new[]{r.xMin+inset+12,r.xMax-inset-12})
            foreach (float y in new[]{r.yMin+inset+12,r.yMax-inset-12})
            {
                Vector2 c=new Vector2(x,y);
                Line(vh,c+Vector2.up*4,c+Vector2.right*3,lineWidth,color);
                Line(vh,c+Vector2.right*3,c+Vector2.down*4,lineWidth,color);
                Line(vh,c+Vector2.down*4,c+Vector2.left*3,lineWidth,color);
                Line(vh,c+Vector2.left*3,c+Vector2.up*4,lineWidth,color);
            }
        }
        static void Ring(VertexHelper vh,Rect r,float d,float w,Color c)
        {
            Vector2 a=new Vector2(r.xMin+d,r.yMin+d),b=new Vector2(r.xMax-d,r.yMin+d);
            Vector2 e=new Vector2(r.xMin+d,r.yMax-d),f=new Vector2(r.xMax-d,r.yMax-d);
            Line(vh,a,b,w,c);Line(vh,b,f,w,c);Line(vh,f,e,w,c);Line(vh,e,a,w,c);
        }
        public static void Line(VertexHelper vh,Vector2 a,Vector2 b,float width,Color color)
        {
            Vector2 d=(b-a).normalized;Vector2 n=new Vector2(-d.y,d.x)*width*.5f;
            int i=vh.currentVertCount;
            vh.AddVert(a-n,color,Vector2.zero);vh.AddVert(a+n,color,Vector2.zero);
            vh.AddVert(b+n,color,Vector2.zero);vh.AddVert(b-n,color,Vector2.zero);
            vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
    }
}
