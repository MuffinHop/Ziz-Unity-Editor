using System.Collections.Generic;
using System.Linq; // Add this
using UnityEngine;

/// <summary>
/// Base class for procedural shapes. Handles mesh generation and optional export.
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

    [Header("Animation Recording")]
    [Tooltip("Enable animation recording for this shape")]
    public bool recordAnimation = false;
    [Tooltip("Framerate for animation recording")]
    public float animationFramerate = 30f;

    // Animation recording data
    private bool isRecordingAnimation = false;
    private float recordingStartTime;
    private List<Mesh> animationFrames = new List<Mesh>();
    private List<TransformData> frameTransforms = new List<TransformData>();
    private List<float> frameTimestamps = new List<float>();

    /// <summary>
    /// Stores transform data for a single frame
    /// </summary>
    [System.Serializable]
    private struct TransformData
    {
        public Vector3 position;
        public Vector3 rotation; // Euler angles
        public Vector3 scale;
    }

    public Mesh mesh; // Changed from 'protected' to 'public'
    static Material _defaultVertexColorMat;
    private string lastTransformName = "";
    private string baseFilename = ""; // Add this field

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
        }
    }
    
    public void Update()
    {
        if (isRecordingAnimation && Application.isPlaying)
        {
            RecordAnimationFrame();
        }
    }

    /// <summary>
    /// Start recording animation frames for this shape
    /// </summary>
    public void StartAnimationRecording()
    {
        if (isRecordingAnimation)
        {
        Debug.LogWarning($"Shape {name} - animation recording already in progress");
            return;
        }

        isRecordingAnimation = true;
        recordingStartTime = Time.time;
        animationFrames.Clear();
        frameTransforms.Clear();
        frameTimestamps.Clear();

    Debug.Log($"Shape {name} - started recording at {animationFramerate} FPS");
    }

    /// <summary>
    /// Stop recording animation and export to RAT/ACT files
    /// </summary>
    public void StopAnimationRecording()
    {
        if (!isRecordingAnimation)
        {
            Debug.LogWarning($"Shape {name} - no animation recording in progress");
            return;
        }

        isRecordingAnimation = false;

        if (animationFrames.Count == 0)
        {
            Debug.LogWarning($"Shape {name} - no animation frames recorded");
            return;
        }

    Debug.Log($"Shape {name} - stopped recording ({animationFrames.Count} frames)");

        // Export the recorded animation
        ExportRecordedAnimation();
    }

    /// <summary>
    /// Record a single animation frame
    /// </summary>
    private void RecordAnimationFrame()
    {
        float currentTime = Time.time - recordingStartTime;
        float frameInterval = 1f / animationFramerate;

        if (frameTimestamps.Count == 0 || currentTime >= frameTimestamps[frameTimestamps.Count - 1] + frameInterval)
        {
            Mesh frameMesh = Object.Instantiate(mesh);
            frameMesh.name = $"{mesh.name}_frame_{animationFrames.Count}";

            TransformData frameTransform = new TransformData
            {
                position = transform.position,
                rotation = transform.eulerAngles,
                scale = transform.lossyScale
            };

            animationFrames.Add(frameMesh);
            frameTransforms.Add(frameTransform);
            frameTimestamps.Add(currentTime);

            Debug.Log($"Shape {name} - recorded frame {animationFrames.Count} at {currentTime:F3}s");
        }
    }

    /// <summary>
    /// Export the recorded animation frames to RAT and ACT files with transforms baked into vertices
    /// </summary>
    private void ExportRecordedAnimation()
    {
        if (animationFrames.Count == 0) return;

        UpdateBaseFilename();

        // Convert recorded transforms to ActorTransformFloat format
        var actorTransforms = frameTransforms.Select((t, index) => new Rat.ActorTransformFloat
        {
            position = t.position,
            rotation = t.rotation,
            scale = t.scale,
            rat_file_index = 0,
            rat_local_frame = (uint)index
        }).ToList();

        try
        {
            // Use unified export API with transforms to be baked into vertices
            Rat.Tool.ExportAnimation(
                baseFilename,
                animationFrames.Select(m => m.vertices).ToList(),
                animationFrames[0],
                null,
                null,
                animationFramerate,
                $"assets/{baseFilename}.png",
                64, // maxFileSizeKB
                Rat.ActorRenderingMode.TextureAndVertexColours,  // Changed from TextureWithDirectionalLight
                actorTransforms  // Pass the recorded transforms
            );
            Debug.Log($"Shape: Animation exported with {actorTransforms.Count} transforms baked into vertices");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Shape: Export failed - {e.Message}");
        }

        foreach (var frameMesh in animationFrames)
            Object.Destroy(frameMesh);
        animationFrames.Clear();
        frameTransforms.Clear();
    }

    /// <summary>
    /// Updates the base filename from transform name
    /// </summary>
    private void UpdateBaseFilename()
    {
        string cleanName = System.Text.RegularExpressions.Regex.Replace(transform.name, @"[<>:""/\\|?*]", "_");
        baseFilename = cleanName;
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
