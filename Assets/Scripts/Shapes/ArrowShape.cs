using UnityEngine;

[ExecuteAlways]
public class ArrowShape : Shape
{
    public Vector2 direction = Vector2.right;
    public float length = 1f;
    [Tooltip("Half thickness of the shaft body (radius from center line).")]
    public float thickness = 0.05f;
    public float headSize = 0.2f; // extra beyond shaft
    [Tooltip("If a child transform exists, its local position sets direction+length.")]
    public bool useChildDirection = true;
    [Tooltip("Draw outline stroke around arrow (outward). Thickness reused as stroke thickness.")]
    public bool drawOutline = false;
    [Tooltip("Optional stroke thickness (outward). If <= 0, uses 'thickness'.")] public float strokeThickness = 0f;

    protected override void BuildMesh(Mesh target)
    {
        if (useChildDirection && transform.childCount > 0)
        {
            Vector3 childPos = transform.GetChild(0).localPosition;
            length = childPos.magnitude;
            direction = new Vector2(childPos.x, childPos.y).normalized;
        }
        Vector2 dir = direction; float mag = dir.magnitude; if (mag < 1e-4f) return; dir/=mag;
        float shaftLen = Mathf.Max(0, length - headSize);
        // Build as rectangle + triangle head
        System.Collections.Generic.List<Vector3> verts = new();
        System.Collections.Generic.List<Color> cols = new();
        System.Collections.Generic.List<int> indices = new();
    float tScaled = ScaleThickness(thickness);
    Vector2 perp = new Vector2(-dir.y, dir.x) * tScaled;
        Vector2 origin = Vector2.zero;
        Vector2 shaftEnd = dir * shaftLen;
        // rectangle quad
        int baseRect = verts.Count;
        verts.Add(origin + perp); verts.Add(shaftEnd + perp); verts.Add(shaftEnd - perp); verts.Add(origin - perp);
        cols.Add(color); cols.Add(color); cols.Add(color); cols.Add(color);
        indices.AddRange(new[]{baseRect+0,baseRect+1,baseRect+2, baseRect+0,baseRect+2,baseRect+3});
        // head triangle
        Vector2 headBaseL = shaftEnd + perp * 2f;
        Vector2 headBaseR = shaftEnd - perp * 2f;
        Vector2 tip = dir * length;
        int triBase = verts.Count;
        verts.Add(headBaseL); verts.Add(headBaseR); verts.Add(tip);
        cols.Add(color); cols.Add(color); cols.Add(color);
        indices.AddRange(new[]{triBase+0,triBase+1,triBase+2});
        if (drawOutline && tScaled > 0f)
        {
            float stroke = strokeThickness > 0f ? ScaleThickness(strokeThickness) : tScaled; // effective stroke thickness
            Color strokeCol = GetEffectiveStrokeColor(useOutlineColor ? outlineColor : color);
            // outline of shaft rectangle edges
            float zOff = strokeZOffset;
            void AddStrokeQuad(Vector3 a, Vector3 b){ Vector3 d=b-a; float len=d.magnitude; if(len<1e-5f) return; d/=len; Vector3 rawPerp = new Vector3(-d.y,d.x,0); int s=verts.Count; verts.Add(new Vector3(a.x,a.y,zOff)); verts.Add(new Vector3(b.x,b.y,zOff)); verts.Add(new Vector3(b.x+rawPerp.x*stroke,b.y+rawPerp.y*stroke,zOff)); verts.Add(new Vector3(a.x+rawPerp.x*stroke,a.y+rawPerp.y*stroke,zOff)); cols.Add(strokeCol); cols.Add(strokeCol); cols.Add(strokeCol); cols.Add(strokeCol); indices.AddRange(new[]{s+0,s+2,s+1,s+0,s+3,s+2}); }
            Vector3 r0 = origin + perp; Vector3 r1 = shaftEnd + perp; Vector3 r2 = shaftEnd - perp; Vector3 r3 = origin - perp;
            AddStrokeQuad(r0,r1); AddStrokeQuad(r1,r2); AddStrokeQuad(r2,r3); AddStrokeQuad(r3,r0);
            // outline for head triangle edges
            void AddEdge(Vector3 a, Vector3 b){ Vector3 d=b-a; float len=d.magnitude; if(len<1e-5f) return; d/=len; Vector3 rawPerp=new Vector3(-d.y,d.x,0); int s=verts.Count; verts.Add(new Vector3(a.x,a.y,zOff)); verts.Add(new Vector3(b.x,b.y,zOff)); verts.Add(new Vector3(b.x+rawPerp.x*stroke,b.y+rawPerp.y*stroke,zOff)); verts.Add(new Vector3(a.x+rawPerp.x*stroke,a.y+rawPerp.y*stroke,zOff)); cols.Add(strokeCol); cols.Add(strokeCol); cols.Add(strokeCol); cols.Add(strokeCol); indices.AddRange(new[]{s+0,s+2,s+1,s+0,s+3,s+2}); }
            Vector3 h0=headBaseL; Vector3 h1=headBaseR; Vector3 h2=tip;
            AddEdge(h0,h1); AddEdge(h1,h2); AddEdge(h2,h0);
        }
        target.SetVertices(verts); target.SetColors(cols); target.SetTriangles(indices,0);
    }

    protected override void DrawGizmos()
    {
        Vector3 pos = transform.position; Vector2 dir = direction.normalized; Gizmos.DrawLine(pos, pos + (Vector3)dir*length);
    }
}
