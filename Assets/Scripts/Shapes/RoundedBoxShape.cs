using UnityEngine;

[ExecuteAlways]
public class RoundedBoxShape : Shape
{
    public Vector2 size = new(1,1);
    [Min(0)] public float radius = 0.2f;
    [Range(2,32)] public int cornerSegments = 6;
    public bool filled = true;
    public float outlineThickness = 0.05f;
    [Tooltip("Draw outline even when filled (outward stroke).")]
    public bool drawOutline = true;

    protected override void BuildMesh(Mesh target)
    {
        Vector2 half = size*0.5f;
        float r = Mathf.Clamp(radius,0, Mathf.Min(half.x, half.y));
        if (r <= 0f)
        {
            // fallback to simple box
            Vector3[] v={new(-half.x,half.y,0), new(half.x,half.y,0), new(half.x,-half.y,0), new(-half.x,-half.y,0)}; Color[] c={color,color,color,color}; int[] tris={0,1,2,0,2,3}; target.SetVertices(v); target.SetColors(c); target.SetTriangles(tris,0); return;
        }
        int seg = Mathf.Max(2, cornerSegments);
    if (filled)
        {
            // Center rectangle + 4 corner fans
            System.Collections.Generic.List<Vector3> verts = new();
            System.Collections.Generic.List<Color> cols = new();
            System.Collections.Generic.List<int> tris = new();
            Vector2 inner = new(half.x - r, half.y - r);
            // central quad
            int baseQuad = verts.Count; verts.Add(new(-inner.x, inner.y,0)); verts.Add(new(inner.x, inner.y,0)); verts.Add(new(inner.x,-inner.y,0)); verts.Add(new(-inner.x,-inner.y,0)); for(int i=0;i<4;i++) cols.Add(color); tris.AddRange(new[]{baseQuad+0,baseQuad+1,baseQuad+2, baseQuad+0,baseQuad+2,baseQuad+3});
            // corners data
            Vector2[] centers = { new(-inner.x, inner.y), new(inner.x, inner.y), new(inner.x, -inner.y), new(-inner.x, -inner.y)};
            float[] startAngles = { Mathf.PI, -Mathf.PI/2f, 0f, Mathf.PI/2f };
            for(int corner=0;corner<4;corner++)
            {
                int centerIndex = verts.Count; verts.Add(new Vector3(centers[corner].x, centers[corner].y,0)); cols.Add(color);
                for(int i=0;i<=seg;i++)
                {
                    float t = i/(float)seg; float ang = startAngles[corner] + t*Mathf.PI/2f;
                    Vector3 p = new(centers[corner].x + Mathf.Cos(ang)*r, centers[corner].y + Mathf.Sin(ang)*r, 0);
                    verts.Add(p); cols.Add(color);
                }
                for(int i=0;i<seg;i++)
                {
                    tris.Add(centerIndex); tris.Add(centerIndex+1+i); tris.Add(centerIndex+2+i);
                }
                // bridge to central rectangle using one triangle fan per corner edge
                // (optional, central quad already covers interior)
            }
            // Outward stroke ring if requested
            if (drawOutline && outlineThickness > 0f)
            {
                System.Collections.Generic.List<Vector3> innerPerim = new();
                System.Collections.Generic.List<Vector3> outerPerim = new();
                for(int cci=0; cci<4; cci++)
                {
                    for(int i=0;i<=seg;i++)
                    {
                        float t=i/(float)seg; float ang = startAngles[cci] + t*Mathf.PI/2f;
                        Vector2 baseP = new(centers[cci].x + Mathf.Cos(ang)*r, centers[cci].y + Mathf.Sin(ang)*r);
                        innerPerim.Add(new Vector3(baseP.x, baseP.y,0));
                        Vector2 radial = (baseP - centers[cci]); if (radial.sqrMagnitude < 1e-6f) radial = Vector2.up; radial.Normalize();
                        Vector2 outP = baseP + radial * outlineThickness;
                        outerPerim.Add(new Vector3(outP.x, outP.y,0));
                    }
                }
                int ringCount = innerPerim.Count;
                Color strokeCol = useOutlineColor ? outlineColor : color;
                for(int i=0;i<ringCount;i++)
                {
                    Vector3 aIn = innerPerim[i];
                    Vector3 bIn = innerPerim[(i+1)%ringCount];
                    Vector3 aOut = outerPerim[i];
                    Vector3 bOut = outerPerim[(i+1)%ringCount];
                    int s = verts.Count; verts.Add(aIn); verts.Add(bIn); verts.Add(bOut); verts.Add(aOut); cols.Add(strokeCol); cols.Add(strokeCol); cols.Add(strokeCol); cols.Add(strokeCol);
                    tris.AddRange(new[]{s+0,s+2,s+1,s+0,s+3,s+2});
                }
            }
            target.SetVertices(verts); target.SetColors(cols); target.SetTriangles(tris,0);
        }
        else
        {
            // Outline: build polyline around rounded rectangle using quads (inflate by thickness)
            System.Collections.Generic.List<Vector3> outline = new();
            // gather perimeter points (outer path)
            int totalCorners = 4;
            Vector2[] centers = { new(-half.x + r, half.y - r), new(half.x - r, half.y - r), new(half.x - r, -half.y + r), new(-half.x + r, -half.y + r)};
            float[] starts = { Mathf.PI, -Mathf.PI/2f, 0f, Mathf.PI/2f };
            for(int c=0;c<totalCorners;c++)
            {
                for(int i=0;i<=seg;i++)
                {
                    float t = i/(float)seg; float ang = starts[c] + t*Mathf.PI/2f;
                    outline.Add(new Vector3(centers[c].x + Mathf.Cos(ang)*r, centers[c].y + Mathf.Sin(ang)*r,0));
                }
            }
            // Build thick polyline
            System.Collections.Generic.List<Vector3> verts = new();
            System.Collections.Generic.List<Color> cols = new();
            System.Collections.Generic.List<int> tris = new();
            Color strokeCol = useOutlineColor ? outlineColor : color;
            for(int i=0;i<outline.Count;i++)
            {
                Vector3 a = outline[i];
                Vector3 b = outline[(i+1)%outline.Count];
                Vector3 d = b - a; float len = d.magnitude;
                if (len < 1e-4f) continue;
                d /=len;
                Vector3 perp = new Vector3(-d.y,d.x,0)*outlineThickness;
                int s = verts.Count;
                verts.Add(a+perp);
                verts.Add(b+perp);
                verts.Add(b-perp);
                verts.Add(a-perp);
                for(int k=0;k<4;k++) cols.Add(strokeCol);
                tris.AddRange(new[]{s,s+1,s+2,s,s+2,s+3});
            }
            target.SetVertices(verts);
            target.SetColors(cols);
            target.SetTriangles(tris,0);
        }
    }

    protected override void DrawGizmos()
    {
        Vector2 half = size*0.5f; float r = Mathf.Clamp(radius,0,Mathf.Min(half.x,half.y)); Vector3 center = transform.position; if(r<=0){Vector3 a=center+new Vector3(-half.x,half.y,0); Vector3 b=center+new Vector3(half.x,half.y,0); Vector3 c=center+new Vector3(half.x,-half.y,0); Vector3 d=center+new Vector3(-half.x,-half.y,0); Gizmos.DrawLine(a,b); Gizmos.DrawLine(b,c); Gizmos.DrawLine(c,d); Gizmos.DrawLine(d,a); return;} int seg=Mathf.Max(2,cornerSegments); Vector2[] corners={ new(-half.x+r,half.y-r), new(half.x-r,half.y-r), new(half.x-r,-half.y+r), new(-half.x+r,-half.y+r)}; float[] start={Mathf.PI,-Mathf.PI/2f,0f,Mathf.PI/2f}; Vector3 prev=Vector3.zero; bool hasPrev=false; for(int c=0;c<4;c++){ for(int i=0;i<=seg;i++){ float t=i/(float)seg; float ang=start[c]+t*Mathf.PI/2f; Vector3 p=center+new Vector3(corners[c].x+Mathf.Cos(ang)*r, corners[c].y+Mathf.Sin(ang)*r,0); if(hasPrev) Gizmos.DrawLine(prev,p); prev=p; hasPrev=true; }} Gizmos.DrawLine(prev, center+new Vector3(corners[0].x+Mathf.Cos(start[0])*r, corners[0].y+Mathf.Sin(start[0])*r,0));
    }
}
