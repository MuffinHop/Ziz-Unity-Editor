using UnityEngine;

/// <summary>
/// Base class for all procedural shapes translated from gl_shapes.* style API.
/// Provides a common mesh-generation pipeline & editor-time gizmo drawing.
/// </summary>
public abstract class Shape : MonoBehaviour
{
    // Stroke alignment modes for shapes that support an outline ring/quad band.
    public enum StrokeAlignment { Outer, Center, Inner }

    [Header("Common Shape Settings")] 
    public Color color = Color.white;
    [Tooltip("Color for outline/stroke when a shape supports drawOutline.")]
    public Color outlineColor = Color.black;
    [Tooltip("If true, outline uses outlineColor instead of base color.")]
    public bool useOutlineColor = true;
    [Tooltip("How stroke thickness is applied when a shape supports an outline: Outer = expand outward only; Center = half inward / half outward; Inner = inward only.")]
    public StrokeAlignment strokeAlignment = StrokeAlignment.Outer;
    [Tooltip("Generate/refresh mesh automatically when a value changes.")]
    public bool autoRebuild = true;
    [Tooltip("If true a MeshRenderer / MeshFilter will be created and driven by this component.")]
    public bool generateRuntimeMesh = true;
    [Tooltip("Z offset applied to stroke/outline vertices to reduce z-fighting.")]
    public float strokeZOffset = 0.001f;
    [Tooltip("If true, assigns a Sprite/Default (vertex color) material automatically when none present.")]
    public bool autoAssignVertexColorMaterial = true;

    [Header("Debug / Visibility")]
    [Tooltip("If true, strokes/outline will use a high-contrast debug color to make thickness obvious.")]
    public bool debugForceHighContrast = false;
    [Tooltip("Global multiplier applied to stroke/outline thickness when generating meshes (for quick exaggeration). 1 = no change.")]
    public float debugStrokeMultiplier = 1f;

    protected Mesh mesh; // cached mesh instance
    static Material _defaultVertexColorMat;

    /// <summary> Called to (re)build the shape's mesh data. Implement per shape. </summary>
    protected abstract void BuildMesh(Mesh target);

    /// <summary> Optionally draw editor gizmos (Handles drawn in custom editors). </summary>
    protected virtual void DrawGizmos() { }

    /// <summary> Public manual rebuild trigger. </summary>
    public void Rebuild()
    {
        if (!generateRuntimeMesh) return;
        EnsureComponents();
        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.name = GetType().Name + "_Mesh";
            mesh.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;
        }
        mesh.Clear();
        BuildMesh(mesh);
        mesh.RecalculateBounds();

        // Ensure we have a vertex-color capable material so stroke vs fill colors are visible.
        if (autoAssignVertexColorMaterial && TryGetComponent(out MeshRenderer mr))
        {
            if (_defaultVertexColorMat == null)
            {
                // Sprite/Default supports vertex colors and is built-in.
                Shader s = Shader.Find("Sprites/Default");
                if (s != null)
                {
                    _defaultVertexColorMat = new Material(s) { name = "ShapeVertexColorMat" };
                }
            }
            if (_defaultVertexColorMat != null)
            {
                int subCount = mesh.subMeshCount > 0 ? mesh.subMeshCount : 1;
                var current = mr.sharedMaterials;
                bool needsAssign = current == null || current.Length < subCount || System.Array.Exists(current, m => m == null);
                if (needsAssign)
                {
                    Material[] mats = new Material[subCount];
                    for (int i = 0; i < subCount; i++) mats[i] = _defaultVertexColorMat;
                    mr.sharedMaterials = mats;
                }
            }
        }
    }

    protected void EnsureComponents()
    {
        if (!generateRuntimeMesh) return;
        if (!TryGetComponent(out MeshFilter mf)) mf = gameObject.AddComponent<MeshFilter>();
        if (!TryGetComponent(out MeshRenderer mr)) mr = gameObject.AddComponent<MeshRenderer>();
        if (mesh != null && mf.sharedMesh != mesh) mf.sharedMesh = mesh;
    }

    /// <summary>Apply debug stroke multiplier (never returns less than 0).</summary>
    protected float ScaleThickness(float t) => Mathf.Max(0f, t * (debugStrokeMultiplier <= 0f ? 1f : debugStrokeMultiplier));

    /// <summary>Return stroke color, optionally overridden for debug visibility.</summary>
    protected Color GetEffectiveStrokeColor(Color baseStroke)
    {
        if (!debugForceHighContrast) return baseStroke;
        // Pick a vivid alternating palette ignoring original alpha for clarity.
        return baseStroke.grayscale > 0.5f ? new Color(1f, 0f, 1f, 1f) : new Color(1f, 1f, 0f, 1f); // magenta or yellow
    }

    protected virtual void OnValidate()
    {
        if (autoRebuild && Application.isEditor && !Application.isPlaying)
            Rebuild();
    }

    protected virtual void Reset()
    {
        Rebuild();
    }

    protected virtual void OnDrawGizmos()
    {
        // Fallback gizmo in case no custom editor is present.
        Gizmos.color = color;
        DrawGizmos();
    }
}
