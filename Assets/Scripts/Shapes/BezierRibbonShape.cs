using UnityEngine;

[ExecuteAlways]
public class BezierRibbonShape : Shape
{
    public Vector2 start = new(-1,0);
    public Vector2 control = new(0,1);
    public Vector2 end = new(1,0);
    public float halfThickness = 0.1f;
    [Range(2,64)] public int segments = 16;
    public bool useChildPoints = true; // expects 3 children start/control/end

    protected override void BuildMesh(Mesh target)
    {
        if (useChildPoints && transform.childCount >= 3)
        {
            start = transform.GetChild(0).localPosition;
            control = transform.GetChild(1).localPosition;
            end = transform.GetChild(2).localPosition;
        }
        int seg = Mathf.Max(2, segments);
        Vector3[] center = new Vector3[seg+1];
        for(int i=0;i<=seg;i++)
        {
            float t = i/(float)seg; float u=1-t; center[i] = new Vector3(u*u*start.x + 2*u*t*control.x + t*t*end.x, u*u*start.y + 2*u*t*control.y + t*t*end.y,0);
        }
        // build oriented quad strip
        Vector3[] verts = new Vector3[(seg+1)*2]; Color[] cols = new Color[verts.Length]; int[] tris = new int[seg*6];
        for(int i=0;i<=seg;i++)
        {
            Vector3 p = center[i]; Vector3 forward;
            if (i == 0) forward = center[1] - center[0];
            else if (i == seg) forward = center[seg] - center[seg - 1];
            else forward = center[i + 1] - center[i - 1];
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.right;
            forward.Normalize();
            Vector3 perp = new Vector3(-forward.y * halfThickness, forward.x * halfThickness, 0.0f);
            verts[i * 2] = p + perp;
            verts[i * 2 + 1] = p - perp;
            cols[i * 2] = cols[i * 2 + 1] = color;
        }
        int ti = 0;
        for (int i = 0; i < seg; i++)
        {
            int i0 = i * 2;
            int i1 = i * 2 + 1;
            int i2 = i * 2 + 2;
            int i3 = i * 2 + 3;
            tris[ti++] = i0;
            tris[ti++] = i2;
            tris[ti++] = i1;
            tris[ti++] = i2;
            tris[ti++] = i3;
            tris[ti++] = i1;
        }
        target.SetVertices(verts);
        target.SetColors(cols);
        target.SetTriangles(tris,   0);
    }

    protected override void DrawGizmos()
    {
        Vector3 o=transform.position; int seg=Mathf.Max(2,segments); Vector3 prev=o+(Vector3)start; for(int i=1;i<=seg;i++){float t=i/(float)seg; float u=1-t; Vector3 p = o+ new Vector3(u*u*start.x + 2*u*t*control.x + t*t*end.x, u*u*start.y + 2*u*t*control.y + t*t*end.y,0); Gizmos.DrawLine(prev,p); prev=p; }
    }
}
