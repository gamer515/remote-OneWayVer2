using UnityEngine;

// Scrolls menu surface patterns without moving the fixed player or exposing mesh edges.
public sealed class MainMenuCoastScroll : MonoBehaviour
{
    [Min(0)] public float scrollSpeed = 1.5f;
    [Tooltip("Ground texture repeats every this many world units.")]
    [Min(.01f)] public float groundTileSize = 4f;
    public MeshFilter[] groundSurfaces;
    public Renderer waterRenderer;
    [Tooltip("Water shader panning speed per unit of walking speed.")]
    public float waterSpeedScale = .1f;
    [Range(0,1)] public float waterDirection = .25f;
    Mesh[] originals, copies;
    Vector2[][] initialUVs;
    Material originalWater, water;
    double distance;

    void OnEnable()
    {
        int count=groundSurfaces==null?0:groundSurfaces.Length;
        originals=new Mesh[count];copies=new Mesh[count];initialUVs=new Vector2[count][];
        for(int i=0;i<count;i++)
        {
            if(!groundSurfaces[i] || !groundSurfaces[i].sharedMesh)continue;
            originals[i]=groundSurfaces[i].sharedMesh;
            copies[i]=Instantiate(originals[i]);copies[i].MarkDynamic();
            initialUVs[i]=originals[i].uv;groundSurfaces[i].sharedMesh=copies[i];
        }
        if(waterRenderer){originalWater=waterRenderer.sharedMaterial;water=new Material(originalWater);waterRenderer.sharedMaterial=water;}
        distance=0;
    }
    void Update()
    {
        distance+=Mathf.Max(0,scrollSpeed)*Time.deltaTime;
        float offset=(float)(distance/Mathf.Max(.01f,groundTileSize)%1.0);
        for(int i=0;i<copies.Length;i++)
        {
            if(!copies[i])continue;
            var uv=new Vector2[initialUVs[i].Length];var normals=originals[i].normals;
            for(int j=0;j<uv.Length;j++)
            {
                uv[j]=initialUVs[i][j];
                if(Mathf.Abs(normals[j].y)>.5f)uv[j].y+=offset;
                else if(Mathf.Abs(normals[j].x)>.5f)uv[j].x+=offset;
            }
            copies[i].uv=uv;
        }
        if(water){water.SetFloat("_SurfaceFoamSpeed",scrollSpeed*waterSpeedScale);water.SetFloat("_SurfaceFoamDirection",waterDirection);}
    }
    void OnDisable()
    {
        if(copies!=null)for(int i=0;i<copies.Length;i++)
        {if(groundSurfaces[i] && groundSurfaces[i].sharedMesh==copies[i])groundSurfaces[i].sharedMesh=originals[i];if(copies[i])Destroy(copies[i]);}
        if(waterRenderer && waterRenderer.sharedMaterial==water)waterRenderer.sharedMaterial=originalWater;
        if(water)Destroy(water);
    }
}

