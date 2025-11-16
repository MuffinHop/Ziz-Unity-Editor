using UnityEngine;

[ExecuteAlways]
public class PlusShape : Shape
{
    public float size = 0.5f;
    public float thickness = 0.1f;

    protected override void BuildMesh(Mesh target)
    {
        float s=size; float t=thickness*0.5f;
        // plus formed by vertical & horizontal rectangles union. We'll build 12 verts -> 4 quads but can merge; simpler build as two quads
        Vector3[] v = new Vector3[8]; Color[] c = new Color[8]; int[] tris = {0,1,2,0,2,3,4,5,6,4,6,7};
        // horizontal
        v[0]=new Vector3(-s, t,0); v[1]=new Vector3(s,t,0); v[2]=new Vector3(s,-t,0); v[3]=new Vector3(-s,-t,0);
        // vertical
        v[4]=new Vector3(t,s,0); v[5]=new Vector3(-t,s,0); v[6]=new Vector3(-t,-s,0); v[7]=new Vector3(t,-s,0);
        for(int i=0;i<v.Length;i++) c[i]=color;
        target.SetVertices(v); target.SetColors(c); target.SetTriangles(tris,0);
    }

    protected override void DrawGizmos()
    {
        Vector3 o=transform.position; float s=size; Gizmos.DrawLine(o+new Vector3(-s,0,0), o+new Vector3(s,0,0)); Gizmos.DrawLine(o+new Vector3(0,-s,0), o+new Vector3(0,s,0));
    }
}
