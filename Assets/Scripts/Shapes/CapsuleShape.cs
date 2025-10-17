using UnityEngine;

[ExecuteAlways]
public class CapsuleShape : Shape
{
    public Vector2 a = new Vector2(-0.5f, 0);
    public Vector2 b = new Vector2(0.5f, 0);
    public float radius = 0.25f;
    [Range(4,128)] public int segments = 24; // full circle sample count
    [Tooltip("If true and exactly two child transforms exist, their local positions define endpoints.")]
    public bool useChildEndpoints = true;

    protected override void BuildMesh(Mesh target)
    {
        if (useChildEndpoints)
        {
            // Use first two children (if present) as endpoints in local space
            if (transform.childCount >= 2)
            {
                a = transform.GetChild(0).localPosition;
                b = transform.GetChild(1).localPosition;
            }
        }
        Vector2 dir = b - a;
        float len = dir.magnitude;
        if (len < 1e-4f || radius <= 0) return;
        Vector2 norm = new Vector2(-dir.y, dir.x).normalized;
        int hemiSeg = Mathf.Max(2, segments/2);
        int sideVerts = 2; // rectangle side endpoints along length
        int totalVerts = hemiSeg + 1 + hemiSeg + 1 + sideVerts*2; // approximate overshoot; we'll build lists
        System.Collections.Generic.List<Vector3> v = new();
        System.Collections.Generic.List<Color> cols = new();
        System.Collections.Generic.List<int> tris = new();
        // Core rectangle
        Vector2 at = a + norm * radius; Vector2 ab = a - norm * radius;
        Vector2 bt = b + norm * radius; Vector2 bb = b - norm * radius;
        int baseIndex = v.Count; v.Add(at); v.Add(bt); v.Add(bb); v.Add(ab);
        for(int i=0;i<4;i++) cols.Add(color);
        tris.AddRange(new[]{baseIndex+0,baseIndex+1,baseIndex+2, baseIndex+0,baseIndex+2,baseIndex+3});
        // Left hemi (center a)
        int leftCenter = v.Count; v.Add(a); cols.Add(color);
        for(int i=0;i<=hemiSeg;i++)
        {
            float t = i/(float)hemiSeg; // 0..1 from top to bottom
            float ang = Mathf.PI/2f + t * Mathf.PI; // 90 -> 270 deg
            Vector2 p = a + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius;
            v.Add(p); cols.Add(color);
        }
        for(int i=0;i<hemiSeg;i++)
        {
            tris.Add(leftCenter); tris.Add(leftCenter+1+i); tris.Add(leftCenter+2+i);
        }
        // Right hemi (center b)
        int rightCenter = v.Count; v.Add(b); cols.Add(color);
        for(int i=0;i<=hemiSeg;i++)
        {
            float t = i/(float)hemiSeg; // bottom to top
            float ang = -Mathf.PI/2f + t * Mathf.PI; // -90 -> 90
            Vector2 p = b + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius;
            v.Add(p); cols.Add(color);
        }
        for(int i=0;i<hemiSeg;i++)
        {
            tris.Add(rightCenter); tris.Add(rightCenter+1+i); tris.Add(rightCenter+2+i);
        }
        target.SetVertices(v);
        target.SetColors(cols);
        target.SetTriangles(tris,0);
    }

    protected override void DrawGizmos()
    {
        Vector3 pos = transform.position;
        Gizmos.DrawLine(pos + (Vector3)a, pos + (Vector3)b);
    }
}
