using UnityEngine;

[ExecuteAlways]
public class HexagonShape : Shape
{
    public float size = 0.5f;
    public bool filled = false;
    public float thickness = 0.05f;
    [Tooltip("Draw outline even when filled (outward stroke).")]
    public bool drawOutline = true;

    protected override void BuildMesh(Mesh target)
    {
        int seg = 6;
        Vector3[] pts = new Vector3[seg];
        for(int i=0;i<seg;i++)
        {
            float t = (float)i/seg * Mathf.PI*2f + Mathf.PI/6f; // point up
            pts[i] = new Vector3(Mathf.Cos(t)*size, Mathf.Sin(t)*size,0);
        }
        System.Collections.Generic.List<Vector3> verts = new();
        System.Collections.Generic.List<Color> cols = new();
        System.Collections.Generic.List<int> indices = new();
        if (filled)
        {
            int baseIndex = verts.Count;
            for (int i = 0; i < seg; i++) { verts.Add(pts[i]); cols.Add(color); }
            for (int i = 1; i < seg - 1; i++) { indices.Add(baseIndex + 0); indices.Add(baseIndex + i); indices.Add(baseIndex + i + 1); }
        }
        else
        {
            void AddEdge(Vector3 a, Vector3 b){ Vector3 d=b-a; float len=d.magnitude; if(len<1e-4f) return; d/=len; Vector3 perp=new Vector3(-d.y,d.x,0)*thickness; int s=verts.Count; verts.Add(a+perp); verts.Add(b+perp); verts.Add(b-perp); verts.Add(a-perp); for(int k=0;k<4;k++) cols.Add(color); indices.AddRange(new[]{s,s+1,s+2,s,s+2,s+3}); }
            for(int i=0;i<seg;i++) AddEdge(pts[i], pts[(i+1)%seg]);
        }
    if (filled && drawOutline && thickness > 0f)
        {
            // outward stroke around hexagon edges
            float zOff = strokeZOffset;
            for(int i=0;i<seg;i++)
            {
                Vector3 a = pts[i];
                Vector3 b = pts[(i+1)%seg];
                Vector3 d = b-a; float len = d.magnitude; if(len < 1e-4f) continue; d/=len; Vector3 rawPerp = new Vector3(-d.y,d.x,0);
                Vector3 mid = (a+b)*0.5f; Vector3 centroid = Vector3.zero; for(int k=0;k<seg;k++) centroid += pts[k]; centroid/=seg;
                Vector3 pOut = mid + rawPerp; Vector3 pIn = mid - rawPerp; Vector3 perp = ((pOut-centroid).sqrMagnitude > (pIn-centroid).sqrMagnitude ? rawPerp : -rawPerp).normalized * thickness;
                Color strokeCol = useOutlineColor ? outlineColor : color;
                int s = verts.Count; verts.Add(new Vector3(a.x,a.y,zOff)); verts.Add(new Vector3(b.x,b.y,zOff)); verts.Add(new Vector3(b.x+perp.x,b.y+perp.y,zOff)); verts.Add(new Vector3(a.x+perp.x,a.y+perp.y,zOff)); cols.Add(strokeCol); cols.Add(strokeCol); cols.Add(strokeCol); cols.Add(strokeCol); indices.AddRange(new[]{s+0,s+2,s+1,s+0,s+3,s+2});
            }
        }
        target.SetVertices(verts); target.SetColors(cols); target.SetTriangles(indices,0);
    }

    protected override void DrawGizmos()
    {
        int seg=6; Vector3 pivot=transform.position; Vector3 prev=Vector3.zero; for(int i=0;i<=seg;i++){float t=(float)i/seg*Mathf.PI*2f+Mathf.PI/6f; Vector3 p=pivot+new Vector3(Mathf.Cos(t)*size,Mathf.Sin(t)*size,0); if(i>0) Gizmos.DrawLine(prev,p); prev=p;}
    }
}
