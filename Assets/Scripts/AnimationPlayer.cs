using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Rat;

/// <summary>
/// Test component to load and play back .act and .rat animation files.
/// Use this to verify that exported animations work correctly.
/// </summary>
[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class AnimationPlayer : MonoBehaviour
{
    [Header("Animation Files")]
    [Tooltip("Path to the .act file to load")]
    public string actFilePath = ""; // Start empty, will be set by AutoExporter
    
    [Tooltip("Auto-play animation when loaded")]
    public bool autoPlay = true;
    
    [Tooltip("Loop the animation")]
    public bool loop = true;
    
    [Tooltip("Playback speed multiplier")]
    public float playbackSpeed = 1f;

    [Header("Debug Options")]
    [Tooltip("Show detailed vertex position logs")]
    public bool debugVertexPositions = false;
    
    [Tooltip("Draw gizmos showing vertex positions")]
    public bool showVertexGizmos = false;
    
    [Tooltip("Maximum vertices to show in gizmos (performance)")]
    public int maxGizmoVertices = 100;

    public ActorAnimationData _actorData; // Made public for testing
    public List<CompressedAnimation> _ratAnimations = new List<CompressedAnimation>();
    private List<DecompressionContext> _decompContexts = new List<DecompressionContext>();
    
    private Mesh _mesh;
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    
    private bool _isPlaying = false;
    private float _currentTime = 0f;
    private int _currentKeyframe = 0;
    
    void Start()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();
        
        // Only try to load if a path is set
        if (!string.IsNullOrEmpty(actFilePath))
        {
            LoadAnimation(actFilePath);
            if (autoPlay) Play();
        }
        else
        {
            Debug.LogWarning("AnimationPlayer: No ACT file path set. Set actFilePath or let AutoExporter configure it.");
        }
    }
    
    void Update()
    {
        if (_isPlaying && _actorData != null && _ratAnimations.Count > 0)
        {
            _currentTime += Time.deltaTime * playbackSpeed;
            
            float frameTime = 1f / _actorData.framerate;
            int newKeyframe = Mathf.FloorToInt(_currentTime / frameTime);
            
            if (newKeyframe != _currentKeyframe)
            {
                var ratAnim = _ratAnimations[0];
                
                if (newKeyframe >= ratAnim.num_frames)
                {
                    if (loop)
                    {
                        newKeyframe = 0;
                        _currentTime = 0f;
                    }
                    else
                    {
                        Stop();
                        return;
                    }
                }
                
                ApplyKeyframe(newKeyframe);
                _currentKeyframe = newKeyframe;
            }
        }
    }
    
    /// <summary>
    /// Load .act and referenced .rat files
    /// </summary>
    public void LoadAnimation(string actPath)
    {
        if (!File.Exists(actPath))
        {
            Debug.LogError($"AnimationPlayer: ACT file not found: {actPath}");
            return;
        }
        
        try
        {
            // Load ACT file
            _actorData = LoadActorData(actPath);
            Debug.Log($"Loaded ACT file at {_actorData.framerate} FPS");
            
            // Load referenced RAT files
            _ratAnimations.Clear();
            _decompContexts.Clear();
            
            string directory = Path.GetDirectoryName(actPath);
            foreach (string ratPath in _actorData.ratFilePaths)
            {
                string fullRatPath = Path.Combine(directory, ratPath);
                if (File.Exists(fullRatPath))
                {
                    var ratAnim = Core.ReadRatFile(fullRatPath);
                    _ratAnimations.Add(ratAnim);
                    _decompContexts.Add(Core.CreateDecompressionContext(ratAnim));
                    Debug.Log($"Loaded RAT file: {ratPath} ({ratAnim.num_frames} frames, {ratAnim.num_vertices} vertices)");
                }
                else
                {
                    Debug.LogError($"RAT file not found: {fullRatPath}");
                }
            }
            
            // Create mesh from embedded data (if available)
            CreateMeshFromActData();
            
            Debug.Log("Animation loaded successfully!");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load animation: {e.Message}");
        }
    }
    
    /// <summary>
    /// Load .rat files (RAT-only animation with transforms baked into vertices)
    /// </summary>
    public void LoadRatOnlyAnimation(string ratPath)
    {
        if (!File.Exists(ratPath))
        {
            Debug.LogError($"AnimationPlayer: RAT file not found: {ratPath}");
            return;
        }
        
        try
        {
            // Load RAT file
            var ratAnim = Core.ReadRatFile(ratPath);
            _ratAnimations.Add(ratAnim);
            _decompContexts.Add(Core.CreateDecompressionContext(ratAnim));
            
            // Create dummy actor data for compatibility
            _actorData = new ActorAnimationData();
            _actorData.framerate = 30f; // Default framerate
            _actorData.ratFilePaths.Add(Path.GetFileName(ratPath));
            
            // Create mesh from embedded data (if available)
            CreateMeshFromActData();
            
            Debug.Log($"Loaded RAT-only animation: {ratAnim.num_frames} frames, {ratAnim.num_vertices} vertices");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load RAT animation: {e.Message}");
        }
    }
    
    /// <summary>
    /// Create mesh from embedded ACT data (version 6)
    /// </summary>
    private void CreateMeshFromActData()
    {
        if (_meshFilter == null)
        {
            Debug.LogError("AnimationPlayer: MeshFilter not found!");
            return;
        }
        
        if (_mesh == null)
        {
            _mesh = new Mesh();
            _mesh.name = "AnimationMesh";
        }
        
        try
        {
            // If we have RAT animations, create a mesh with the right vertex count
            if (_ratAnimations.Count > 0)
            {
                var ratAnim = _ratAnimations[0];
                var vertices = new Vector3[ratAnim.num_vertices];
                
                // Initialize with first frame data
                Core.DecompressToFrame(_decompContexts[0], ratAnim, 0);
                for (int i = 0; i < ratAnim.num_vertices; i++)
                {
                    float x = ratAnim.min_x + (_decompContexts[0].current_positions[i].x / 255f) * (ratAnim.max_x - ratAnim.min_x);
                    float y = ratAnim.min_y + (_decompContexts[0].current_positions[i].y / 255f) * (ratAnim.max_y - ratAnim.min_y);
                    float z = ratAnim.min_z + (_decompContexts[0].current_positions[i].z / 255f) * (ratAnim.max_z - ratAnim.min_z);
                    vertices[i] = new Vector3(x, y, -z);
                }
                
                _mesh.vertices = vertices;
                
                // Create proper triangle indices (quads: each 4 vertices = 2 triangles = 6 indices)
                if (ratAnim.num_vertices >= 4)
                {
                    int numQuads = (int)ratAnim.num_vertices / 4;
                    var triangles = new int[numQuads * 6];
                    
                    for (int i = 0; i < numQuads; i++)
                    {
                        int vertexOffset = i * 4;
                        int indexOffset = i * 6;
                        
                        // Quad: two triangles
                        triangles[indexOffset + 0] = vertexOffset + 0;
                        triangles[indexOffset + 1] = vertexOffset + 1;
                        triangles[indexOffset + 2] = vertexOffset + 2;
                        
                        triangles[indexOffset + 3] = vertexOffset + 2;
                        triangles[indexOffset + 4] = vertexOffset + 1;
                        triangles[indexOffset + 5] = vertexOffset + 3;
                    }
                    _mesh.triangles = triangles;
                }
                
                // Create default UVs
                var uvs = new Vector2[ratAnim.num_vertices];
                for (int i = 0; i < ratAnim.num_vertices; i++)
                {
                    uvs[i] = new Vector2(0, 0);
                }
                _mesh.uv = uvs;
                
                // Create default colors
                var colors = new Color[ratAnim.num_vertices];
                for (int i = 0; i < ratAnim.num_vertices; i++)
                {
                    colors[i] = Color.white;
                }
                _mesh.colors = colors;
                
                _mesh.RecalculateBounds();
            }
            else
            {
                // Fallback: create a simple quad mesh as placeholder
                _mesh.vertices = new Vector3[]
                {
                    new Vector3(-0.5f, -0.5f, 0),
                    new Vector3(0.5f, -0.5f, 0),
                    new Vector3(-0.5f, 0.5f, 0),
                    new Vector3(0.5f, 0.5f, 0)
                };
                _mesh.uv = new Vector2[]
                {
                    new Vector2(0, 0),
                    new Vector2(1, 0),
                    new Vector2(0, 1),
                    new Vector2(1, 1)
                };
                _mesh.triangles = new int[] { 0, 1, 2, 2, 1, 3 };
                _mesh.colors = new Color[]
                {
                    Color.white,
                    Color.white,
                    Color.white,
                    Color.white
                };
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"AnimationPlayer: Error creating mesh: {e.Message}");
            // Create minimal fallback mesh so something still renders
            _mesh.vertices = new Vector3[]
            {
                Vector3.zero,
                Vector3.one,
                Vector3.one * 2f
            };
            _mesh.triangles = new int[] { 0, 1, 2 };
        }
        
        // Always assign the mesh to the filter, even if it's minimal
        if (_meshFilter != null)
        {
            _meshFilter.mesh = _mesh;
        }
    }
    
    /// <summary>
    /// Apply a specific keyframe to the mesh and transform
    /// </summary>
    public void ApplyKeyframe(int keyframeIndex)
    {
        if (_ratAnimations.Count == 0 || keyframeIndex >= _ratAnimations[0].num_frames)
            return;
            
        var ratAnim = _ratAnimations[0];
        var context = _decompContexts[0];
        
        // Decompress to the target frame
        Core.DecompressToFrame(context, ratAnim, (uint)keyframeIndex);
        
        // Convert compressed positions back to world space
        var vertices = new Vector3[ratAnim.num_vertices];
        for (int i = 0; i < ratAnim.num_vertices; i++)
        {
            // Convert from 8-bit quantized back to float using RAT bounds
            float x = ratAnim.min_x + (context.current_positions[i].x / 255f) * (ratAnim.max_x - ratAnim.min_x);
            float y = ratAnim.min_y + (context.current_positions[i].y / 255f) * (ratAnim.max_y - ratAnim.min_y);
            float z = ratAnim.min_z + (context.current_positions[i].z / 255f) * (ratAnim.max_z - ratAnim.min_z);
            
            // Convert from right-handed to left-handed coordinates
            vertices[i] = new Vector3(x, y, -z);
        }
        
        // Debug logging
        if (debugVertexPositions)
        {
            Debug.Log($"Frame {keyframeIndex}: First vertex={vertices[0]}, Last vertex={vertices[vertices.Length - 1]}");
        }
        
        // Update mesh vertices
        if (_mesh != null && vertices.Length <= _mesh.vertexCount)
        {
            _mesh.vertices = vertices;
            _mesh.RecalculateBounds();
        }
    }
    
    /// <summary>
    /// Start playback
    /// </summary>
    public void Play()
    {
        if (_actorData == null)
        {
            Debug.LogError("No animation loaded!");
            return;
        }
        
        _isPlaying = true;
        _currentTime = 0f;
        _currentKeyframe = 0;
        
        Debug.Log("Animation playback started");
    }
    
    /// <summary>
    /// Stop playback
    /// </summary>
    public void Stop()
    {
        _isPlaying = false;
        _currentTime = 0f;
        _currentKeyframe = 0;
        
        Debug.Log("Animation playback stopped");
    }
    
    /// <summary>
    /// Pause/unpause playback
    /// </summary>
    public void Pause()
    {
        _isPlaying = !_isPlaying;
        Debug.Log(_isPlaying ? "Animation resumed" : "Animation paused");
    }
    
    /// <summary>
    /// Load ACT file data with proper parsing of version 6 format
    /// </summary>
    private ActorAnimationData LoadActorData(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
        using (var reader = new BinaryReader(stream))
        {
            // Read header
            var headerBytes = reader.ReadBytes(Marshal.SizeOf<ActorHeader>());
            var header = BytesToStruct<ActorHeader>(headerBytes);
            
            if (header.magic != 0x52544341) // 'ACTR'
            {
                throw new System.Exception($"Invalid ACT file magic: 0x{header.magic:X8}");
            }
            
            if (header.version != 6)
            {
                throw new System.Exception($"Unsupported ACT version: {header.version} (expected 6)");
            }
            
            var data = new ActorAnimationData();
            data.framerate = header.framerate;
            
            // Read RAT filenames
            stream.Seek(Marshal.SizeOf<ActorHeader>(), SeekOrigin.Begin);
            var ratFilenamesBlob = reader.ReadBytes((int)header.rat_filenames_length);
            
            // Parse null-terminated UTF-8 strings
            var ratPaths = new List<string>();
            int start = 0;
            for (int i = 0; i < ratFilenamesBlob.Length; i++)
            {
                if (ratFilenamesBlob[i] == 0)
                {
                    var pathBytes = new byte[i - start];
                    System.Array.Copy(ratFilenamesBlob, start, pathBytes, 0, i - start);
                    ratPaths.Add(System.Text.Encoding.UTF8.GetString(pathBytes));
                    start = i + 1;
                }
            }
            data.ratFilePaths.AddRange(ratPaths);
            
            return data;
        }
    }

    // Helper methods for fixed-point conversion (same as in Actor.cs)
    private static float Fixed16ToFloat(ushort fixedValue, float minValue, float maxValue)
    {
        if (maxValue <= minValue) return minValue;
        float normalized = fixedValue / 65535f;
        return minValue + normalized * (maxValue - minValue);
    }
    
    private static float Fixed16ToDegrees(ushort fixedValue)
    {
        return (fixedValue / 65535f) * 360f;
    }
    
    private static T BytesToStruct<T>(byte[] bytes) where T : struct
    {
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
        }
        finally
        {
            handle.Free();
        }
    }
    
    void OnGUI()
    {
        if (_actorData != null && _ratAnimations.Count > 0)
        {
            var ratAnim = _ratAnimations[0];
            GUI.Label(new Rect(10, 10, 300, 20), $"Frame: {_currentKeyframe}/{ratAnim.num_frames - 1}");
            GUI.Label(new Rect(10, 30, 300, 20), $"Time: {_currentTime:F2}s");
            GUI.Label(new Rect(10, 50, 300, 20), $"Playing: {_isPlaying}");
            if (GUI.Button(new Rect(10, 70, 60, 30), _isPlaying ? "Pause" : "Play"))
            {
                if (_isPlaying) Pause(); else Play();
            }
            if (GUI.Button(new Rect(80, 70, 60, 30), "Stop"))
            {
                Stop();
            }
        }
    }
    
    void OnDrawGizmos()
    {
        if (!showVertexGizmos || _mesh == null || _mesh.vertices == null) return;
        
        Gizmos.color = Color.cyan;
        int count = Mathf.Min(maxGizmoVertices, _mesh.vertices.Length);
        
        for (int i = 0; i < count; i++)
        {
            // Transform local vertex to world space
            Vector3 worldPos = transform.TransformPoint(_mesh.vertices[i]);
            Gizmos.DrawSphere(worldPos, 0.01f);
        }
        
        // Draw bounds
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.TransformPoint(_mesh.bounds.center), 
                           Vector3.Scale(_mesh.bounds.size, transform.localScale));
    }
}
