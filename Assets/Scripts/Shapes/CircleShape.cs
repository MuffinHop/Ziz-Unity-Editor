using UnityEngine;

[ExecuteAlways]
public class CircleShape : Shape
{
    [Min(0.0001f)] public float radius = 0.5f;
    [Range(3,128)] public int segments = 32;
    public bool filled = true;
    [Tooltip("Stroke / ring thickness in world units.")]
    public float thickness = 0.05f; // outline / stroke thickness
    [Tooltip("Draw outline even when filled (outward stroke).")]
    public bool drawOutline = true;
    [Tooltip("Put outline ring into a second submesh for separate material when filled.")]
    public bool outlineInSeparateSubmesh = false;

    protected override void BuildMesh(Mesh target)
    {
    if (radius <= 0) return;
        segments = Mathf.Clamp(segments, 3, 512);
        // We will accumulate data in lists to optionally append stroke.
        System.Collections.Generic.List<Vector3> verts = new();
        System.Collections.Generic.List<Color> cols = new();
    System.Collections.Generic.List<int> fillIndices = new();
    System.Collections.Generic.List<int> strokeIndices = new();

    if (filled)
        {
            int center = verts.Count;
            verts.Add(Vector3.zero); cols.Add(color);
            for (int i = 0; i < segments; i++)
            {
                float t = (float)i / segments * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(t), Mathf.Sin(t), 0) * radius);
                cols.Add(color);
                if (i < segments - 1)
                {
                    int tri = i * 3;
            fillIndices.Add(center);
            fillIndices.Add(center + 1 + i);
            fillIndices.Add(center + 2 + i);
                }
            }
            // final triangle
        fillIndices.Add(center);
        fillIndices.Add(center + segments);
        fillIndices.Add(center + 1);
        }
    else
        {
            // ring (depending on alignment we adjust inner/outer radii)
        float inner;
        float outer;
        float tScaled = ScaleThickness(thickness);
            switch (strokeAlignment)
            {
                case StrokeAlignment.Inner:
            inner = Mathf.Max(0, radius - tScaled);
                    outer = radius;
                    break;
                case StrokeAlignment.Center:
            inner = Mathf.Max(0, radius - tScaled * 0.5f);
            outer = inner + tScaled;
                    break;
                default: // Outer
                    inner = radius;
            outer = radius + tScaled;
                    break;
            }
            int baseIndex = verts.Count;
            for (int i = 0; i < segments; i++)
            {
                float t = (float)i / segments * Mathf.PI * 2f;
                Vector2 dir = new(Mathf.Cos(t), Mathf.Sin(t));
                verts.Add(dir * inner);
                verts.Add(dir * outer);
                Color strokeCol = GetEffectiveStrokeColor(useOutlineColor ? outlineColor : color);
        cols.Add(strokeCol); cols.Add(strokeCol);
            }
            for (int i = 0; i < segments; i++)
            {
                int i0 = baseIndex + i * 2;
                int i1 = baseIndex + i * 2 + 1;
                int i2 = baseIndex + ((i + 1) % segments) * 2;
                int i3 = baseIndex + ((i + 1) % segments) * 2 + 1;
                fillIndices.Add(i1); fillIndices.Add(i0); fillIndices.Add(i2);
                fillIndices.Add(i1); fillIndices.Add(i2); fillIndices.Add(i3);
            }
        }

        // Stroke when filled: ring whose placement depends on alignment
    float tScaledFill = ScaleThickness(thickness);
    if (filled && drawOutline && tScaledFill > 0f)
        {
            float innerR, outerR;
            switch (strokeAlignment)
            {
                case StrokeAlignment.Inner:
            innerR = Mathf.Max(0, radius - tScaledFill);
                    outerR = radius;
                    break;
                case StrokeAlignment.Center:
            innerR = Mathf.Max(0, radius - tScaledFill * 0.5f);
            outerR = innerR + tScaledFill;
                    break;
                default: // Outer
                    innerR = radius;
            outerR = radius + tScaledFill;
                    break;
            }
            int baseIndex = verts.Count;
            float zOff = strokeZOffset;
            for (int i = 0; i < segments; i++)
            {
                float t = (float)i / segments * Mathf.PI * 2f;
                Vector2 dir = new(Mathf.Cos(t), Mathf.Sin(t));
        Vector2 innerP = dir * innerR; Vector2 outerP = dir * outerR;
        verts.Add(new Vector3(innerP.x, innerP.y, zOff));      // inner
        verts.Add(new Vector3(outerP.x, outerP.y, zOff));       // outer
    Color strokeCol = GetEffectiveStrokeColor(useOutlineColor ? outlineColor : color);
        cols.Add(strokeCol); cols.Add(strokeCol);
            }
            for (int i = 0; i < segments; i++)
            {
                int i0 = baseIndex + i * 2;
                int i1 = baseIndex + i * 2 + 1;
                int i2 = baseIndex + ((i + 1) % segments) * 2;
                int i3 = baseIndex + ((i + 1) % segments) * 2 + 1;
                strokeIndices.Add(i0); strokeIndices.Add(i3); strokeIndices.Add(i1);
                strokeIndices.Add(i0); strokeIndices.Add(i2); strokeIndices.Add(i3);
            }
        }

        target.SetVertices(verts);
        target.SetColors(cols);
        if (strokeIndices.Count > 0 && outlineInSeparateSubmesh && filled)
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
        float r = radius;
        Vector3 center = transform.position;
        int seg = Mathf.Max(segments, 3);
        Vector3 prev = center + new Vector3(r, 0, 0);
        for (int i = 1; i <= seg; i++)
        {
            float t = (float)i / seg * Mathf.PI * 2f;
            Vector3 p = center + new Vector3(Mathf.Cos(t) * r, Mathf.Sin(t) * r, 0);
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
        if (!filled)
        {
            float inner = Mathf.Max(0, r - thickness);
            prev = center + new Vector3(inner, 0, 0);
            for (int i = 1; i <= seg; i++)
            {
                float t = (float)i / seg * Mathf.PI * 2f;
                Vector3 p = center + new Vector3(Mathf.Cos(t) * inner, Mathf.Sin(t) * inner, 0);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
        else if (drawOutline && thickness > 0f)
        {
            float outer = r + thickness;
            prev = center + new Vector3(outer, 0, 0);
            for (int i = 1; i <= seg; i++)
            {
                float t = (float)i / seg * Mathf.PI * 2f;
                Vector3 p = center + new Vector3(Mathf.Cos(t) * outer, Mathf.Sin(t) * outer, 0);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
