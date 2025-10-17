using UnityEngine;

[ExecuteAlways]
public class PolylineShape : Shape
{
    public Vector3[] points = new[]{ new Vector3(-1,0,0), new Vector3(0,1,0), new Vector3(1,0,0)};
    public float thickness = 0.05f;
    public bool closed = false;
    public bool drawCaps = true;
    [Tooltip("If true children define the polyline (local positions).")]
    public bool useChildPoints = true;

    protected override void BuildMesh(Mesh target)
    {
        if (useChildPoints && transform.childCount >= 2)
        {
            int cnt = transform.childCount;
            if (points == null || points.Length != cnt)
                points = new Vector3[cnt];
            for (int i = 0; i < cnt; i++) points[i] = transform.GetChild(i).localPosition;
        }
        if (points == null || points.Length < 2) return;
        System.Collections.Generic.List<Vector3> verts = new();
        System.Collections.Generic.List<Color> cols = new();
        System.Collections.Generic.List<int> tris = new();
        int segs = closed ? points.Length : points.Length-1;
        int running = 0;
        for(int i=0;i<segs;i++)
        {
            Vector3 p0 = points[i];
            Vector3 p1 = points[(i+1)%points.Length];
            Vector3 d = p1 - p0; float len = d.magnitude; if(len<1e-4f) continue; d/=len; Vector3 perp = new Vector3(-d.y,d.x,0)*thickness;
            int s = verts.Count;
            verts.Add(p0+perp); verts.Add(p1+perp); verts.Add(p1-perp); verts.Add(p0-perp);
            for(int k=0;k<4;k++) cols.Add(color);
            tris.AddRange(new[]{s,s+1,s+2,s,s+2,s+3});
            running++;
        }
        target.SetVertices(verts); target.SetColors(cols); target.SetTriangles(tris,0);
    }

    protected override void DrawGizmos()
    {
        if(points==null||points.Length<2) return; Gizmos.color=color; for(int i=0;i<points.Length-1;i++) Gizmos.DrawLine(transform.position+points[i], transform.position+points[i+1]); if(closed) Gizmos.DrawLine(transform.position+points[^1], transform.position+points[0]);
    }
}
