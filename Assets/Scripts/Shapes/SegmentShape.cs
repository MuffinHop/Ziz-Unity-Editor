using UnityEngine;

[ExecuteAlways]
public class SegmentShape : Shape
{
    public Vector2 a = new(-0.5f,0);
    public Vector2 b = new(0.5f,0);
    public float halfThickness = 0.05f;
    public bool useChildEndpoints = true;
    [Tooltip("Draw outline stroke (additional outward thickness).")]
    public bool drawOutline = false;
    [Tooltip("Put outline stroke into second submesh for separate material.")]
    public bool outlineInSeparateSubmesh = false;

    protected override void BuildMesh(Mesh target)
    {
        if (useChildEndpoints && transform.childCount >= 2)
        {
            a = transform.GetChild(0).localPosition; b = transform.GetChild(1).localPosition;
        }
        Vector2 d = b - a; float len = d.magnitude;
        if (len < 1e-4f) return; d/=len;
        Vector2 perp = new Vector2(-d.y,d.x)*halfThickness;
        System.Collections.Generic.List<Vector3> verts = new();
        System.Collections.Generic.List<Color> cols = new();
    System.Collections.Generic.List<int> fillIndices = new();
    System.Collections.Generic.List<int> strokeIndices = new();
        int sIdx = verts.Count;
        verts.Add(a+perp); verts.Add(b+perp); verts.Add(b-perp); verts.Add(a-perp);
        for(int i=0;i<4;i++) cols.Add(color);
    fillIndices.AddRange(new[]{sIdx+0,sIdx+1,sIdx+2,sIdx+0,sIdx+2,sIdx+3});
        if (drawOutline && halfThickness > 0f)
        {
            Vector3 ap = a+perp; Vector3 bp = b+perp; Vector3 bm = b-perp; Vector3 am = a-perp;
            Color strokeCol = useOutlineColor ? outlineColor : color;
            float zOff = strokeZOffset;
            Vector3 dOut = (bp - ap); float lenOut = dOut.magnitude; if(lenOut>1e-5f){ dOut/=lenOut; Vector3 rawPerp = new Vector3(-dOut.y,dOut.x,0); Vector3 stroke = rawPerp * halfThickness; int si=verts.Count; verts.Add(new Vector3(ap.x,ap.y,zOff)); verts.Add(new Vector3(bp.x,bp.y,zOff)); verts.Add(new Vector3(bp.x+stroke.x,bp.y+stroke.y,zOff)); verts.Add(new Vector3(ap.x+stroke.x,ap.y+stroke.y,zOff)); cols.Add(strokeCol); cols.Add(strokeCol); cols.Add(strokeCol); cols.Add(strokeCol); strokeIndices.AddRange(new[]{si+0,si+2,si+1,si+0,si+3,si+2}); }
            Vector3 dIn = (bm - am); float lenIn = dIn.magnitude; if(lenIn>1e-5f){ dIn/=lenIn; Vector3 rawPerp = new Vector3(-dIn.y,dIn.x,0); Vector3 stroke = rawPerp * halfThickness; int si=verts.Count; verts.Add(new Vector3(am.x,am.y,zOff)); verts.Add(new Vector3(bm.x,bm.y,zOff)); verts.Add(new Vector3(bm.x+stroke.x,bm.y+stroke.y,zOff)); verts.Add(new Vector3(am.x+stroke.x,am.y+stroke.y,zOff)); cols.Add(strokeCol); cols.Add(strokeCol); cols.Add(strokeCol); cols.Add(strokeCol); strokeIndices.AddRange(new[]{si+0,si+2,si+1,si+0,si+3,si+2}); }
        }
        target.SetVertices(verts); target.SetColors(cols);
        if (strokeIndices.Count > 0 && outlineInSeparateSubmesh)
        {
            target.subMeshCount = 2;
            target.SetTriangles(fillIndices,0);
            target.SetTriangles(strokeIndices,1);
        }
        else
        {
            if (strokeIndices.Count > 0) fillIndices.AddRange(strokeIndices);
            target.subMeshCount = 1;
            target.SetTriangles(fillIndices,0);
        }
    }

    protected override void DrawGizmos()
    {
        Vector3 o = transform.position; Gizmos.DrawLine(o+(Vector3)a, o+(Vector3)b);
    }
}
