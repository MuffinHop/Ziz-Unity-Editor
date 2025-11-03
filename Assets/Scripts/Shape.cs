using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base class for all procedural shapes translated from gl_shapes.* style API.
/// Provides a common mesh-generation pipeline & editor-time gizmo drawing.
/// Now generates .act files for C engine compatibility.
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

    [Header("Export Settings")]
    [Tooltip("Base filename for generated .rat and .act files (auto-generated from transform name if empty).")]
    public string baseFilename = "";
    
    [Tooltip("Automatically generate .rat and .act files when the shape changes.")]
    public bool autoExportForCEngine = true;

    protected Mesh mesh; // cached mesh instance
    static Material _defaultVertexColorMat;
    private string lastTransformName = "";

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
        {
            Rebuild();
            
            // Auto-export for C engine if enabled
            if (autoExportForCEngine)
            {
                UpdateBaseFilename();
                ExportForCEngine();
            }
        }
    }
    
    private void UpdateBaseFilename()
    {
        // Auto-generate filename from transform name if not set
        if (string.IsNullOrEmpty(baseFilename) || transform.name != lastTransformName)
        {
            string cleanName = System.Text.RegularExpressions.Regex.Replace(transform.name, @"[<>:""/\\|?*]", "_");
            baseFilename = cleanName;
            lastTransformName = transform.name;
        }
    }
    
    /// <summary>
    /// Export this shape as .rat and .act files for the C engine.
    /// Creates a single-frame animation with the current mesh state.
    /// </summary>
    public void ExportForCEngine()
    {
        if (!generateRuntimeMesh || mesh == null) return;
        
        UpdateBaseFilename();
        
        try
        {
            // Get current mesh data
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var uvs = mesh.uv.Length > 0 ? mesh.uv : new Vector2[vertices.Length];
            var colors = mesh.colors.Length > 0 ? mesh.colors : new Color[vertices.Length];
            
            // Fill in defaults if needed
            if (uvs.Length != vertices.Length)
            {
                uvs = new Vector2[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    uvs[i] = new Vector2(0.5f, 0.5f); // Default UV
                }
            }
            
            if (colors.Length != vertices.Length)
            {
                colors = new Color[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    colors[i] = color; // Use shape's base color
                }
            }
            
            // Convert triangles to ushort indices
            var indices = new ushort[triangles.Length];
            for (int i = 0; i < triangles.Length; i++)
            {
                indices[i] = (ushort)triangles[i];
            }
            
            // Create single-frame animation
            var frames = new List<Vector3[]> { vertices };
            
            // Generate texture filename
            string textureFilename = $"assets/{baseFilename}.png";
            string meshDataFilename = $"{baseFilename}.ratmesh";
            
            // Compress animation data
            var compressed = Rat.CommandLine.GLBToRAT.CompressFrames(
                frames, indices, uvs, colors, textureFilename, meshDataFilename);
            
            // Write RAT file with size splitting
            string ratFilePath = $"GeneratedData/{baseFilename}.rat";
            var createdRatFiles = Rat.Tool.WriteRatFileWithSizeSplitting(baseFilename, compressed, 64);
            
            // Create Actor animation data for the .act file
            var actorData = new ActorAnimationData();
            actorData.framerate = 30.0f; // Static shape, framerate doesn't matter
            actorData.ratFilePaths.AddRange(createdRatFiles.ConvertAll(path => System.IO.Path.GetFileName(path)));
            
            // Create single transform keyframe for the shape
            var transform = new ActorTransformFloat
            {
                position = this.transform.position,
                rotation = this.transform.eulerAngles,
                scale = this.transform.localScale,
                rat_file_index = 0,
                rat_local_frame = 0
            };
            actorData.transforms.Add(transform);
            
            // Save .act file
            string actFilePath = $"GeneratedData/{baseFilename}.act";
            Actor.SaveActorData(actFilePath, actorData);
            
            Debug.Log($"Shape '{name}': Exported to .act file system:");
            Debug.Log($"  - RAT files: {string.Join(", ", createdRatFiles)}");
            Debug.Log($"  - ACT file: {actFilePath}");
            Debug.Log($"  - Texture: {textureFilename}");
            
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Shape '{name}': Failed to export for C engine: {e.Message}");
        }
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
