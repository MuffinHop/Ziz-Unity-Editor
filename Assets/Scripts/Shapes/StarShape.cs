using UnityEngine;

[ExecuteAlways]
public class StarShape : Shape
{
    [Range(3,32)] public int points = 5;
    public float outerRadius = 0.5f;
    public float innerRadius = 0.25f;
    public bool filled = true;
    [Tooltip("Outline (stroke) thickness in world units.")]
    public float thickness = 0.05f; // stroke thickness
    [Tooltip("Draw outline even when filled.")]
    public bool drawOutline = true;
    [Tooltip("Put outline stroke into a second submesh for separate material.")]
    public bool outlineInSeparateSubmesh = false;

    protected override void BuildMesh(Mesh target)
    {
        int p = Mathf.Clamp(points,3,64);
        if (outerRadius <= 0) return;

        // Build point list (alternating outer/inner) once.
        int ringVerts = p * 2;
        Vector3[] starPts = new Vector3[ringVerts];
        for (int i = 0; i < ringVerts; i++)
        {
            float t = (float)i / ringVerts * Mathf.PI * 2f;
            float r = (i % 2 == 0) ? outerRadius : innerRadius;
            starPts[i] = new Vector3(Mathf.Cos(t) * r, Mathf.Sin(t) * r, 0);
        }

        // Dynamic lists to combine fill + outline.
        System.Collections.Generic.List<Vector3> verts = new();
        System.Collections.Generic.List<Color> cols = new();
    System.Collections.Generic.List<int> fillIndices = new();
    System.Collections.Generic.List<int> strokeIndices = new();

        if (filled)
        {
            int centerIndex = verts.Count;
            verts.Add(Vector3.zero); cols.Add(color);
            for (int i = 0; i < ringVerts; i++) { verts.Add(starPts[i]); cols.Add(color); }
            for (int i = 0; i < ringVerts; i++)
            {
                int a = centerIndex;
                int b = centerIndex + 1 + i;
                int c = centerIndex + 1 + ((i + 1) % ringVerts);
                fillIndices.Add(a); fillIndices.Add(b); fillIndices.Add(c);
            }
        }

    float tScaled = ScaleThickness(thickness);
    if (drawOutline && tScaled > 0f)
        {
            // Stroke: outward-only quads along each edge of star polygon.
            // Determine approximate center for outward direction.
            Vector3 centroid = Vector3.zero; foreach (var pnt in starPts) centroid += pnt; centroid /= ringVerts;
            float zOff = strokeZOffset;
            for (int i = 0; i < ringVerts; i++)
            {
                Vector3 a = starPts[i];
                Vector3 b = starPts[(i + 1) % ringVerts];
                Vector3 d = b - a; float len = d.magnitude; if (len < 1e-5f) continue; d /= len;
                Vector3 rawPerp = new Vector3(-d.y, d.x, 0f);
                Vector3 mid = (a + b) * 0.5f;
                Vector3 pOut = mid + rawPerp; Vector3 pIn = mid - rawPerp;
                Vector3 perp = ((pOut - centroid).sqrMagnitude > (pIn - centroid).sqrMagnitude ? rawPerp : -rawPerp).normalized * tScaled;
                int s = verts.Count;
                verts.Add(new Vector3(a.x,a.y,zOff));          // inner A
                verts.Add(new Vector3(b.x,b.y,zOff));          // inner B
                verts.Add(new Vector3(b.x+perp.x,b.y+perp.y,zOff));   // outer B
                verts.Add(new Vector3(a.x+perp.x,a.y+perp.y,zOff));   // outer A
                Color strokeCol = GetEffectiveStrokeColor(useOutlineColor ? outlineColor : color);
                cols.Add(strokeCol); cols.Add(strokeCol); cols.Add(strokeCol); cols.Add(strokeCol);
                strokeIndices.Add(s + 0); strokeIndices.Add(s + 2); strokeIndices.Add(s + 1);
                strokeIndices.Add(s + 0); strokeIndices.Add(s + 3); strokeIndices.Add(s + 2);
            }
        }
        target.SetVertices(verts);
        target.SetColors(cols);
        if (strokeIndices.Count > 0 && outlineInSeparateSubmesh)
        {
            target.subMeshCount = 2;
            target.SetTriangles(fillIndices, 0);
            target.SetTriangles(strokeIndices, 1);
        }
        else
        {
            if (strokeIndices.Count > 0) fillIndices.AddRange(strokeIndices);
            target.subMeshCount = 1;
            target.SetTriangles(fillIndices, 0);
        }
    }

    protected override void DrawGizmos()
    {
        int ringVerts = points*2; if(ringVerts<6) return; Vector3 center = transform.position; Vector3 prev=Vector3.zero; for(int i=0;i<=ringVerts;i++){float t=(float)i/ringVerts*Mathf.PI*2f; float r=(i%2==0)? outerRadius: innerRadius; Vector3 p=center+new Vector3(Mathf.Cos(t)*r,Mathf.Sin(t)*r,0); if(i>0) Gizmos.DrawLine(prev,p); prev=p;}
    }
}
