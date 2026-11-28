using UnityEngine;
using UnityEngine.UI;

namespace OneWay.Battle3D
{
    // Resolution-independent antique metal rim; the health layers remain underneath.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BattleHealthFrame : MaskableGraphic
    {
        [Tooltip("Thin grey inset frame for the player's separate health and energy bars.")]
        public bool greyRectangularFrame;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            if (r.width < 16 || r.height < 8) return;
            if (greyRectangularFrame) { GreyFrame(vh, r); return; }
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
        static void GreyFrame(VertexHelper vh, Rect r)
        {
            // Static decoration: independent of the changing health/energy fill mesh.
            RectRing(vh, r, 1, new Color32(24,27,30,255), new Color32(15,17,20,255));
            r = Inset(r, 1);
            RectRing(vh, r, 1.5f, new Color32(157,165,173,255), new Color32(76,82,90,255));
            r = Inset(r, 1.5f);
            RectRing(vh, r, 1.5f, new Color32(18,23,27,255), new Color32(40,47,52,255));
        }
        static Rect Inset(Rect r, float amount) => Rect.MinMaxRect(r.xMin+amount,r.yMin+amount,r.xMax-amount,r.yMax-amount);
        static void RectRing(VertexHelper vh, Rect r, float width, Color32 light, Color32 dark)
        {
            Quad(vh,r.xMin,r.yMax-width,r.xMax,r.yMax,light);
            Quad(vh,r.xMin,r.yMin,r.xMax,r.yMin+width,dark);
            Quad(vh,r.xMin,r.yMin+width,r.xMin+width,r.yMax-width,light);
            Quad(vh,r.xMax-width,r.yMin+width,r.xMax,r.yMax-width,dark);
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
