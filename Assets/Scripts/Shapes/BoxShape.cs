using UnityEngine;

[ExecuteAlways]
public class BoxShape : Shape
{
    public Vector2 size = new Vector2(1,1);
    public bool filled = true;
    public float thickness = 0.05f; // outline thickness if not filled
    public float cornerRadius = 0f; // future extension
    [Range(3,32)] public int cornerSegments = 6;
    [Tooltip("Draw outline even when filled (outward stroke).")]
    public bool drawOutline = true;

    protected override void BuildMesh(Mesh target)
    {
        if (size.x <= 0 || size.y <= 0) return;
        if (cornerRadius <= 0f)
        {
            // Rect case (supports fill + outline + outward stroke when filled)
            var verts = new System.Collections.Generic.List<Vector3>();
            var cols = new System.Collections.Generic.List<Color>();
            var indices = new System.Collections.Generic.List<int>();
            Vector2 half = size * 0.5f;
            if (filled)
            {
                int baseIndex = verts.Count;
                verts.Add(new Vector3(-half.x, half.y, 0));
                verts.Add(new Vector3(half.x, half.y, 0));
                verts.Add(new Vector3(half.x, -half.y, 0));
                verts.Add(new Vector3(-half.x, -half.y, 0));
                for (int i = 0; i < 4; i++) cols.Add(color);
                indices.Add(baseIndex + 0); indices.Add(baseIndex + 1); indices.Add(baseIndex + 2);
                indices.Add(baseIndex + 0); indices.Add(baseIndex + 2); indices.Add(baseIndex + 3);
            }
            else
            {
                void AddEdge(Vector2 a, Vector2 b)
                {
                    Vector2 dir = (b - a); float len = dir.magnitude; if (len < 1e-4f) return; dir /= len; Vector2 perp = new Vector2(-dir.y, dir.x) * thickness;
                    int s = verts.Count;
                    verts.Add(new Vector3(a.x + perp.x, a.y + perp.y, 0));
                    verts.Add(new Vector3(b.x + perp.x, b.y + perp.y, 0));
                    verts.Add(new Vector3(b.x - perp.x, b.y - perp.y, 0));
                    verts.Add(new Vector3(a.x - perp.x, a.y - perp.y, 0));
                    Color strokeCol = useOutlineColor ? outlineColor : color; cols.Add(strokeCol); cols.Add(strokeCol); cols.Add(strokeCol); cols.Add(strokeCol);
                    indices.Add(s + 0); indices.Add(s + 1); indices.Add(s + 2);
                    indices.Add(s + 0); indices.Add(s + 2); indices.Add(s + 3);
                }
                AddEdge(new Vector2(-half.x, half.y), new Vector2(half.x, half.y));
                AddEdge(new Vector2(half.x, half.y), new Vector2(half.x, -half.y));
                AddEdge(new Vector2(half.x, -half.y), new Vector2(-half.x, -half.y));
                AddEdge(new Vector2(-half.x, -half.y), new Vector2(-half.x, half.y));
            }
            if (filled && drawOutline && thickness > 0f)
            {
                Vector2 halfOuter = size * 0.5f + new Vector2(thickness, thickness);
                int baseIndex = verts.Count;
                // inner
                float zOff = strokeZOffset;
                verts.Add(new Vector3(-size.x * 0.5f, size.y * 0.5f, zOff)); // 0
                verts.Add(new Vector3(size.x * 0.5f, size.y * 0.5f, zOff));  // 1
                verts.Add(new Vector3(size.x * 0.5f, -size.y * 0.5f, zOff)); // 2
                verts.Add(new Vector3(-size.x * 0.5f, -size.y * 0.5f, zOff)); //3
                // outer
                verts.Add(new Vector3(-halfOuter.x, halfOuter.y, zOff)); //4
                verts.Add(new Vector3(halfOuter.x, halfOuter.y, zOff));  //5
                verts.Add(new Vector3(halfOuter.x, -halfOuter.y, zOff)); //6
                verts.Add(new Vector3(-halfOuter.x, -halfOuter.y, zOff));//7
                Color strokeCol = useOutlineColor ? outlineColor : color;
                for (int i = 0; i < 8; i++) cols.Add(strokeCol);
                void Quad(int i0, int i1, int o1, int o0){indices.Add(i0); indices.Add(o1); indices.Add(i1); indices.Add(i0); indices.Add(o0); indices.Add(o1);} 
                Quad(baseIndex+0, baseIndex+1, baseIndex+5, baseIndex+4);
                Quad(baseIndex+1, baseIndex+2, baseIndex+6, baseIndex+5);
                Quad(baseIndex+2, baseIndex+3, baseIndex+7, baseIndex+6);
                Quad(baseIndex+3, baseIndex+0, baseIndex+4, baseIndex+7);
            }
            target.SetVertices(verts); target.SetColors(cols); target.SetTriangles(indices,0);
            return;
        }
        // Rounded corner case (filled only; outline + stroke TODO)
        float r = Mathf.Min(cornerRadius, Mathf.Min(size.x, size.y) / 2f);
        int seg = Mathf.Max(1, cornerSegments);
        int arcVerts = seg + 1;
        int rectVerts = 4;
        Vector2 halfR = size * 0.5f;
        Vector2 inner = new Vector2(halfR.x - r, halfR.y - r);
        Vector3[] vtx = new Vector3[rectVerts + arcVerts * 4];
        Color[] col = new Color[vtx.Length];
        var trisList = new System.Collections.Generic.List<int>();
        // center rectangle (shrunk)
        vtx[0] = new Vector3(-inner.x, inner.y, 0);
        vtx[1] = new Vector3(inner.x, inner.y, 0);
        vtx[2] = new Vector3(inner.x, -inner.y, 0);
        vtx[3] = new Vector3(-inner.x, -inner.y, 0);
        for (int i = 0; i < 4; i++) col[i] = color;
        trisList.AddRange(new[]{0,1,2,0,2,3});
        Vector2[] centers = { new(-inner.x, inner.y), new(inner.x, inner.y), new(inner.x, -inner.y), new(-inner.x, -inner.y)};
        float[] starts = { Mathf.PI, -Mathf.PI / 2f, 0f, Mathf.PI / 2f };
        for (int c = 0; c < 4; c++)
        {
            int startIndex = 4 + c * arcVerts;
            for (int i = 0; i < arcVerts; i++)
            {
                float t = i / (float)seg;
                float ang = starts[c] + t * Mathf.PI / 2f;
                vtx[startIndex + i] = new Vector3(centers[c].x + Mathf.Cos(ang) * r, centers[c].y + Mathf.Sin(ang) * r, 0);
                col[startIndex + i] = color;
            }
            // fan
            int rectCorner = c; // maps 0..3
            for (int i = 0; i < seg; i++)
            {
                trisList.Add(rectCorner);
                trisList.Add(startIndex + i);
                trisList.Add(startIndex + i + 1);
            }
        }
        target.SetVertices(vtx); target.SetColors(col); target.SetTriangles(trisList, 0);
    }

    protected override void DrawGizmos()
    {
        Vector2 half = size * 0.5f; Vector3 o = transform.position;
        if (cornerRadius <= 0f)
        {
            Vector3 a = o + new Vector3(-half.x, half.y, 0);
            Vector3 b = o + new Vector3(half.x, half.y, 0);
            Vector3 cpt = o + new Vector3(half.x, -half.y, 0);
            Vector3 d = o + new Vector3(-half.x, -half.y, 0);
            Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, cpt); Gizmos.DrawLine(cpt, d); Gizmos.DrawLine(d, a);
            if (filled && drawOutline && thickness > 0f)
            {
                Vector2 halfOuter = half + new Vector2(thickness, thickness);
                Vector3 ao = o + new Vector3(-halfOuter.x, halfOuter.y, 0);
                Vector3 bo = o + new Vector3(halfOuter.x, halfOuter.y, 0);
                Vector3 co = o + new Vector3(halfOuter.x, -halfOuter.y, 0);
                Vector3 do_ = o + new Vector3(-halfOuter.x, -halfOuter.y, 0);
                Gizmos.DrawLine(ao, bo); Gizmos.DrawLine(bo, co); Gizmos.DrawLine(co, do_); Gizmos.DrawLine(do_, ao);
            }
        }
        else
        {
            // simple perimeter for rounded box (no stroke gizmo yet)
            float r = Mathf.Min(cornerRadius, Mathf.Min(half.x, half.y)); int seg = Mathf.Max(2, cornerSegments);
            Vector2 inner = new Vector2(half.x - r, half.y - r);
            Vector2[] centers = { new(-inner.x, inner.y), new(inner.x, inner.y), new(inner.x, -inner.y), new(-inner.x, -inner.y)};
            float[] starts = { Mathf.PI, -Mathf.PI/2f, 0f, Mathf.PI/2f };
            Vector3 first = Vector3.zero; Vector3 prev = Vector3.zero; bool started=false;
            for(int c=0;c<4;c++)
            {
                for(int i=0;i<=seg;i++)
                {
                    float t=i/(float)seg; float ang = starts[c] + t*Mathf.PI/2f;
                    Vector3 p = o + new Vector3(centers[c].x + Mathf.Cos(ang)*r, centers[c].y + Mathf.Sin(ang)*r,0);
                    if(!started){ first=p; started=true; }
                    else Gizmos.DrawLine(prev,p);
                    prev=p;
                }
            }
            Gizmos.DrawLine(prev, first);
        }
    }
}
