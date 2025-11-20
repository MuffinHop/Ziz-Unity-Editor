using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Rat; // Use the Rat namespace which contains all the compression logic
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// DEPRECATED: Use AnimationRecorder instead.
/// This component will be removed in a future version.
/// </summary>
public class RatRecorder : MonoBehaviour
{
    [Header("Target Mesh")]
    [Tooltip("The SkinnedMeshRenderer to record animation from.")]
    public SkinnedMeshRenderer targetSkinnedMeshRenderer;

    [Tooltip("The MeshFilter to record animation from (if no SkinnedMeshRenderer).")]
    public MeshFilter targetMeshFilter;

    [Header("Recording Controls")]
    [Tooltip("Duration in seconds to record the animation.")]
    public float recordingDuration = 5.0f;

    [Tooltip("Frames per second to capture during recording.")]
    public float captureFramerate = 30.0f;

    [Tooltip("Scale factor to apply to the recorded animation (1.0 = original size, 0.5 = half size, 2.0 = double size).")]
    public float recordingScale = 1.0f;

    [Header("Capture Options")]
    [Tooltip("Capture UV coordinates from the current mesh state (uses current mesh UVs instead of source mesh UVs).")]
    public bool captureCurrentUVs = false;

    [Tooltip("Capture vertex colors from the current mesh state (uses current mesh colors instead of source mesh colors).")]
    public bool captureCurrentColors = false;

    [Header("Compression Options")]
    [Tooltip("Enable compression accuracy control to trade off precision for better compression ratios.")]
    public bool enableCompressionControl = false;

    [Tooltip("Maximum bits allowed for X-axis deltas. Lower values = better compression but less accuracy.\n" +
             "1 bit = ±0 (no movement), 2 bits = ±1, 3 bits = ±3, 4 bits = ±7, 5 bits = ±15, etc.")]
    [Range(1, 8)]
    public int maxBitsX = 8;

    [Tooltip("Maximum bits allowed for Y-axis deltas. Lower values = better compression but less accuracy.\n" +
             "1 bit = ±0 (no movement), 2 bits = ±1, 3 bits = ±3, 4 bits = ±7, 5 bits = ±15, etc.")]
    [Range(1, 8)]
    public int maxBitsY = 8;

    [Tooltip("Maximum bits allowed for Z-axis deltas. Lower values = better compression but less accuracy.\n" +
             "1 bit = ±0 (no movement), 2 bits = ±1, 3 bits = ±3, 4 bits = ±7, 5 bits = ±15, etc.")]
    [Range(1, 8)]
    public int maxBitsZ = 8;

    [Header("File Output")]
    [Tooltip("The base filename for the saved .rat animation files.")]
    public string baseFilename = "recorded_animation";

    [Tooltip("The filename for the texture associated with this animation (optional).")]
    public string textureFilename = "";

    [Tooltip("Maximum file size in KB before splitting into multiple parts (default: 64KB). Set to 0 for no splitting.")]
    [Range(16, 1024)]
    public int maxFileSizeKB = 64;

    [Tooltip("Preserve the first frame of the animation in its raw, uncompressed format.")]
    public bool preserveFirstFrame = false;

    [Tooltip("If true, exports .rat files. If false, only records in memory.")]
    public bool exportBinary = true;

    public bool IsRecording => _isRecording;

    private bool _isRecording = false;
    private bool _recordingComplete = false; // Track if recording finished (but not yet saved)
    private float _recordingStartTime;
    private float _lastCaptureTime;
    private List<Vector3[]> _recordedFrames;
    private List<Rat.ActorTransformFloat> _recordedTransforms; // per-frame triples
    private Vector2[] _capturedUVs; // Static UV data captured once
    private Color[] _capturedColors; // Static color data captured once
    private Mesh _tempMesh;
    private Mesh _sourceMesh;
    private Vector3 _modelCenter; // Center point of the model for delta calculations

#if UNITY_EDITOR
    void OnEnable()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
    }

    void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode && _recordingComplete)
        {
            if (!exportBinary)
            {
                Debug.Log("RatRecorder - exportBinary is false, skipping export.");
                return;
            }
            // Save the recording when exiting play mode
            SaveRecording(true);
        }
    }
#endif

    /// <summary>
    /// Initializes the recorder, finds the target mesh, and prepares for recording.
    /// </summary>
    void Start()
    {
        if (targetSkinnedMeshRenderer != null)
        {
            _sourceMesh = targetSkinnedMeshRenderer.sharedMesh;
            _tempMesh = new Mesh(); // Create a temporary mesh to bake skinned mesh data into.
            
            // Pre-initialize the temporary mesh with source mesh data to prevent warnings
            if (_sourceMesh != null)
            {
                // Copy the basic mesh structure (triangles, UVs, etc.) to prevent Unity warnings
                _tempMesh.vertices = _sourceMesh.vertices;
                _tempMesh.triangles = _sourceMesh.triangles;
                if (_sourceMesh.uv != null && _sourceMesh.uv.Length > 0)
                    _tempMesh.uv = _sourceMesh.uv;
                if (_sourceMesh.normals != null && _sourceMesh.normals.Length > 0)
                    _tempMesh.normals = _sourceMesh.normals;
                
                Debug.Log($"RatRecorder: Pre-initialized temporary mesh with {_tempMesh.vertices.Length} vertices and {_tempMesh.triangles.Length} triangle indices");
            }
        }
        else if (targetMeshFilter != null)
        {
            _sourceMesh = targetMeshFilter.mesh;
            _tempMesh = _sourceMesh; // Can use the mesh directly if it's not skinned.
        }
        else
        {
            Debug.LogError("RatRecorder: No target SkinnedMeshRenderer or MeshFilter assigned. Disabling component.");
            enabled = false;
            return;
        }
    _recordedFrames = new List<Vector3[]>();
    _recordedTransforms = new List<Rat.ActorTransformFloat>();
        
        // Validate mesh compatibility with RAT format before starting
        if (!ValidateMeshCompatibility())
        {
            Debug.LogError("RatRecorder: Mesh is incompatible with RAT format. Disabling component.");
            enabled = false;
            return;
        }
        
        // Calculate the model center for delta calculations
        CalculateModelCenter();
        
        // Start recording automatically
        StartRecording();
        
        Debug.Log($"RatRecorder initialized. Recording for {recordingDuration} seconds with current UVs: {captureCurrentUVs}, current colors: {captureCurrentColors}...");
    }

    /// <summary>
    /// Checks if the recording duration has elapsed and stops recording if so.
    /// </summary>
    void Update()
    {
        // Only auto-stop if we are running in standalone mode (not controlled by Actor)
        // If controlled by Actor, Actor will call StopRecording() when it's done.
        // We detect standalone mode by checking if we have an Actor component on the same GameObject.
        bool hasActor = GetComponent<Actor>() != null;
        
        if (!hasActor && _isRecording && Time.time - _recordingStartTime >= recordingDuration)
        {
            StopRecording();
        }
    }

    /// <summary>
    /// Captures the mesh's vertex positions in LateUpdate to ensure animations have been applied for the frame.
    /// </summary>
    void LateUpdate()
    {
        if (_isRecording)
        {
            // Check if enough time has passed since the last capture based on the target framerate
            float captureInterval = 1.0f / captureFramerate;
            if (Time.time - _lastCaptureTime >= captureInterval)
            {
                CaptureFrame();
                _lastCaptureTime = Time.time;
            }
        }
    }

    /// <summary>
    /// Starts the recording process.
    /// </summary>
    public void StartRecording()
    {
        _recordedFrames.Clear();
        _recordingStartTime = Time.time;
        _lastCaptureTime = Time.time;
        _isRecording = true;
        
        // Capture static UV and color data once at the start
        CaptureStaticMeshData();
        
        Debug.Log($"Recording started at {captureFramerate} FPS...");
    }

    /// <summary>
    /// Public alias to StartRecording() for external callers.
    /// </summary>
    public void BeginRecording()
    {
        StartRecording();
    }

    /// <summary>
    /// Captures the current state of the vertices only (UVs and colors are captured once at start).
    /// </summary>
    private void CaptureFrame()
    {
        Vector3[] frameVertices = null;
        
        if (targetSkinnedMeshRenderer != null)
        {
            // For skinned meshes, we need to bake the current pose into our temporary mesh.
            targetSkinnedMeshRenderer.BakeMesh(_tempMesh);
            frameVertices = _tempMesh.vertices;
            
            // Debug: Check if the baked mesh has valid triangle data (only for first frame to avoid spam)
            if (_recordedFrames.Count == 0)
            {
                var tempTriangles = _tempMesh.triangles;
                if (tempTriangles != null && tempTriangles.Length % 3 != 0)
                {
                    Debug.LogWarning($"RatRecorder: Baked mesh has invalid triangle count ({tempTriangles.Length}). This might cause rendering warnings. " +
                                   $"Source mesh triangles: {_sourceMesh.triangles?.Length ?? 0}");
                    
                    // Try to fix the temporary mesh by copying triangle data from source mesh
                    if (_sourceMesh.triangles != null && _sourceMesh.triangles.Length % 3 == 0)
                    {
                        _tempMesh.triangles = _sourceMesh.triangles;
                        Debug.Log($"RatRecorder: Copied triangle data from source mesh to fix baked mesh.");
                    }
                }
                else if (tempTriangles != null)
                {
                    Debug.Log($"RatRecorder: Baked mesh triangle validation passed. Triangle indices: {tempTriangles.Length}, Triangles: {tempTriangles.Length / 3}");
                }
                else
                {
                    // If tempMesh has no triangles, copy from source mesh
                    if (_sourceMesh.triangles != null)
                    {
                        _tempMesh.triangles = _sourceMesh.triangles;
                        Debug.Log($"RatRecorder: Copied triangle data from source mesh to temporary mesh (was null).");
                    }
                }
            }
        }
        else if (targetMeshFilter != null)
        {
            // For regular meshes, we can just grab the vertices directly.
            frameVertices = targetMeshFilter.mesh.vertices;
        }
        
        // Debug: Log capture information for first few frames
        if (_recordedFrames.Count < 3)
        {
            Debug.Log($"Frame {_recordedFrames.Count}: Captured {frameVertices?.Length ?? 0} vertices");
        }
        
        // Debug: Log original vertex range for first frame
        if (_recordedFrames.Count == 0 && frameVertices != null && frameVertices.Length > 0)
        {
            var minOrig = frameVertices[0];
            var maxOrig = frameVertices[0];
            foreach (var v in frameVertices)
            {
                if (v.x < minOrig.x) minOrig.x = v.x;
                if (v.y < minOrig.y) minOrig.y = v.y;
                if (v.z < minOrig.z) minOrig.z = v.z;
                if (v.x > maxOrig.x) maxOrig.x = v.x;
                if (v.y > maxOrig.y) maxOrig.y = v.y;
                if (v.z > maxOrig.z) maxOrig.z = v.z;
            }
            Debug.Log($"Original vertex bounds: Min({minOrig.x:F3}, {minOrig.y:F3}, {minOrig.z:F3}) Max({maxOrig.x:F3}, {maxOrig.y:F3}, {maxOrig.z:F3})");
        }
        
        // Apply scaling if needed
        if (recordingScale != 1.0f && frameVertices != null)
        {
            Vector3[] scaledVertices = new Vector3[frameVertices.Length];
            for (int i = 0; i < frameVertices.Length; i++)
            {
                // Calculate delta from model center, then apply scaling
                Vector3 deltaFromCenter = frameVertices[i] - _modelCenter;
                scaledVertices[i] = (_modelCenter + deltaFromCenter * recordingScale);
            }
            
            // Debug: Log scaled vertex range for first frame
            if (_recordedFrames.Count == 0)
            {
                var minScaled = scaledVertices[0];
                var maxScaled = scaledVertices[0];
                foreach (var v in scaledVertices)
                {
                    if (v.x < minScaled.x) minScaled.x = v.x;
                    if (v.y < minScaled.y) minScaled.y = v.y;
                    if (v.z < minScaled.z) minScaled.z = v.z;
                    if (v.x > maxScaled.x) maxScaled.x = v.x;
                    if (v.y > maxScaled.y) maxScaled.y = v.y;
                    if (v.z > maxScaled.z) maxScaled.z = v.z;
                }
                Debug.Log($"Scaled vertex bounds (scale {recordingScale}, center {_modelCenter}): Min({minScaled.x:F3}, {minScaled.y:F3}, {minScaled.z:F3}) Max({maxScaled.x:F3}, {maxScaled.y:F3}, {maxScaled.z:F3})");
            }
            
            _recordedFrames.Add(scaledVertices);
            // Capture the transform at the time of this frame so ExportAnimation can bake it into vertices
            _recordedTransforms.Add(new Rat.ActorTransformFloat
            {
                position = transform.position,
                rotation = transform.eulerAngles,
                scale = transform.lossyScale,
                rat_file_index = 0,
                rat_local_frame = (uint)(_recordedFrames.Count - 1)
            });
        }
        else if (frameVertices != null)
        {
            // Store full local-space vertices (no scaling requested)
            // Debug: Log vertex range for first frame
            if (_recordedFrames.Count == 0)
            {
                var minV = frameVertices[0];
                var maxV = frameVertices[0];
                foreach (var v in frameVertices)
                {
                    if (v.x < minV.x) minV.x = v.x;
                    if (v.y < minV.y) minV.y = v.y;
                    if (v.z < minV.z) minV.z = v.z;
                    if (v.x > maxV.x) maxV.x = v.x;
                    if (v.y > maxV.y) maxV.y = v.y;
                    if (v.z > maxV.z) maxV.z = v.z;
                }
                Debug.Log($"Vertex bounds: Min({minV.x:F3}, {minV.y:F3}, {minV.z:F3}) Max({maxV.x:F3}, {maxV.y:F3}, {maxV.z:F3})");
            }

            _recordedFrames.Add(frameVertices);
            // Capture the transform at the time of this frame so ExportAnimation can bake it into vertices
            _recordedTransforms.Add(new Rat.ActorTransformFloat
            {
                position = transform.position,
                rotation = transform.eulerAngles,
                scale = transform.lossyScale,
                rat_file_index = 0,
                rat_local_frame = (uint)(_recordedFrames.Count - 1)
            });
        }
    }

    /// <summary>
    /// Stops recording (but doesn't save - saving happens when exiting play mode).
    /// </summary>
    public void StopRecording()
    {
        _isRecording = false;
        _recordingComplete = true;
        
        if (_recordedFrames.Count == 0)
        {
            Debug.LogWarning("Recording stopped, but no frames were captured. Nothing to save.");
            return;
        }

        Debug.Log($"Recording stopped. Captured {_recordedFrames.Count} frames at {recordingScale}x scale.");

        // Save the recording immediately (not only on exiting play mode) so callers can sync RAT/ACT
        try
        {
            SaveRecording();
            _recordingComplete = false;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"RatRecorder: Immediate save failed - will try again on exit: {e.Message}");
        }
    }

    /// <summary>
    /// Public alias to StopRecording() for external callers.
    /// </summary>
    public void EndRecording()
    {
        StopRecording();
    }

    /// <summary>
    /// Saves the recorded animation to .rat and .act files with transforms baked into vertices.
    /// This is called when exiting play mode.
    /// </summary>
    private void SaveRecording(bool skipValidation = false)
    {
        if (_recordedFrames.Count == 0) return;

        Debug.Log($"RatRecorder: Saving {_recordedFrames.Count} frames with baked transforms...");

        try
        {
            // Export recorded local-space frames and the per-frame transforms
            List<UnityEngine.Vector3[]> framesToExport = new List<UnityEngine.Vector3[]>(_recordedFrames);
            List<Rat.ActorTransformFloat> frameTransforms = new List<Rat.ActorTransformFloat>(_recordedTransforms);

            if (framesToExport.Count != frameTransforms.Count)
            {
                UnityEngine.Debug.LogWarning($"RatRecorder: Frame count ({framesToExport.Count}) does not match transform count ({frameTransforms.Count}). ExportAnimation expects them to be equal. Will proceed with available transforms.");
            }
            
            Debug.Log("RatRecorder: Starting export...");
            Rat.Tool.ExportAnimation(
                baseFilename,
                framesToExport,
                _sourceMesh,
                _capturedUVs,
                _capturedColors,
                captureFramerate,
                textureFilename,
                maxFileSizeKB,
                Rat.ActorRenderingMode.TextureWithDirectionalLight,
                frameTransforms,  // Pass transforms
                skipValidation
            );
            Debug.Log("RatRecorder: Export finished");
            
            Debug.Log($"RatRecorder: Export complete - all transforms baked into vertices");

            // If this GameObject has an Actor component, update its RAT file references
            try
            {
                var genPath = System.IO.Path.Combine(Application.dataPath.Replace("Assets", ""), "GeneratedData");
                var files = new List<string>(System.IO.Directory.GetFiles(genPath, baseFilename + "*.rat"));
                for (int i = 0; i < files.Count; i++)
                    files[i] = System.IO.Path.GetFileName(files[i]);

                var actor = GetComponent<Actor>();
                if (actor != null && files.Count > 0)
                {
                    UnityEngine.Debug.Log($"RatRecorder - updating actor {name} RAT references ({files.Count} files)");
                    actor.SetRatFileReferences(files, new List<uint>());
                }
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning($"RatRecorder: Could not update Actor RAT references automatically: {e.Message}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"RatRecorder: Export failed - {e.Message}\n{e}");
        }

    _recordedFrames.Clear();
    _recordedTransforms.Clear();
    }

    /// <summary>
    /// Cleans up the temporary mesh object when the component is destroyed.
    /// </summary>
    void OnDestroy()
    {
        if (_tempMesh != null && _sourceMesh != _tempMesh)
        {
            Destroy(_tempMesh);
        }
    }

    /// <summary>
    /// Captures static UV and color data once at the start of recording.
    /// </summary>
    private void CaptureStaticMeshData()
    {
        Vector2[] uvs = null;
        Color[] colors = null;
        
        if (targetSkinnedMeshRenderer != null)
        {
            if (captureCurrentUVs || captureCurrentColors)
            {
                // For skinned meshes, bake current state to get UVs/colors
                targetSkinnedMeshRenderer.BakeMesh(_tempMesh);
                if (captureCurrentUVs) uvs = _tempMesh.uv;
                if (captureCurrentColors) colors = _tempMesh.colors;
                
                // Validate the temporary mesh after baking for static data capture
                var tempTriangles = _tempMesh.triangles;
                if (tempTriangles != null && tempTriangles.Length % 3 != 0)
                {
                    Debug.LogWarning($"RatRecorder: CaptureStaticMeshData - Baked mesh has invalid triangle count ({tempTriangles.Length}) after baking for UV/color capture. " +
                                   $"Fixing by copying from source mesh.");
                    _tempMesh.triangles = _sourceMesh.triangles;
                }
            }
        }
        else if (targetMeshFilter != null)
        {
            // For regular meshes, get UVs/colors directly
            if (captureCurrentUVs) uvs = targetMeshFilter.mesh.uv;
            if (captureCurrentColors) colors = targetMeshFilter.mesh.colors;
        }
        
        // Store captured data
        _capturedUVs = uvs;
        _capturedColors = colors;
        
        // Debug logging
        int uvCount = _capturedUVs?.Length ?? 0;
        int colorCount = _capturedColors?.Length ?? 0;
        string uvSource = captureCurrentUVs ? "current mesh state" : "not captured";
        string colorSource = captureCurrentColors ? "current mesh state" : "not captured";
        Debug.Log($"Captured static mesh data: {uvCount} UVs ({uvSource}), {colorCount} colors ({colorSource})");
    }

    /// <summary>
    /// Calculates the center point of the model based on its bounding box.
    /// This center will be used as the origin for delta calculations.
    /// </summary>
    private void CalculateModelCenter()
    {
        if (_sourceMesh == null)
        {
            Debug.LogError("RatRecorder: Cannot calculate model center - source mesh is null.");
            _modelCenter = Vector3.zero;
            return;
        }

        // Get the mesh bounds
        Bounds bounds = _sourceMesh.bounds;
        _modelCenter = bounds.center;
        
        Debug.Log($"RatRecorder: Model center calculated as {_modelCenter} (bounds: min={bounds.min}, max={bounds.max}, size={bounds.size})");
        
        // Alternative method: calculate center from actual vertices (more accurate for non-centered meshes)
        Vector3[] vertices = _sourceMesh.vertices;
        if (vertices != null && vertices.Length > 0)
        {
            Vector3 vertexCenter = Vector3.zero;
            foreach (var vertex in vertices)
            {
                vertexCenter += vertex;
            }
            vertexCenter /= vertices.Length;
            
            Debug.Log($"RatRecorder: Alternative vertex-average center: {vertexCenter} (difference from bounds center: {Vector3.Distance(bounds.center, vertexCenter):F4})");
            
            // Use vertex-average center if it's significantly different from bounds center
            if (Vector3.Distance(bounds.center, vertexCenter) > 0.001f)
            {
                Debug.Log("RatRecorder: Using vertex-average center for better accuracy");
                _modelCenter = vertexCenter;
            }
        }
    }

    /// <summary>
    /// Validates that the source mesh is compatible with the RAT format limitations.
    /// </summary>
    /// <returns>True if the mesh is compatible, false otherwise</returns>
    private bool ValidateMeshCompatibility()
    {
        if (_sourceMesh == null)
        {
            Debug.LogError("RatRecorder: Source mesh is null.");
            return false;
        }

        // Check vertex count limit (ushort limitation)
        int vertexCount = _sourceMesh.vertexCount;
        if (vertexCount > 65535)
        {
            Debug.LogError($"RatRecorder: Mesh has {vertexCount} vertices, but RAT format only supports up to 65,535 vertices due to ushort index limitation. " +
                          $"Consider using a mesh with fewer vertices, splitting the mesh into multiple objects, or implementing uint32 indices in the RAT format.");
            return false;
        }

        // Check triangle indices
        var triangles = _sourceMesh.triangles;
        if (triangles == null)
        {
            Debug.LogError("RatRecorder: Mesh triangles array is null. Invalid mesh data.");
            return false;
        }
        
        // Debug: Log detailed mesh information
        Debug.Log($"RatRecorder: Mesh analysis - Vertices: {vertexCount}, Triangle indices array length: {triangles.Length}, Expected triangles: {triangles.Length / 3}");
        
        // Additional validation: ensure triangle data makes sense
        if (triangles.Length == 0)
        {
            Debug.LogWarning($"RatRecorder: Mesh has no triangles. This is unusual but not necessarily an error if you're working with point clouds or line meshes.");
        }
        
        if (triangles.Length % 3 != 0)
        {
            Debug.LogError($"RatRecorder: Triangle indices count ({triangles.Length}) is not divisible by 3. Each triangle requires exactly 3 indices. " +
                          $"This means the mesh has malformed triangle data. Triangle count should be {triangles.Length / 3} but the remainder is {triangles.Length % 3}. " +
                          $"This could be caused by importing issues, mesh corruption, or a bug in mesh generation.");
            
            // Try to provide helpful debugging information
            if (triangles.Length == vertexCount)
            {
                Debug.LogError($"RatRecorder: DIAGNOSTIC - The triangle indices count ({triangles.Length}) equals the vertex count ({vertexCount}). " +
                              $"This suggests the mesh data might be corrupted or there's confusion between vertices and triangle indices in the mesh importer/generator.");
            }
            
            return false;
        }
        for (int i = 0; i < triangles.Length; i++)
        {
            if (triangles[i] > 65535)
            {
                Debug.LogError($"RatRecorder: Triangle index {triangles[i]} at position {i} exceeds ushort limit (65535). " +
                              $"This mesh is incompatible with the current RAT format.");
                return false;
            }
            if (triangles[i] < 0)
            {
                Debug.LogError($"RatRecorder: Triangle index {triangles[i]} at position {i} is negative. Invalid mesh data.");
                return false;
            }
            if (triangles[i] >= vertexCount)
            {
                Debug.LogError($"RatRecorder: Triangle index {triangles[i]} at position {i} references vertex {triangles[i]}, but mesh only has {vertexCount} vertices (valid range: 0-{vertexCount - 1}). Invalid mesh topology.");
                return false;
            }
        }

        // Log mesh info if validation passes
        Debug.Log($"RatRecorder: Mesh validation passed. Vertices: {vertexCount}, Triangles: {triangles.Length / 3}, Triangle indices: {triangles.Length}");
        
        // Additional helpful info
        if (vertexCount > 32767) // Half of the ushort limit
        {
            Debug.LogWarning($"RatRecorder: Mesh has {vertexCount} vertices, which is close to the RAT format limit of 65,535. " +
                           $"Consider optimizing the mesh if you encounter issues.");
        }

        return true;
    }

    /// <summary>
    /// Converts delta-based vertex positions back to world-space coordinates.
    /// Useful for debugging or when you need absolute positions.
    /// </summary>
    /// <param name="deltaVertices">Array of delta vertices (relative to model center)</param>
    /// <returns>Array of world-space vertices</returns>
    public Vector3[] ConvertDeltasToWorldSpace(Vector3[] deltaVertices)
    {
        if (deltaVertices == null) return null;
        
        Vector3[] worldVertices = new Vector3[deltaVertices.Length];
        for (int i = 0; i < deltaVertices.Length; i++)
        {
            worldVertices[i] = deltaVertices[i] + _modelCenter;
        }
        return worldVertices;
    }

    /// <summary>
    /// Gets the model center used for delta calculations.
    /// </summary>
    /// <returns>The center point of the model</returns>
    public Vector3 GetModelCenter()
    {
        return _modelCenter;
    }

    /// <summary>
    /// Estimates the file size that would be generated for the current recording.
    /// Call this during recording to get an estimate before saving.
    /// </summary>
    /// <returns>Estimated file size in bytes, or -1 if no recording data available</returns>
    public long EstimateFileSize()
    {
        if (_recordedFrames == null || _recordedFrames.Count == 0 || _sourceMesh == null)
        {
            return -1;
        }
        
        // Calculate static data sizes
        uint headerSize = (uint)Marshal.SizeOf(typeof(Rat.RatHeader));
        uint numVertices = (uint)_sourceMesh.vertexCount;
        uint numIndices = (uint)_sourceMesh.triangles.Length;
        
        uint uvSize = numVertices * (uint)Marshal.SizeOf(typeof(Rat.VertexUV));
        uint colorSize = numVertices * (uint)Marshal.SizeOf(typeof(Rat.VertexColor));
        uint indicesSize = numIndices * sizeof(ushort);
        uint bitWidthsSize = numVertices * 3; // X, Y, Z bit widths
        uint firstFrameSize = numVertices * (uint)Marshal.SizeOf(typeof(Rat.VertexU8));
        
        // Estimate delta stream size (rough approximation)
        uint estimatedDeltaSize = 0;
        if (_recordedFrames.Count > 1)
        {
            // Assume average of 4 bits per axis per vertex per frame (conservative estimate)
            float avgBitsPerVertex = enableCompressionControl ? 
                (maxBitsX + maxBitsY + maxBitsZ) : 12.0f; // Conservative estimate
            
            uint totalDeltaBits = (uint)(_recordedFrames.Count - 1) * numVertices * (uint)avgBitsPerVertex;
            estimatedDeltaSize = (totalDeltaBits + 31) / 32 * 4; // Round up to 32-bit words
        }
        
        long totalSize = headerSize + uvSize + colorSize + indicesSize + bitWidthsSize + firstFrameSize + estimatedDeltaSize;
        return totalSize;
    }

    /// <summary>
    /// Performs a comprehensive diagnosis of mesh data to help identify issues.
    /// Call this if you're getting unexpected warnings or errors.
    /// </summary>
    public void DiagnoseMesh()
    {
        if (_sourceMesh == null)
        {
            Debug.LogError("RatRecorder: Cannot diagnose - source mesh is null.");
            return;
        }

    Debug.Log("RatRecorder mesh diagnosis:");
        Debug.Log($"Source Mesh: {_sourceMesh.name}");
        Debug.Log($"Vertex Count: {_sourceMesh.vertexCount}");
        Debug.Log($"Triangle Indices Count: {_sourceMesh.triangles?.Length ?? 0}");
        Debug.Log($"Calculated Triangle Count: {(_sourceMesh.triangles?.Length ?? 0) / 3}");
        Debug.Log($"Triangle data valid: {(_sourceMesh.triangles?.Length ?? 0) % 3 == 0}");
        Debug.Log($"Model Center: {_modelCenter}");
        Debug.Log($"Mesh Bounds: {_sourceMesh.bounds}");
        
        if (_sourceMesh.subMeshCount > 1)
        {
            Debug.Log($"Submesh Count: {_sourceMesh.subMeshCount}");
            for (int i = 0; i < _sourceMesh.subMeshCount; i++)
            {
                var subMeshIndices = _sourceMesh.GetTriangles(i);
                Debug.Log($"  Submesh {i}: {subMeshIndices.Length} indices, {subMeshIndices.Length / 3} triangles");
            }
        }
        
        Debug.Log($"UV Count: {_sourceMesh.uv?.Length ?? 0}");
        Debug.Log($"Color Count: {_sourceMesh.colors?.Length ?? 0}");
        Debug.Log($"Normal Count: {_sourceMesh.normals?.Length ?? 0}");
        
        if (targetSkinnedMeshRenderer != null)
        {
            Debug.Log("Skinned mesh renderer info:");
            Debug.Log($"Shared Mesh: {targetSkinnedMeshRenderer.sharedMesh?.name ?? "null"}");
            Debug.Log($"Bone Count: {targetSkinnedMeshRenderer.bones?.Length ?? 0}");
            
            if (_tempMesh != null)
            {
                Debug.Log("Temporary mesh info:");
                Debug.Log($"Temp Mesh Vertex Count: {_tempMesh.vertexCount}");
                Debug.Log($"Temp Mesh Triangle Indices Count: {_tempMesh.triangles?.Length ?? 0}");
                Debug.Log($"Temp Mesh Triangle data valid: {(_tempMesh.triangles?.Length ?? 0) % 3 == 0}");
            }
        }
        
    Debug.Log("End diagnosis");
    }
}
