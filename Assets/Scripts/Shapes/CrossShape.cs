using UnityEngine;

[ExecuteAlways]
public class CrossShape : Shape
{
    public float size = 0.5f;
    public float thickness = 0.05f;

    protected override void BuildMesh(Mesh target)
    {
        // Build as two rectangles
        Vector3[] v = new Vector3[8]; Color[] c = new Color[8]; int[] tris = new int[12];
        float s=size; float t=thickness; // diag lines approximated by quads
        // line 1 from (-s,-s) to (s,s)
        Vector2 a = new(-s,-s); Vector2 b=new(s,s);
        AddQuad(a,b,0);
        // line 2 from (s,-s) to (-s,s)
        a=new(s,-s); b=new(-s,s);
        AddQuad(a,b,4);
        for(int i=0;i<8;i++) c[i]=color;
        // triangles indices 2 quads
        tris[0]=0;tris[1]=1;tris[2]=2; tris[3]=0;tris[4]=2;tris[5]=3; tris[6]=4;tris[7]=5;tris[8]=6; tris[9]=4;tris[10]=6;tris[11]=7;
        target.SetVertices(v); target.SetColors(c); target.SetTriangles(tris,0);

        void AddQuad(Vector2 p0, Vector2 p1, int offset){
            Vector2 d=(p1-p0);
            float l=d.magnitude;
            if (l < 1e-4f) {
                for (int i = 0; i < 4; i++) v[offset + i] = p0;
                return;
            }
            d/=l; Vector2 perp=new Vector2(-d.y,d.x)*t; v[offset+0]=p0+perp; v[offset+1]=p1+perp; v[offset+2]=p1-perp; v[offset+3]=p0-perp;}
    }

    protected override void DrawGizmos()
    {
        Vector3 o=transform.position; float s=size; Gizmos.DrawLine(o+new Vector3(-s,-s,0), o+new Vector3(s,s,0)); Gizmos.DrawLine(o+new Vector3(s,-s,0), o+new Vector3(-s,s,0));
    }
}
