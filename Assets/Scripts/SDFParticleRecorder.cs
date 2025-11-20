using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Records Unity ParticleSystem data and converts each particle into an SDF shape instance,
/// then exports the entire particle system animation as .act and .rat files.
/// This enables retro hardware to display particle effects using efficient SDF rendering.
/// 
/// This component is automatically added to all ParticleSystem objects in the scene.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
[DisallowMultipleComponent]
public class SDFParticleRecorder : MonoBehaviour
{
    [Header("Particle System Target")]
    [Tooltip("The particle system to record. Auto-detected if not set.")]
    public ParticleSystem targetParticleSystem;

    [Header("SDF Shape Settings")]
    [Tooltip("The SDF shape type to use for each particle.")]
    public SDFShapeType particleShapeType = SDFShapeType.Circle;

    [Tooltip("The emulated resolution for SDF shape textures.")]
    public SDFEmulatedResolution shapeResolution = SDFEmulatedResolution.Tex256x256;

    [Tooltip("Color for particles (can be overridden by particle system's own colors).")]
    public Color particleColor = Color.white;

    [Tooltip("Use particle system's color over lifetime module if available.")]
    public bool useParticleSystemColors = true;

    [Tooltip("Roundness for shapes like Box and Triangle.")]
    [Range(0.0f, 0.5f)]
    public float roundness = 0.0f;

    [Tooltip("Smoothness/anti-aliasing of shape edges.")]
    [Range(0.001f, 0.1f)]
    public float smooth = 0.01f;

    [Tooltip("Thickness for ring and line shapes.")]
    [Range(0.01f, 0.5f)]
    public float thickness = 0.1f;

    [Header("Arrow Shape Settings")]
    [Tooltip("Arrow head size (only for Arrow shape).")]
    [Range(0.1f, 0.5f)]
    public float arrowHeadSize = 0.25f;

    [Tooltip("Arrow shaft thickness (only for Arrow shape).")]
    [Range(0.01f, 0.2f)]
    public float arrowShaftThickness = 0.08f;

    [Header("Star Shape Settings")]
    [Tooltip("Star inner radius (only for Star shape).")]
    [Range(0.1f, 0.9f)]
    public float starInner = 0.3f;

    [Tooltip("Star outer radius (only for Star shape).")]
    [Range(0.1f, 1.0f)]
    public float starOuter = 0.5f;

    [Tooltip("Number of star points (only for Star shape).")]
    [Range(3f, 12f)]
    public float starPoints = 5f;

    [Header("Recording Settings")]
    [Tooltip("Frames per second to capture during recording.")]
    public float captureFramerate = 30.0f;

    [Tooltip("Auto-start recording when play mode begins.")]
    public bool autoStartRecording = true;

    [Tooltip("Only record when particle system is visible by any camera.")]
    public bool onlyRecordWhenVisible = true;

    [Tooltip("Base filename for exported .rat and .act files (defaults to GameObject name).")]
    public string baseFilename = "particle_system";

    [Tooltip("Maximum file size in KB before splitting into multiple parts.")]
    [Range(16, 1024)]
    public int maxFileSizeKB = 512;

    [Tooltip("Automatically export when exiting play mode.")]
    public bool autoExportOnPlayModeExit = true;

    [Tooltip("If true, exports .act and .rat files. If false, only records in memory.")]
    public bool exportBinary = true;

    // Private fields
    private bool _isRecording = false;
    private float _recordingStartTime;
    private float _lastCaptureTime;
    private ParticleSystem.Particle[] _particleBuffer;
    private ParticleSystemRenderer _particleRenderer;
    private Camera _mainCamera;
    
    // Storage for particle data per frame
    private List<ParticleFrameData> _recordedFrames = new List<ParticleFrameData>();
    
    // Slot-based particle tracking (stable indices)
    private Dictionary<uint, int> _particleIdToSlot = new Dictionary<uint, int>();
    private HashSet<int> _freeSlots = new HashSet<int>();
    private int _nextSlotIndex = 0;
    
    // SDF shape template (used to generate texture)
    private SDFShape _sdfShapeTemplate;

    /// <summary>
    /// Stores all particle data for a single frame
    /// </summary>
    [System.Serializable]
    private struct ParticleFrameData
    {
        public List<ParticleInstance> particles;
        public float timestamp;

        public ParticleFrameData(int capacity)
        {
            particles = new List<ParticleInstance>(capacity);
            timestamp = 0f;
        }
    }

    /// <summary>
    /// Stores data for a single particle instance
    /// </summary>
    [System.Serializable]
    private struct ParticleInstance
    {
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;
        public Color color;
    }

    void Awake()
    {
        baseFilename = gameObject.name;
        if (targetParticleSystem == null)
        {
            targetParticleSystem = GetComponent<ParticleSystem>();
        }

        if (targetParticleSystem == null)
        {
        Debug.LogError("SDFParticleRecorder - no ParticleSystem found");
            enabled = false;
            return;
        }

        // Initialize particle buffer
        _particleBuffer = new ParticleSystem.Particle[targetParticleSystem.main.maxParticles];
        
        // Get the particle renderer for visibility checks
        _particleRenderer = targetParticleSystem.GetComponent<ParticleSystemRenderer>();
        
        // Cache main camera for billboard calculations
        _mainCamera = Camera.main;
        
        // Set base filename to GameObject name if it's still the default
        if (string.IsNullOrEmpty(baseFilename) || baseFilename == "particle_system")
        {
            baseFilename = gameObject.name;
        }

#if UNITY_EDITOR
        // Subscribe to play mode state changes
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Update base filename to GameObject name if empty or default
        if (string.IsNullOrEmpty(baseFilename) || baseFilename == "particle_system")
        {
            baseFilename = gameObject.name;
        }
        
        // Ensure we have a target particle system
        if (targetParticleSystem == null)
        {
            targetParticleSystem = GetComponent<ParticleSystem>();
        }
        
        // Update SDF settings when inspector values change (works in both edit and play mode)
        if (_sdfShapeTemplate != null)
        {
            UpdateSDFSettings();
        }
        else if (!Application.isPlaying && targetParticleSystem != null)
        {
            // In edit mode, create the template if it doesn't exist
            // This allows previewing the SDF material without entering play mode
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && _sdfShapeTemplate == null && targetParticleSystem != null)
                {
                    CreateSDFShapeTemplate();
                    ApplySDFMaterialToParticleSystem();
                }
            };
        }
    }
#endif

    void Start()
    {
        baseFilename = gameObject.name;
        // Sanitize filename
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            baseFilename = baseFilename.Replace(c, '_');
        }

        // Create SDF shape template for texture generation (or recreate if needed)
        if (_sdfShapeTemplate == null)
        {
            CreateSDFShapeTemplate();
        }
        
        // Apply the SDF texture to the particle system's material
        ApplySDFMaterialToParticleSystem();

        if (autoStartRecording && Application.isPlaying)
        {
            StartRecording();
        }
    }

    void Update()
    {
        // Recording continues while in play mode
        // It will be stopped when exiting play mode (auto-export) or manually
    }

    void LateUpdate()
    {
        if (_isRecording)
        {
            // Check if we should record this frame
            bool shouldRecord = ShouldRecordFrame();
            
            if (shouldRecord)
            {
                // Capture particle data at the specified framerate
                float captureInterval = 1.0f / captureFramerate;
                if (Time.time - _lastCaptureTime >= captureInterval)
                {
                    CaptureParticleFrame();
                    _lastCaptureTime = Time.time;
                }
            }
        }
    }
    
    /// <summary>
    /// Determines if we should record the current frame based on visibility and enabled state
    /// </summary>
    private bool ShouldRecordFrame()
    {
        // Check if particle system is enabled
        if (!targetParticleSystem.isPlaying && !targetParticleSystem.isPaused)
            return false;
        
        // Check if component is enabled
        if (!enabled)
            return false;
        
        // Check visibility if required
        if (onlyRecordWhenVisible)
        {
            if (_particleRenderer != null && !_particleRenderer.isVisible)
                return false;
        }
        
        return true;
    }

    /// <summary>
    /// Creates a temporary SDF shape to use as a template for generating textures
    /// </summary>
    private void CreateSDFShapeTemplate()
    {
        GameObject templateObj = new GameObject("_SDFShapeTemplate_Hidden");
        templateObj.hideFlags = HideFlags.HideAndDontSave;
        
        _sdfShapeTemplate = templateObj.AddComponent<SDFShape>();
        _sdfShapeTemplate.shapeType = particleShapeType;
        _sdfShapeTemplate.emulatedResolution = shapeResolution;
        _sdfShapeTemplate.color = particleColor;
        _sdfShapeTemplate.roundness = roundness;
        _sdfShapeTemplate.smooth = smooth;
        _sdfShapeTemplate.thickness = thickness;
        
        // Arrow-specific parameters
        _sdfShapeTemplate.headSize = arrowHeadSize;
        _sdfShapeTemplate.shaftThickness = arrowShaftThickness;
        
        // Star-specific parameters
        _sdfShapeTemplate.starInner = starInner;
        _sdfShapeTemplate.starOuter = starOuter;
        _sdfShapeTemplate.starPoints = starPoints;

        // Force material update to generate the texture
        _sdfShapeTemplate.UpdateMaterial();

    Debug.Log($"SDFParticleRecorder - created template: {particleShapeType}, {shapeResolution}");
    }

    /// <summary>
    /// Applies the SDF shape texture to the particle system's material
    /// </summary>
    private void ApplySDFMaterialToParticleSystem()
    {
        // Ensure we have a particle renderer reference
        if (_particleRenderer == null && targetParticleSystem != null)
        {
            _particleRenderer = targetParticleSystem.GetComponent<ParticleSystemRenderer>();
        }
        
        if (_particleRenderer == null)
        {
            // Silently return in edit mode if renderer not ready yet
            if (!Application.isPlaying)
            {
                return;
            }
            Debug.LogWarning("SDFParticleRecorder - no ParticleSystemRenderer found");
            return;
        }

        if (_sdfShapeTemplate == null)
        {
            Debug.LogWarning("SDFParticleRecorder - SDF template not created yet");
            return;
        }

        // Get the render texture from the SDF shape
        var sdfRenderer = _sdfShapeTemplate.GetComponent<MeshRenderer>();
        if (sdfRenderer != null && sdfRenderer.sharedMaterial != null)
        {
            Texture sdfTexture = sdfRenderer.sharedMaterial.mainTexture;
            
            if (sdfTexture != null)
            {
                // In Edit Mode, use sharedMaterial to avoid leaking materials
                // In Play Mode, use material to allow runtime modification
                Material particleMaterial;
                if (Application.isPlaying)
                {
                    particleMaterial = _particleRenderer.material;
                }
                else
                {
                    particleMaterial = _particleRenderer.sharedMaterial;
                }
                
                if (particleMaterial == null)
                {
                    // Create a new unlit transparent material for particles
                    Shader unlitShader = Shader.Find("Unlit/Transparent");
                    if (unlitShader != null)
                    {
                        particleMaterial = new Material(unlitShader);
                        particleMaterial.name = "ParticleSystem_SDF_Material";
                    }
                    else
                    {
                        Debug.LogError("SDFParticleRecorder - missing Unlit/Transparent shader");
                        return;
                    }
                }
                
                // Apply the SDF texture to the particle material
                particleMaterial.mainTexture = sdfTexture;
                
                // Set the material back (use appropriate method based on mode)
                if (Application.isPlaying)
                {
                    _particleRenderer.material = particleMaterial;
                }
                else
                {
                    _particleRenderer.sharedMaterial = particleMaterial;
                }
                
            }
            else
            {
                Debug.LogWarning("SDFParticleRecorder - SDF texture not yet generated");
            }
        }
    }

    /// <summary>
    /// Updates the SDF shape settings and re-applies the material to the particle system
    /// Call this when you change shape type, resolution, color, or radius at runtime
    /// </summary>
    public void UpdateSDFSettings()
    {
        if (_sdfShapeTemplate != null)
        {
            // Update the SDF shape template with new settings
            _sdfShapeTemplate.shapeType = particleShapeType;
            _sdfShapeTemplate.emulatedResolution = shapeResolution;
            _sdfShapeTemplate.color = particleColor;
            _sdfShapeTemplate.roundness = roundness;
            _sdfShapeTemplate.smooth = smooth;
            _sdfShapeTemplate.thickness = thickness;
            
            // Arrow-specific parameters
            _sdfShapeTemplate.headSize = arrowHeadSize;
            _sdfShapeTemplate.shaftThickness = arrowShaftThickness;
            
            // Star-specific parameters
            _sdfShapeTemplate.starInner = starInner;
            _sdfShapeTemplate.starOuter = starOuter;
            _sdfShapeTemplate.starPoints = starPoints;
            
            // Regenerate the texture
            _sdfShapeTemplate.UpdateMaterial();
            
            // Reapply to particle system
            ApplySDFMaterialToParticleSystem();
            
        }
    }

    /// <summary>
    /// Starts recording particle system data
    /// </summary>
    public void StartRecording()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("SDFParticleRecorder - recording only works in Play Mode");
            return;
        }
        
        if (_isRecording)
        {
            Debug.LogWarning("SDFParticleRecorder - already recording");
            return;
        }

    _recordedFrames.Clear();
        
        // Reset slot tracking
        _particleIdToSlot.Clear();
        _freeSlots.Clear();
        _nextSlotIndex = 0;
        int maxParticles = targetParticleSystem.main.maxParticles;
        for (int i = 0; i < maxParticles; i++)
        {
            _freeSlots.Add(i);
        }
        
        _recordingStartTime = Time.time;
        _lastCaptureTime = Time.time;
        _isRecording = true;

    }

    /// <summary>
    /// Captures current particle system state as a frame
    /// </summary>
    private void CaptureParticleFrame()
    {
        if (targetParticleSystem == null) return;

        int maxParticles = targetParticleSystem.main.maxParticles;
        
        // Ensure buffer is large enough
        if (_particleBuffer == null || _particleBuffer.Length < maxParticles)
        {
            _particleBuffer = new ParticleSystem.Particle[maxParticles];
        }

        // Get current particle data
        int particleCount = targetParticleSystem.GetParticles(_particleBuffer);

        // Track which particle IDs are alive this frame
        HashSet<uint> aliveParticleIds = new HashSet<uint>();
        
        // Initialize frame with all slots as inactive
        ParticleFrameData frameData = new ParticleFrameData(maxParticles);
        frameData.timestamp = Time.time - _recordingStartTime;
        
        // Get spawn position for inactive particles
        Vector3 spawnPosition = GetSpawnPosition();
        
        // Create inactive particle template
        ParticleInstance inactiveParticle = new ParticleInstance
        {
            position = spawnPosition,
            rotation = Vector3.zero,
            scale = new Vector3(0.001f, 0.001f, 0.001f),
            color = new Color(particleColor.r, particleColor.g, particleColor.b, 0f)
        };
        
        // Fill all slots with inactive particles first
        for (int i = 0; i < maxParticles; i++)
        {
            frameData.particles.Add(inactiveParticle);
        }

        // Process active particles and assign them to stable slots
        for (int i = 0; i < particleCount; i++)
        {
            ParticleSystem.Particle p = _particleBuffer[i];
            uint particleId = p.randomSeed; // Use randomSeed as stable particle ID
            
            aliveParticleIds.Add(particleId);
            
            // Assign or get slot for this particle
            int slotIndex;
            if (!_particleIdToSlot.TryGetValue(particleId, out slotIndex))
            {
                // New particle - assign it a slot
                if (_freeSlots.Count > 0)
                {
                    slotIndex = _freeSlots.First();
                    _freeSlots.Remove(slotIndex);
                }
                else
                {
                    // Fallback: use next available index (shouldn't happen with proper maxParticles)
                    slotIndex = _nextSlotIndex % maxParticles;
                    _nextSlotIndex++;
                }
                _particleIdToSlot[particleId] = slotIndex;
            }
            
            // Create particle instance with actual data
            ParticleInstance instance = new ParticleInstance
            {
                position = p.position,
                rotation = p.rotation3D,
                scale = new Vector3(p.GetCurrentSize(targetParticleSystem), p.GetCurrentSize(targetParticleSystem), 1f),
                color = useParticleSystemColors ? p.GetCurrentColor(targetParticleSystem) : particleColor
            };
            
            // Place particle in its stable slot
            frameData.particles[slotIndex] = instance;
        }
        
        // Free up slots from dead particles
        List<uint> deadParticles = new List<uint>();
        foreach (var kvp in _particleIdToSlot)
        {
            if (!aliveParticleIds.Contains(kvp.Key))
            {
                _freeSlots.Add(kvp.Value);
                deadParticles.Add(kvp.Key);
            }
        }
        foreach (uint deadId in deadParticles)
        {
            _particleIdToSlot.Remove(deadId);
        }
        
        // Add frame to recorded frames
        _recordedFrames.Add(frameData);
    }

    /// <summary>
    /// Gets the spawn position for inactive particles based on the particle system's shape module
    /// </summary>
    private Vector3 GetSpawnPosition()
    {
        if (targetParticleSystem == null) return Vector3.zero;

        var shape = targetParticleSystem.shape;
        var main = targetParticleSystem.main;
        
        // Get the spawn position in the appropriate space
        Vector3 spawnPos = Vector3.zero;
        
        if (main.simulationSpace == ParticleSystemSimulationSpace.World)
        {
            spawnPos = transform.position;
        }
        else if (main.simulationSpace == ParticleSystemSimulationSpace.Local)
        {
            spawnPos = Vector3.zero; // Local space origin
        }
        else if (main.simulationSpace == ParticleSystemSimulationSpace.Custom && main.customSimulationSpace != null)
        {
            spawnPos = main.customSimulationSpace.position;
        }
        
        return spawnPos;
    }

    /// <summary>
    /// Stops recording and exports to .act and .rat files
    /// </summary>
    public void StopRecordingAndExport()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("SDFParticleRecorder: Recording only works in Play Mode!");
            return;
        }
        
        if (!_isRecording)
        {
            Debug.LogWarning("SDFParticleRecorder - not currently recording");
            return;
        }

        _isRecording = false;
        
        if (_recordedFrames.Count == 0)
        {
            Debug.LogError("SDFParticleRecorder - no frames recorded");
            return;
        }

        if (!exportBinary)
        {
            Debug.Log("SDFParticleRecorder - exportBinary is false, skipping export.");
            return;
        }

        // Export the recorded data
        ExportToActAndRat();
    }

    /// <summary>
    /// Exports recorded particle data to .act and .rat files with transforms baked into vertices
    /// Creates multiple RAT chunks (one per particle configuration) but only one ACT file
    /// </summary>
    private void ExportToActAndRat()
    {
        Debug.Log($"SDFParticleRecorder - starting export ({_recordedFrames.Count} frames)");

        // Use the particle system's max particles - all frames should have this count
        int maxParticles = targetParticleSystem.main.maxParticles;

        // Validate max particles for RAT format (ushort index limit)
        // Each particle uses 4 vertices. 65535 / 4 = 16383.75
        if (maxParticles > 16383)
        {
            Debug.LogError($"SDFParticleRecorder: Particle system has {maxParticles} max particles, which requires {maxParticles * 4} vertices. RAT format is limited to 65535 vertices (approx 16383 particles). Export aborted to prevent corruption.");
            return;
        }

        Debug.Log($"SDFParticleRecorder - max particles: {maxParticles}");

        // Generate mesh data (quad per particle)
        // Each particle is a quad with 4 vertices
        int verticesPerParticle = 4;
        int totalVertices = maxParticles * verticesPerParticle;

        // Create mesh topology (shared across all frames)
        ushort[] indices = new ushort[maxParticles * 6]; // 2 triangles per quad
        Vector2[] staticUVs = new Vector2[totalVertices];
        Color[] staticColors = new Color[totalVertices];

        // Build quad topology, UVs, and default colors
        for (int i = 0; i < maxParticles; i++)
        {
            int vertexOffset = i * 4;
            int indexOffset = i * 6;

            // Quad indices (two triangles, standard counter-clockwise winding)
            // A quad is made of two triangles: (0, 2, 1) and (2, 3, 1)
            // Vertex layout: 0=BL, 1=BR, 2=TL, 3=TR
            indices[indexOffset + 0] = (ushort)(vertexOffset + 0); // Triangle 1: V0 (BL)
            indices[indexOffset + 1] = (ushort)(vertexOffset + 2); // Triangle 1: V2 (TL)
            indices[indexOffset + 2] = (ushort)(vertexOffset + 1); // Triangle 1: V1 (BR)

            indices[indexOffset + 3] = (ushort)(vertexOffset + 2); // Triangle 2: V2 (TL)
            indices[indexOffset + 4] = (ushort)(vertexOffset + 3); // Triangle 2: V3 (TR)
            indices[indexOffset + 5] = (ushort)(vertexOffset + 1); // Triangle 2: V1 (BR)

            // Quad UVs (standard quad mapping)
            staticUVs[vertexOffset + 0] = new Vector2(0, 0); // Bottom-left
            staticUVs[vertexOffset + 1] = new Vector2(1, 0); // Bottom-right
            staticUVs[vertexOffset + 2] = new Vector2(0, 1); // Top-left
            staticUVs[vertexOffset + 3] = new Vector2(1, 1); // Top-right
            
            // Default white color for all vertices
            staticColors[vertexOffset + 0] = Color.white;
            staticColors[vertexOffset + 1] = Color.white;
            staticColors[vertexOffset + 2] = Color.white;
            staticColors[vertexOffset + 3] = Color.white;
            
            // Debug first particle's indices and UVs
            if (i == 0)
            {
                Debug.Log($"SDFParticleRecorder - First particle indices setup:\n" +
                         $"  Vertex offset: {vertexOffset}, Index offset: {indexOffset}\n" +
                         $"  Triangle 1: indices[{indexOffset}]={indices[indexOffset + 0]}, indices[{indexOffset + 1}]={indices[indexOffset + 1]}, indices[{indexOffset + 2}]={indices[indexOffset + 2]}\n" +
                         $"  Triangle 2: indices[{indexOffset + 3}]={indices[indexOffset + 3]}, indices[{indexOffset + 4}]={indices[indexOffset + 4]}, indices[{indexOffset + 5}]={indices[indexOffset + 5]}\n" +
                         $"  UVs: V{vertexOffset}={staticUVs[vertexOffset + 0]}, V{vertexOffset + 1}={staticUVs[vertexOffset + 1]}, V{vertexOffset + 2}={staticUVs[vertexOffset + 2]}, V{vertexOffset + 3}={staticUVs[vertexOffset + 3]}");
            }
        }

        // Generate texture filename based on SDF shape (before loop)
        int width, height;
        switch (shapeResolution)
        {
            case SDFEmulatedResolution.Tex512x512:
                width = height = 512;
                break;
            case SDFEmulatedResolution.Tex256x256:
                width = height = 256;
                break;
            case SDFEmulatedResolution.Tex128x64:
                width = 128;
                height = 64;
                break;
            default:
                width = height = 256;
                break;
        }

        // Apply platform-specific texture size limits
        SDFShape.GetPlatformTextureSize(width, height, out int actualWidth, out int actualHeight);

        string textureFilename = _sdfShapeTemplate != null 
            ? Path.GetFileName(_sdfShapeTemplate.BuildOutputFilename(actualWidth, actualHeight))
            : $"sdf_{particleShapeType.ToString().ToLower()}_{actualWidth}x{actualHeight}.png";

        // Ensure the SDF texture PNG is generated at the platform-appropriate size
        if (_sdfShapeTemplate != null)
        {
            _sdfShapeTemplate.EnsureTextureExported();
        }

        Debug.Log($"SDFParticleRecorder - using texture: {textureFilename}");

        // Create a dummy mesh with the quad topology (shared across all chunks)
        Mesh quadMesh = new Mesh();
        quadMesh.vertices = new Vector3[totalVertices];
        quadMesh.triangles = indices.Select(i => (int)i).ToArray();
        quadMesh.uv = staticUVs;
        quadMesh.colors = staticColors;
        quadMesh.RecalculateBounds();
        
        // Get GeneratedData path
        string generatedDataPath = Path.Combine(Application.dataPath.Replace("Assets", ""), "GeneratedData");
        if (!Directory.Exists(generatedDataPath))
        {
            Directory.CreateDirectory(generatedDataPath);
        }
        
        // Build per-frame vertex positions for all recorded frames
        List<Vector3[]> frameVertices = new List<Vector3[]>();
        // Track active particles to fix their positions later (to ensure they stay within bounds)
        List<bool[]> frameActiveFlags = new List<bool[]>();
        
        // Initialize bounds for active particles
        Vector3 minBounds = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 maxBounds = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        bool hasActiveParticles = false;
        
        Debug.Log($"SDFParticleRecorder - building vertex frames ({_recordedFrames.Count} frames, {totalVertices} vertices)...");

        foreach (var frame in _recordedFrames)
        {
            Vector3[] vertices = new Vector3[totalVertices];
            bool[] activeFlags = new bool[maxParticles];

            // Process all particles (maxParticles count)
            for (int i = 0; i < maxParticles; i++)
            {
                int vertexOffset = i * 4;
                
                // Get particle data (always present due to slot-based tracking)
                ParticleInstance particle = frame.particles[i];
                
                // Check if particle is active (scale > threshold indicates active particle)
                bool isActive = particle.scale.magnitude > 0.01f;
                activeFlags[i] = isActive;
                
                if (isActive)
                {
                    hasActiveParticles = true;

                    // Get world and local positions
                    var simulationSpace = targetParticleSystem.main.simulationSpace;
                    Vector3 worldPos = (simulationSpace == ParticleSystemSimulationSpace.World)
                        ? particle.position
                        : transform.TransformPoint(particle.position);

                    // 1. Create billboard matrix to face the camera (or default orientation)
                    Matrix4x4 billboardMatrix;
                    if (_mainCamera != null)
                    {
                        // This matrix will orient an object at 'worldPos' to face the camera.
                        // We extract only the rotation part to use it for orienting the quad vertices.
                        Vector3 viewDir = worldPos - _mainCamera.transform.position;
                        if (viewDir.sqrMagnitude < 0.0001f) viewDir = Vector3.forward;
                        billboardMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.LookRotation(viewDir, Vector3.up), Vector3.one);
                    }
                    else
                    {
                        // Fallback to identity if no camera
                        billboardMatrix = Matrix4x4.identity;
                    }

                    // 2. Apply particle's own rotation to the billboard matrix
                    if (particle.rotation != Vector3.zero)
                    {
                        billboardMatrix *= Matrix4x4.Rotate(Quaternion.Euler(particle.rotation));
                    }

                    // 3. Define quad corners in local particle space (centered unit quad)
                    Vector3 v0 = new Vector3(-0.5f, -0.5f, 0); // Bottom-left
                    Vector3 v1 = new Vector3( 0.5f, -0.5f, 0); // Bottom-right
                    Vector3 v2 = new Vector3(-0.5f,  0.5f, 0); // Top-left
                    Vector3 v3 = new Vector3( 0.5f,  0.5f, 0); // Top-right

                    // 4. Orient, scale, and position the vertices
                    Vector3 final_v0 = worldPos + billboardMatrix.MultiplyVector(v0) * particle.scale.x;
                    Vector3 final_v1 = worldPos + billboardMatrix.MultiplyVector(v1) * particle.scale.x;
                    Vector3 final_v2 = worldPos + billboardMatrix.MultiplyVector(v2) * particle.scale.y;
                    Vector3 final_v3 = worldPos + billboardMatrix.MultiplyVector(v3) * particle.scale.y;

                    // 5. Convert final world-space vertices to the particle system's local space for export
                    if (simulationSpace == ParticleSystemSimulationSpace.World)
                    {
                        // If PS is in world space, the recorder's transform needs to be accounted for
                        vertices[vertexOffset + 0] = transform.InverseTransformPoint(final_v0);
                        vertices[vertexOffset + 1] = transform.InverseTransformPoint(final_v1);
                        vertices[vertexOffset + 2] = transform.InverseTransformPoint(final_v2);
                        vertices[vertexOffset + 3] = transform.InverseTransformPoint(final_v3);
                    }
                    else // Local or Custom space
                    {
                        vertices[vertexOffset + 0] = final_v0;
                        vertices[vertexOffset + 1] = final_v1;
                        vertices[vertexOffset + 2] = final_v2;
                        vertices[vertexOffset + 3] = final_v3;
                    }
                    
                    // Update bounds
                    minBounds = Vector3.Min(minBounds, vertices[vertexOffset + 0]);
                    minBounds = Vector3.Min(minBounds, vertices[vertexOffset + 1]);
                    minBounds = Vector3.Min(minBounds, vertices[vertexOffset + 2]);
                    minBounds = Vector3.Min(minBounds, vertices[vertexOffset + 3]);
                    
                    maxBounds = Vector3.Max(maxBounds, vertices[vertexOffset + 0]);
                    maxBounds = Vector3.Max(maxBounds, vertices[vertexOffset + 1]);
                    maxBounds = Vector3.Max(maxBounds, vertices[vertexOffset + 2]);
                    maxBounds = Vector3.Max(maxBounds, vertices[vertexOffset + 3]);
                }
                else
                {
                    // Inactive particle: will be fixed to minBounds later
                    // Just set to zero for now
                    vertices[vertexOffset + 0] = Vector3.zero;
                    vertices[vertexOffset + 1] = Vector3.zero;
                    vertices[vertexOffset + 2] = Vector3.zero;
                    vertices[vertexOffset + 3] = Vector3.zero;
                }
            }

            frameVertices.Add(vertices);
            frameActiveFlags.Add(activeFlags);
        }
        
        // Handle no active particles case
        if (!hasActiveParticles)
        {
            Debug.LogWarning($"SDFParticleRecorder - no active particles, using default bounds");
            minBounds = Vector3.zero;
            maxBounds = Vector3.one;
        }
        else
        {
            Debug.Log($"SDFParticleRecorder - optimized bounds from active vertices: Min({minBounds.x:F3}, {minBounds.y:F3}, {minBounds.z:F3}) Max({maxBounds.x:F3}, {maxBounds.y:F3}, {maxBounds.z:F3})");
        }

        // Fix inactive particles by snapping them to minBounds
        // This ensures they are within the compression bounds and won't cause verification errors
        for (int f = 0; f < frameVertices.Count; f++)
        {
            Vector3[] vertices = frameVertices[f];
            bool[] activeFlags = frameActiveFlags[f];
            
            for (int i = 0; i < maxParticles; i++)
            {
                if (!activeFlags[i])
                {
                    int vertexOffset = i * 4;
                    vertices[vertexOffset + 0] = minBounds;
                    vertices[vertexOffset + 1] = minBounds;
                    vertices[vertexOffset + 2] = minBounds;
                    vertices[vertexOffset + 3] = minBounds;
                }
            }
        }
        
        // Create full path for RAT file export
        string fullPath = Path.Combine(generatedDataPath, baseFilename);
        
        Debug.Log($"SDFParticleRecorder - built {frameVertices.Count} frames");
        
        // Compress and export as RAT file(s)
        List<string> allCreatedRatFiles = new List<string>();
        try
        {
            Debug.Log($"SDFParticleRecorder - starting compression...");
            
            // Compress the animation data with custom bounds
            Rat.CompressedAnimation compressedAnim = Rat.Tool.CompressFromFramesWithBounds(
                frameVertices,
                quadMesh,
                staticUVs,
                staticColors,
                minBounds,
                maxBounds,
                preserveFirstFrame: false
            );
            
            Debug.Log($"SDFParticleRecorder - compression complete, writing file...");
            
            // Write RAT file(s) with size splitting
            List<string> ratFiles = Rat.Tool.WriteRatFileWithSizeSplitting(
                fullPath,
                compressedAnim,
                maxFileSizeKB
            );
            
            allCreatedRatFiles.AddRange(ratFiles);
            
            Debug.Log($"SDFParticleRecorder - exported: {ratFiles.Count} RAT file(s)");
            foreach (var ratFile in ratFiles)
            {
                Debug.Log($"  - {ratFile}");
            }

            // Verify the export immediately
            //VerifyExport(ratFiles, frameVertices, minBounds, maxBounds);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SDFParticleRecorder - export failed: {e.Message}\n{e.StackTrace}");
        }
        
        // Create a single ACT file referencing all RAT files
        if (allCreatedRatFiles.Count > 0)
        {
            try
            {
                Debug.Log($"SDFParticleRecorder - Total RAT files created: {allCreatedRatFiles.Count}");
                
                // Convert RAT file paths to just filenames (relative to GeneratedData folder)
                List<string> ratFilenames = allCreatedRatFiles.Select(f => Path.GetFileName(f)).ToList();
                
                Debug.Log($"SDFParticleRecorder - RAT filenames for ACT:");
                foreach (var filename in ratFilenames)
                {
                    Debug.Log($"  - {filename}");
                }
                
                // Create Actor animation data
                ActorAnimationData actorData = new ActorAnimationData();
                actorData.framerate = captureFramerate;
                actorData.ratFilePaths = ratFilenames;
                actorData.textureFilename = textureFilename;
                
                // Set mesh data from the quad mesh
                actorData.meshUVs = staticUVs.Select(uv => new Vector2(uv.x, uv.y)).ToArray();
                actorData.meshColors = staticColors;
                actorData.meshIndices = quadMesh.triangles;
                
                // Save single ACT file
                string actFilePath = Path.Combine(generatedDataPath, baseFilename + ".act");
                Actor.SaveActorData(actFilePath, actorData, Rat.ActorRenderingMode.TextureWithDirectionalLight, embedMeshData: true);
                
                Debug.Log($"SDFParticleRecorder - created single ACT file: {baseFilename}.act ({allCreatedRatFiles.Count} RAT chunks)");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"SDFParticleRecorder - ACT file creation failed: {e.Message}");
            }
        }
        
        Debug.Log($"SDFParticleRecorder - export complete: {allCreatedRatFiles.Count} RAT files, 1 ACT file");
    }

    /// <summary>
    /// Verifies the exported RAT files by reading them back and comparing with original data.
    /// This helps diagnose corruption issues (quantization artifacts vs data corruption).
    /// </summary>
    private void VerifyExport(List<string> ratFiles, List<Vector3[]> originalFrames, Vector3 minBounds, Vector3 maxBounds)
    {
        Debug.Log("SDFParticleRecorder - Verifying export integrity...");
        
        // Calculate expected precision (quantization step size)
        Vector3 range = maxBounds - minBounds;
        // Avoid division by zero
        if (range.x == 0) range.x = 1;
        if (range.y == 0) range.y = 1;
        if (range.z == 0) range.z = 1;

        Vector3 step = new Vector3(
            range.x / 255f,
            range.y / 255f,
            range.z / 255f
        );
        
        // The maximum error introduced by quantization is half the step size on each axis
        float maxQuantizationError = Mathf.Sqrt(step.x*step.x + step.y*step.y + step.z*step.z) * 0.5f;
        
        Debug.Log($"Verification: Bounds size {range}, Max expected quantization error: {maxQuantizationError:F5}");

        int globalFrameIndex = 0;
        bool hasErrors = false;
        
        foreach (string filePath in ratFiles)
        {
            try 
            {
                // Read the file back
                var anim = Rat.Core.ReadRatFile(filePath);
                var ctx = Rat.Core.CreateDecompressionContext(anim);
                
                Debug.Log($"Verifying {Path.GetFileName(filePath)}: {anim.num_frames} frames...");
                
                // Check each frame in this chunk
                for (uint i = 0; i < anim.num_frames; i++)
                {
                    if (globalFrameIndex >= originalFrames.Count) break;
                    
                    Rat.Core.DecompressToFrame(ctx, anim, i);
                    
                    // Compare vertices
                    Vector3[] original = originalFrames[globalFrameIndex];
                    float maxFrameError = 0f;
                    int maxErrorVertex = -1;
                    
                    for (int v = 0; v < anim.num_vertices; v++)
                    {
                        // Dequantize: map 0-255 back to min-max
                        var q = ctx.current_positions[v];
                        Vector3 reconstructed = new Vector3(
                            anim.min_x + (q.x / 255f) * (anim.max_x - anim.min_x),
                            anim.min_y + (q.y / 255f) * (anim.max_y - anim.min_y),
                            anim.min_z + (q.z / 255f) * (anim.max_z - anim.min_z)
                        );
                        
                        float dist = Vector3.Distance(reconstructed, original[v]);
                        if (dist > maxFrameError) 
                        {
                            maxFrameError = dist;
                            maxErrorVertex = v;
                        }
                    }
                    
                    // Check if error is acceptable (allow small margin for float precision)
                    // If error is significantly larger than quantization step, it's likely data corruption
                    if (maxFrameError > maxQuantizationError * 1.5f + 0.01f)
                    {
                        Debug.LogError($"Verification FAILED at Global Frame {globalFrameIndex} (Local {i}) in {Path.GetFileName(filePath)}.\n" +
                                     $"Max Error: {maxFrameError:F5} (Expected < {maxQuantizationError:F5})\n" +
                                     $"Vertex: {maxErrorVertex}\n" +
                                     $"Possible Causes:\n" +
                                     $"1. Delta Compression Drift: If error increases over time, the delta encoding is failing.\n" +
                                     $"2. Bounds Mismatch: If error is constant but high, the bounds used for compression don't match decompression.\n" +
                                     $"3. Data Corruption: If error is random/huge, the file structure might be invalid.");
                        hasErrors = true;
                        
                        // Stop after first error to avoid spam
                        return; 
                    }
                    
                    globalFrameIndex++;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Verification FAILED reading {filePath}: {e.Message}");
                return;
            }
        }
        
        if (!hasErrors)
        {
            Debug.Log("SDFParticleRecorder - Verification PASSED. Exported data matches recorded data within quantization limits.");
        }
    }

    void OnDestroy()
    {
        // Clean up SDF shape template
        if (_sdfShapeTemplate != null)
        {
            if (_sdfShapeTemplate.gameObject != null)
            {
                DestroyImmediate(_sdfShapeTemplate.gameObject);
            }
        }

#if UNITY_EDITOR
        // Unsubscribe from play mode state changes
        UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// Handles play mode state changes to auto-export when exiting play mode
    /// </summary>
    private void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
        {
            // Auto-export if we have recorded frames and auto-export is enabled
            if (autoExportOnPlayModeExit && _isRecording && _recordedFrames != null && _recordedFrames.Count > 0)
            {
                Debug.Log($"SDFParticleRecorder - auto-exporting on play mode exit ({_recordedFrames.Count} frames)");
                StopRecordingAndExport();
            }
        }
    }
#endif

    /// <summary>
    /// Public API to manually start recording
    /// </summary>
    public void ManualStartRecording()
    {
        StartRecording();
    }

    /// <summary>
    /// Public API to manually stop recording and export
    /// </summary>
    public void ManualStopRecording()
    {
        StopRecordingAndExport();
    }
}
