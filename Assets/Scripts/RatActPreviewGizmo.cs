#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Rat;

/// <summary>
/// Displays RAT/ACT animation data as Gizmos in the editor with playback support.
/// Allows you to preview exported animations directly in the Scene view.
/// 
/// How to use:
/// 1. Select a GameObject in the scene
/// 2. In the Inspector, assign a .act file path
/// 3. Enable "Show Preview" to visualize the animation
/// 4. Click "Play" to animate through frames, or use Frame slider to scrub manually
/// 5. See vertex positions, bounds, and animation structure update in real-time
/// </summary>
[ExecuteAlways]
public class RatActPreviewGizmo : MonoBehaviour
{
    [Header("RAT/ACT Preview Settings")]
    [Tooltip("Path to the .act file to preview (relative to project root or absolute)")]
    public string actFilePath = "";
    
    [Tooltip("Enable gizmo visualization")]
    public bool showPreview = false;
    
    [Header("Playback Controls")]
    [Tooltip("Auto-play animation when preview is enabled")]
    public bool autoPlay = false;
    
    [Tooltip("Loop animation")]
    public bool loop = true;
    
    [Tooltip("Playback speed multiplier")]
    [Range(0.1f, 5f)]
    public float playbackSpeed = 1f;
    
    [Tooltip("Frame to display (0 to num_frames-1)")]
    [SerializeField]
    private int previewFrame = 0;
    
    [Header("Visualization")]
    [Tooltip("Draw vertex positions as dots")]
    public bool drawVertices = true;
    
    [Tooltip("Draw animation bounds as wireframe box")]
    public bool drawBounds = true;
    
    [Tooltip("Draw triangle mesh")]
    public bool drawMesh = true;
    
    [Tooltip("Draw only every N-th vertex to reduce clutter")]
    [Range(1, 100)]
    public int vertexSkip = 1;
    
    [Tooltip("Size of vertex dots")]
    [Range(0.001f, 0.1f)]
    public float vertexSize = 0.01f;
    
    [Tooltip("Color for vertices")]
    public Color vertexColor = Color.cyan;
    
    [Tooltip("Color for bounds")]
    public Color boundsColor = Color.yellow;
    
    [Tooltip("Color for mesh")]
    public Color meshColor = new Color(0, 1, 0, 0.3f);
    
    [Tooltip("Color for origin point")]
    public Color originColor = Color.red;
    
    // Private state
    private CompressedAnimation _currentRatAnim;
    private List<CompressedAnimation> _ratAnimations = new List<CompressedAnimation>();
    private List<DecompressionContext> _decompContexts = new List<DecompressionContext>();
    private ActorAnimationData _actorData;
    private string _lastActPath = "";
    private uint _maxFrames = 0;
    
    // Playback state
    private bool _isPlaying = false;
    private float _playbackTime = 0f;
    private double _lastEditorTime = 0;
    
    void OnEnable()
    {
        // Force scene view redraw when enabled
        EditorApplication.update += OnEditorUpdate;
        _lastEditorTime = EditorApplication.timeSinceStartup;
        
        if (autoPlay && showPreview && _maxFrames > 0)
        {
            _isPlaying = true;
            _playbackTime = 0f;
        }
    }
    
    void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        _isPlaying = false;
    }
    
    private void OnEditorUpdate()
    {
        if (!showPreview || _maxFrames == 0)
            return;
        
        // Update playback
        if (_isPlaying && _actorData != null)
        {
            double currentTime = EditorApplication.timeSinceStartup;
            double deltaTime = currentTime - _lastEditorTime;
            _lastEditorTime = currentTime;
            
            _playbackTime += (float)deltaTime * playbackSpeed;
            
            // Convert time to frame
            float frameTime = 1f / _actorData.framerate;
            int newFrame = Mathf.FloorToInt(_playbackTime / frameTime);
            
            if (newFrame >= _maxFrames)
            {
                if (loop)
                {
                    _playbackTime = 0f;
                    newFrame = 0;
                }
                else
                {
                    _isPlaying = false;
                    newFrame = (int)_maxFrames - 1;
                }
            }
            
            previewFrame = newFrame;
        }
        
        // Trigger scene view repaint
        SceneView.RepaintAll();
    }
    
    void OnValidate()
    {
        // Reload when path changes
        if (actFilePath != _lastActPath && !string.IsNullOrEmpty(actFilePath))
        {
            LoadActFile();
            _lastActPath = actFilePath;
        }
        
        // Clamp frame
        if (_maxFrames > 0)
        {
            previewFrame = (int)Mathf.Clamp(previewFrame, 0, (int)_maxFrames - 1);
        }
        
        // Auto-play if enabled
        if (autoPlay && showPreview && _maxFrames > 0 && !_isPlaying)
        {
            _isPlaying = true;
            _playbackTime = 0f;
        }
    }
    
    /// <summary>
    /// Loads a .act file and its referenced .rat files
    /// </summary>
    private void LoadActFile()
    {
        if (string.IsNullOrEmpty(actFilePath))
        {
            Debug.LogWarning("RatActPreviewGizmo: No ACT file path set");
            return;
        }
        
        // Resolve path - handle both relative and absolute
        string fullPath = actFilePath;
        
        // If it's a relative path, resolve it from the project root
        if (!Path.IsPathRooted(fullPath))
        {
            // First try relative to project root (for GeneratedData paths)
            string projectRoot = Application.dataPath.Replace("Assets", "");
            string fullPathFromRoot = Path.Combine(projectRoot, fullPath);
            
            if (File.Exists(fullPathFromRoot))
            {
                fullPath = fullPathFromRoot;
            }
            else
            {
                // Try relative to Assets folder
                string fullPathFromAssets = Path.Combine(Application.dataPath, fullPath);
                if (File.Exists(fullPathFromAssets))
                {
                    fullPath = fullPathFromAssets;
                }
                else
                {
                    // Try as-is
                    if (!File.Exists(fullPath))
                    {
                        Debug.LogError($"RatActPreviewGizmo: ACT file not found: {actFilePath}");
                        return;
                    }
                }
            }
        }
        
        // Ensure the file is a .act file
        if (!fullPath.EndsWith(".act", System.StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError($"RatActPreviewGizmo: Expected .act file, got: {Path.GetFileName(fullPath)}");
            return;
        }
        
        if (!File.Exists(fullPath))
        {
            Debug.LogError($"RatActPreviewGizmo: ACT file not found: {fullPath}");
            return;
        }
        
        try
        {
            // Load ACT file
            _actorData = LoadActorData(fullPath);
            Debug.Log($"RatActPreviewGizmo: Loaded ACT file at {_actorData.framerate} FPS");
            
            // Load referenced RAT files
            _ratAnimations.Clear();
            _decompContexts.Clear();
            _maxFrames = 0;
            
            string directory = Path.GetDirectoryName(fullPath);
            foreach (string ratPath in _actorData.ratFilePaths)
            {
                string fullRatPath = Path.Combine(directory, ratPath);
                if (File.Exists(fullRatPath))
                {
                    var ratAnim = Core.ReadRatFile(fullRatPath);
                    _ratAnimations.Add(ratAnim);
                    _decompContexts.Add(Core.CreateDecompressionContext(ratAnim));
                    
                    _maxFrames = (uint)Mathf.Max(_maxFrames, ratAnim.num_frames);
                    Debug.Log($"RatActPreviewGizmo: Loaded RAT file {ratPath} ({ratAnim.num_frames} frames, {ratAnim.num_vertices} vertices)");
                }
                else
                {
                    Debug.LogError($"RatActPreviewGizmo: RAT file not found: {fullRatPath}");
                }
            }
            
            if (_ratAnimations.Count > 0)
            {
                _currentRatAnim = _ratAnimations[0];
                previewFrame = 0;
                _playbackTime = 0f;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"RatActPreviewGizmo: Failed to load ACT file: {e.Message}");
        }
    }
    
    void OnDrawGizmos()
    {
        if (!showPreview || _currentRatAnim == null)
            return;
        
        // Decompress to current frame
        if (_decompContexts.Count > 0)
        {
            Core.DecompressToFrame(_decompContexts[0], _currentRatAnim, (uint)previewFrame);
            
            // Convert decompressed vertices to world space
            var worldVertices = new Vector3[_currentRatAnim.num_vertices];
            for (int i = 0; i < _currentRatAnim.num_vertices; i++)
            {
                float x = _currentRatAnim.min_x + (_decompContexts[0].current_positions[i].x / 255f) * (_currentRatAnim.max_x - _currentRatAnim.min_x);
                float y = _currentRatAnim.min_y + (_decompContexts[0].current_positions[i].y / 255f) * (_currentRatAnim.max_y - _currentRatAnim.min_y);
                float z = _currentRatAnim.min_z + (_decompContexts[0].current_positions[i].z / 255f) * (_currentRatAnim.max_z - _currentRatAnim.min_z);
                worldVertices[i] = transform.TransformPoint(new Vector3(x, y, z));
            }
            
            // Draw bounds
            if (drawBounds)
            {
                DrawBoundsGizmo(worldVertices);
            }
            
            // Draw vertices
            if (drawVertices)
            {
                DrawVerticesGizmo(worldVertices);
            }
            
            // Draw mesh
            if (drawMesh && _actorData.meshIndices != null)
            {
                DrawMeshGizmo(worldVertices);
            }
            
            // Draw origin
            Gizmos.color = originColor;
            Gizmos.DrawSphere(transform.position, vertexSize * 2);
            
            // Draw frame info and playback status
            GUI.color = Color.white;
            string statusText = _isPlaying ? "▶ Playing" : "⏸ Paused";
            Handles.Label(transform.position + Vector3.up * 2, 
                $"{statusText}\n" +
                $"Frame {previewFrame}/{_maxFrames - 1}\n" +
                $"Vertices: {_currentRatAnim.num_vertices}\n" +
                $"Bounds: X[{_currentRatAnim.min_x:F2}, {_currentRatAnim.max_x:F2}]");
        }
    }
    
    private void DrawBoundsGizmo(Vector3[] vertices)
    {
        // Calculate bounds from vertices
        Bounds bounds = new Bounds(vertices[0], Vector3.zero);
        foreach (var v in vertices)
        {
            bounds.Encapsulate(v);
        }
        
        Gizmos.color = boundsColor;
        Gizmos.DrawWireCube(bounds.center, bounds.size);
    }
    
    private void DrawVerticesGizmo(Vector3[] vertices)
    {
        Gizmos.color = vertexColor;
        
        for (int i = 0; i < vertices.Length; i += vertexSkip)
        {
            Gizmos.DrawSphere(vertices[i], vertexSize);
        }
    }
    
    private void DrawMeshGizmo(Vector3[] vertices)
    {
        if (_actorData.meshIndices == null || _actorData.meshIndices.Length == 0)
            return;
        
        Gizmos.color = meshColor;
        
        // Draw triangles
        for (int i = 0; i < _actorData.meshIndices.Length; i += 3)
        {
            int i0 = _actorData.meshIndices[i];
            int i1 = _actorData.meshIndices[i + 1];
            int i2 = _actorData.meshIndices[i + 2];
            
            if (i0 < vertices.Length && i1 < vertices.Length && i2 < vertices.Length)
            {
                Gizmos.DrawLine(vertices[i0], vertices[i1]);
                Gizmos.DrawLine(vertices[i1], vertices[i2]);
                Gizmos.DrawLine(vertices[i2], vertices[i0]);
            }
        }
    }
    
    /// <summary>
    /// Public method to toggle playback (useful for editor scripts)
    /// </summary>
    public void TogglePlayback()
    {
        if (_maxFrames == 0)
            return;
        
        _isPlaying = !_isPlaying;
        if (!_isPlaying)
            _lastEditorTime = EditorApplication.timeSinceStartup;
    }
    
    /// <summary>
    /// Public method to play the animation
    /// </summary>
    public void Play()
    {
        if (_maxFrames > 0)
        {
            _isPlaying = true;
            _lastEditorTime = EditorApplication.timeSinceStartup;
        }
    }
    
    /// <summary>
    /// Public method to pause the animation
    /// </summary>
    public void Pause()
    {
        _isPlaying = false;
    }
    
    /// <summary>
    /// Public method to reset animation to first frame
    /// </summary>
    public void Reset()
    {
        previewFrame = 0;
        _playbackTime = 0f;
        _isPlaying = false;
    }
    
    private ActorAnimationData LoadActorData(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
        using (var reader = new BinaryReader(stream))
        {
            var headerBytes = reader.ReadBytes(Marshal.SizeOf<ActorHeader>());
            var header = BytesToStruct<ActorHeader>(headerBytes);
            
            if (header.magic != 0x52544341) // 'ACTR'
                throw new System.Exception($"Invalid ACT file magic");
            
            var data = new ActorAnimationData();
            data.framerate = header.framerate;
            data.meshIndices = new int[header.num_indices];
            
            // Read RAT filenames
            stream.Seek(Marshal.SizeOf<ActorHeader>(), SeekOrigin.Begin);
            var ratBlob = reader.ReadBytes((int)header.rat_filenames_length);
            
            var ratPaths = new List<string>();
            int start = 0;
            for (int i = 0; i < ratBlob.Length; i++)
            {
                if (ratBlob[i] == 0)
                {
                    var pathBytes = new byte[i - start];
                    System.Array.Copy(ratBlob, start, pathBytes, 0, i - start);
                    ratPaths.Add(System.Text.Encoding.UTF8.GetString(pathBytes));
                    start = i + 1;
                }
            }
            data.ratFilePaths.AddRange(ratPaths);
            
            // Read mesh indices
            stream.Seek(header.mesh_indices_offset, SeekOrigin.Begin);
            for (int i = 0; i < header.num_indices; i++)
            {
                data.meshIndices[i] = reader.ReadUInt16();
            }
            
            return data;
        }
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
}

#endif
