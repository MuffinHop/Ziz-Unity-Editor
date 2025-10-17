using UnityEngine;

[ExecuteAlways]
public class TriangleShape : Shape
{
    public float size = 1f; // side length
    public float angleDeg = 0f; // rotation
    public bool filled = true;
    [Tooltip("Outline (stroke) thickness in world units.")]
    public float thickness = 0.05f;
    [Tooltip("Draw outline even when filled.")]
    public bool drawOutline = true;
    [Tooltip("Put outline triangles into a second submesh so you can assign a distinct material.")]
    public bool outlineInSeparateSubmesh = true;

    protected override void BuildMesh(Mesh target)
    {
        float h = size * Mathf.Sqrt(3f)/2f;
        Vector3[] pts = new Vector3[3];
        pts[0] = new Vector3(-size/2f, -h/3f, 0);
        pts[1] = new Vector3(size/2f, -h/3f, 0);
        pts[2] = new Vector3(0, 2f*h/3f, 0);
        Quaternion q = Quaternion.Euler(0,0,angleDeg);
        for(int i=0;i<3;i++) pts[i] = q * pts[i];
        // We build into dynamic lists so we can optionally append outline.
    System.Collections.Generic.List<Vector3> vertices = new();
    System.Collections.Generic.List<Color> colors = new();
    System.Collections.Generic.List<int> fillIndices = new();
    System.Collections.Generic.List<int> strokeIndices = new();

        if (filled)
        {
            int baseIndex = vertices.Count;
            vertices.AddRange(pts);
            colors.Add(color); colors.Add(color); colors.Add(color);
            fillIndices.Add(baseIndex + 0); fillIndices.Add(baseIndex + 1); fillIndices.Add(baseIndex + 2);
        }

    float tScaled = ScaleThickness(thickness);
    if (drawOutline && tScaled > 0f)
        {
            // Create outward-only quads per edge so the stroke is visible (inner edge aligns with shape boundary).
            Vector3 center = (pts[0] + pts[1] + pts[2]) / 3f;
            float zOff = strokeZOffset;
            void AddStrokeEdge(Vector3 a, Vector3 b)
            {
                Vector3 d = b - a; float len = d.magnitude; if (len < 1e-5f) return; d /= len;
                Vector3 rawPerp = new Vector3(-d.y, d.x, 0f);
                // Determine outward direction by comparing distances from center.
                Vector3 mid = (a + b) * 0.5f;
                Vector3 pOut = mid + rawPerp; Vector3 pIn = mid - rawPerp;
                Vector3 perp = ( (pOut - center).sqrMagnitude > (pIn - center).sqrMagnitude ? rawPerp : -rawPerp).normalized * tScaled;
                // Inner edge lies on the triangle edge (a,b); outer edge offset by perp.
                int s = vertices.Count;
                vertices.Add(new Vector3(a.x,a.y,zOff));         // inner A
                vertices.Add(new Vector3(b.x,b.y,zOff));         // inner B
                vertices.Add(new Vector3(b.x+perp.x,b.y+perp.y,zOff));  // outer B
                vertices.Add(new Vector3(a.x+perp.x,a.y+perp.y,zOff));  // outer A
                Color strokeCol = GetEffectiveStrokeColor(useOutlineColor ? outlineColor : color);
                colors.Add(strokeCol); colors.Add(strokeCol); colors.Add(strokeCol); colors.Add(strokeCol);
                strokeIndices.Add(s + 0); strokeIndices.Add(s + 2); strokeIndices.Add(s + 1); // first tri
                strokeIndices.Add(s + 0); strokeIndices.Add(s + 3); strokeIndices.Add(s + 2); // second tri
            }
            AddStrokeEdge(pts[0], pts[1]);
            AddStrokeEdge(pts[1], pts[2]);
            AddStrokeEdge(pts[2], pts[0]);
        }
        target.SetVertices(vertices);
        target.SetColors(colors);
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
        float h = size * Mathf.Sqrt(3f)/2f;
        Vector3[] pts = new Vector3[4];
        pts[0] = new Vector3(-size/2f, -h/3f, 0);
        pts[1] = new Vector3(size/2f, -h/3f, 0);
        pts[2] = new Vector3(0, 2f*h/3f, 0);
        pts[3] = pts[0];
        Quaternion q = Quaternion.Euler(0,0,angleDeg);
        Vector3 pivot = transform.position;
        for(int i=0;i<3;i++)
        {
            Vector3 a = pivot + q * pts[i];
            Vector3 b = pivot + q * pts[i+1];
            Gizmos.DrawLine(a,b);
        }
    }
}
