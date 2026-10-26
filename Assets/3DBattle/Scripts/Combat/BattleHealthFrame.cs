using UnityEngine;
using UnityEngine.UI;

namespace OneWay.Battle3D
{
    // Resolution-independent antique metal rim; the health layers remain underneath.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BattleHealthFrame : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            if (r.width < 16 || r.height < 8) return;
            var shadow = new Color32(32, 30, 23, 255);
            var metal = new Color32(111, 105, 76, 255);
            var light = new Color32(183, 174, 128, 255);
            var dark = new Color32(63, 65, 50, 255);
            Quad(vh,r.xMin,r.yMax-4,r.xMax,r.yMax,shadow);
            Quad(vh,r.xMin,r.yMin,r.xMax,r.yMin+4,shadow);
            Quad(vh,r.xMin+3,r.yMax-2,r.xMax-3,r.yMax-1,light);
            Quad(vh,r.xMin+3,r.yMax-3,r.xMax-3,r.yMax-2,metal);
            Quad(vh,r.xMin+3,r.yMin+1,r.xMax-3,r.yMin+2,metal);
            Quad(vh,r.xMin+3,r.yMin+2,r.xMax-3,r.yMin+3,dark);
            Cap(vh,r.xMin,r.yMin,r.height,1,shadow,metal,light);
            Cap(vh,r.xMax,r.yMin,r.height,-1,shadow,metal,light);
        }
        static void Cap(VertexHelper vh,float x,float y,float h,float sign,Color32 shadow,Color32 metal,Color32 light)
        {
            Poly(vh,new Vector2(x,y),new Vector2(x+sign*9,y+4),new Vector2(x+sign*5,y+h-4),new Vector2(x,y+h),shadow);
            Poly(vh,new Vector2(x+sign,y+2),new Vector2(x+sign*6,y+5),new Vector2(x+sign*3,y+h-5),new Vector2(x+sign,y+h-2),metal);
            Poly(vh,new Vector2(x+sign,y+2),new Vector2(x+sign*3,y+4),new Vector2(x+sign*2,y+h-4),new Vector2(x+sign,y+h-2),light);
        }
        static void Quad(VertexHelper vh,float x,float y,float right,float top,Color32 c) =>
            Poly(vh,new Vector2(x,y),new Vector2(right,y),new Vector2(right,top),new Vector2(x,top),c);
        static void Poly(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color32 color)
        {
            int i=vh.currentVertCount;
            vh.AddVert(a,color,Vector2.zero);vh.AddVert(b,color,Vector2.zero);
            vh.AddVert(c,color,Vector2.zero);vh.AddVert(d,color,Vector2.zero);
            vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
    }
}
