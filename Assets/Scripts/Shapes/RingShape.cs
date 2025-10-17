using UnityEngine;

[ExecuteAlways]
public class RingShape : Shape
{
    public float outerRadius = 0.5f;
    public float innerRadius = 0.4f;
    [Range(3,256)] public int segments = 64;

    protected override void BuildMesh(Mesh target)
    {
        if (outerRadius <= 0 || innerRadius <= 0 || innerRadius >= outerRadius) return;
        int seg = Mathf.Clamp(segments,3,1024);
        Vector3[] v = new Vector3[seg*2]; Color[] c = new Color[v.Length]; int[] tris = new int[seg*6];
        for(int i=0;i<seg;i++)
        {
            float t = (float)i/seg*Mathf.PI*2f;
            Vector2 dir = new(Mathf.Cos(t), Mathf.Sin(t));
            v[i*2] = dir * innerRadius;
            v[i*2+1] = dir * outerRadius;
            c[i*2] = c[i*2+1] = color;
        }
        int ti=0; for(int i=0;i<seg;i++){int i0=(i*2)%v.Length; int i1=(i*2+1)%v.Length; int i2=(i*2+2)%v.Length; int i3=(i*2+3)%v.Length; tris[ti++]=i1; tris[ti++]=i0; tris[ti++]=i2; tris[ti++]=i1; tris[ti++]=i2; tris[ti++]=i3; }
        target.SetVertices(v); target.SetColors(c); target.SetTriangles(tris,0);
    }

    protected override void DrawGizmos()
    {
        Vector3 center = transform.position; int seg=Mathf.Max(segments,3); Vector3 prevOuter=center+Vector3.right*outerRadius; for(int i=1;i<=seg;i++){float t=(float)i/seg*Mathf.PI*2f; Vector3 pO=center+new Vector3(Mathf.Cos(t)*outerRadius,Mathf.Sin(t)*outerRadius,0); Gizmos.DrawLine(prevOuter,pO); prevOuter=pO;} Vector3 prevInner=center+Vector3.right*innerRadius; for(int i=1;i<=seg;i++){float t=(float)i/seg*Mathf.PI*2f; Vector3 pI=center+new Vector3(Mathf.Cos(t)*innerRadius,Mathf.Sin(t)*innerRadius,0); Gizmos.DrawLine(prevInner,pI); prevInner=pI;}
    }
}
