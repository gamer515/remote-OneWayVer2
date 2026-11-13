using UnityEngine;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class MapSymbolGraphic : UnityEngine.UI.MaskableGraphic
{
    public bool building, warning;
    [Range(0,1)] public float warningHeight=.2f;
    private bool warningFromTop;

    public void SetWarningFront(float height, bool fromTop)
    {
        height = Mathf.Clamp01(height);
        if (Mathf.Approximately(height, warningHeight) && warningFromTop == fromTop) return;
        warningHeight = height;
        warningFromTop = fromTop;
        SetVerticesDirty();
    }
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear();var r=rectTransform.rect;
        void Triangle(Vector2 a,Vector2 b,Vector2 c,Color tint)
        {
            int n=vh.currentVertCount;vh.AddVert(a,tint,Vector2.zero);vh.AddVert(b,tint,Vector2.zero);vh.AddVert(c,tint,Vector2.zero);vh.AddTriangle(n,n+1,n+2);
        }
        void Quad(Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color tint){Triangle(a,b,c,tint);Triangle(a,c,d,tint);}
        if(warning)
        {
            const int segments=32;
            float edge = warningFromTop ? r.yMax : r.yMin;
            float amplitude = Mathf.Min(.04f, warningHeight, 1f-warningHeight) * r.height;
            for(int i=0;i<segments;i++)
            {
                float x1=r.xMin+r.width*i/segments,x2=r.xMin+r.width*(i+1)/segments;
                float y1=r.yMin+r.height*warningHeight+Mathf.Sin(i*.5f)*amplitude;
                float y2=r.yMin+r.height*warningHeight+Mathf.Sin((i+1)*.5f)*amplitude;
                Quad(new Vector2(x1,edge),new Vector2(x2,edge),new Vector2(x2,y2),new Vector2(x1,y1),new Color(.5f,.17f,.46f,.25f));
                if(i%2==0)Quad(new Vector2(x1,y1-2),new Vector2(x2,y2-2),new Vector2(x2,y2+2),new Vector2(x1,y1+2),new Color(1,.48f,.73f,.9f));
            }
            return;
        }
        Vector2 center=r.center;float radius=Mathf.Min(r.width,r.height)*.48f;
        for(int i=0;i<24;i++)
        {
            float a=i*Mathf.PI/12,b=(i+1)*Mathf.PI/12;
            Triangle(center,center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,new Color(.04f,.08f,.12f,1));
            Triangle(center,center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius*.78f,center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius*.78f,color);
        }
        if(!building)return;
        float s=radius*.55f;Color white=new Color(.97f,.97f,.9f,1);
        Triangle(center+new Vector2(-s,0),center+new Vector2(s,0),center+new Vector2(0,s),white);
        Quad(center+new Vector2(-s*.7f,-s*.7f),center+new Vector2(s*.7f,-s*.7f),center+new Vector2(s*.7f,.1f*s),center+new Vector2(-s*.7f,.1f*s),white);
        Quad(center+new Vector2(-s*.15f,-s*.7f),center+new Vector2(s*.15f,-s*.7f),center+new Vector2(s*.15f,-s*.2f),center+new Vector2(-s*.15f,-s*.2f),color);
    }
}
