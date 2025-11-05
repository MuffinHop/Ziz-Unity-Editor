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

    [Header("Export Settings")]
    [Tooltip("Base filename for generated .rat and .act files.")]
    public string baseFilename = "particle_system";

    [Tooltip("Maximum file size in KB before splitting into multiple parts.")]
    [Range(16, 1024)]
    public int maxFileSizeKB = 64;

    [Tooltip("Automatically export when exiting play mode.")]
    public bool autoExportOnPlayModeExit = true;

    // Private fields
    private bool _isRecording = false;
    private float _recordingStartTime;
    private float _lastCaptureTime;
    private ParticleSystem.Particle[] _particleBuffer;
    private ParticleSystemRenderer _particleRenderer;
    
    // Storage for particle data per frame
    private List<ParticleFrameData> _recordedFrames = new List<ParticleFrameData>();
    
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
        if (targetParticleSystem == null)
        {
            targetParticleSystem = GetComponent<ParticleSystem>();
        }

        if (targetParticleSystem == null)
        {
            Debug.LogError("SDFParticleRecorder: No ParticleSystem found!");
            enabled = false;
            return;
        }

        // Initialize particle buffer
        _particleBuffer = new ParticleSystem.Particle[targetParticleSystem.main.maxParticles];
        
        // Get the particle renderer for visibility checks
        _particleRenderer = targetParticleSystem.GetComponent<ParticleSystemRenderer>();
        
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

        Debug.Log($"SDFParticleRecorder: Created SDF shape template ({particleShapeType}, {shapeResolution})");
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
            Debug.LogWarning("SDFParticleRecorder: No ParticleSystemRenderer found!");
            return;
        }

        if (_sdfShapeTemplate == null)
        {
            Debug.LogWarning("SDFParticleRecorder: SDF shape template not created yet!");
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
                        Debug.LogError("SDFParticleRecorder: Could not find Unlit/Transparent shader!");
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
                
                Debug.Log($"SDFParticleRecorder: Applied SDF texture to particle system material");
            }
            else
            {
                Debug.LogWarning("SDFParticleRecorder: SDF texture not yet generated!");
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
            
            Debug.Log($"SDFParticleRecorder: Updated SDF settings ({particleShapeType}, {shapeResolution})");
        }
    }

    /// <summary>
    /// Starts recording particle system data
    /// </summary>
    public void StartRecording()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("SDFParticleRecorder: Recording only works in Play Mode!");
            return;
        }
        
        if (_isRecording)
        {
            Debug.LogWarning("SDFParticleRecorder: Already recording!");
            return;
        }

        _recordedFrames.Clear();
        _recordingStartTime = Time.time;
        _lastCaptureTime = Time.time;
        _isRecording = true;

        Debug.Log($"SDFParticleRecorder: Started recording at {captureFramerate} FPS (will record until play mode exits)");
    }

    /// <summary>
    /// Captures current particle system state as a frame
    /// </summary>
    private void CaptureParticleFrame()
    {
        if (targetParticleSystem == null) return;

        // Get current particle data
        int particleCount = targetParticleSystem.GetParticles(_particleBuffer);

        ParticleFrameData frameData = new ParticleFrameData(particleCount);
        frameData.timestamp = Time.time - _recordingStartTime;

        // Convert Unity particles to our particle instances
        for (int i = 0; i < particleCount; i++)
        {
            ParticleSystem.Particle p = _particleBuffer[i];
            
            ParticleInstance instance = new ParticleInstance
            {
                position = p.position,
                rotation = p.rotation3D,
                scale = new Vector3(p.GetCurrentSize(targetParticleSystem), p.GetCurrentSize(targetParticleSystem), 1f),
                color = useParticleSystemColors ? p.GetCurrentColor(targetParticleSystem) : particleColor
            };

            frameData.particles.Add(instance);
        }

        _recordedFrames.Add(frameData);

        if (_recordedFrames.Count % 30 == 0) // Log every 30 frames
        {
            Debug.Log($"SDFParticleRecorder: Captured frame {_recordedFrames.Count} with {particleCount} particles");
        }
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
            Debug.LogWarning("SDFParticleRecorder: Not currently recording!");
            return;
        }

        _isRecording = false;

        Debug.Log($"SDFParticleRecorder: Recording stopped. Captured {_recordedFrames.Count} frames.");
        
        if (_recordedFrames.Count == 0)
        {
            Debug.LogError("SDFParticleRecorder: No frames recorded!");
            return;
        }

        // Export the recorded data
        ExportToActAndRat();
    }

    /// <summary>
    /// Exports recorded particle data to .act and .rat files
    /// Each particle becomes a vertex-animated quad using the SDF shape texture
    /// </summary>
    private void ExportToActAndRat()
    {
        Debug.Log("SDFParticleRecorder: Starting export process...");

        // Find maximum particle count across all frames
        int maxParticles = 0;
        foreach (var frame in _recordedFrames)
        {
            if (frame.particles.Count > maxParticles)
                maxParticles = frame.particles.Count;
        }

        Debug.Log($"SDFParticleRecorder: Max particles in any frame: {maxParticles}");

        // Generate mesh data (quad per particle)
        // Each particle is a quad with 4 vertices
        int verticesPerParticle = 4;
        int totalVertices = maxParticles * verticesPerParticle;

        // Create mesh topology (shared across all frames)
        ushort[] indices = new ushort[maxParticles * 6]; // 2 triangles per quad
        Vector2[] uvs = new Vector2[totalVertices];
        Color[] colors = new Color[totalVertices];

        // Build quad topology and UVs
        for (int i = 0; i < maxParticles; i++)
        {
            int vertexOffset = i * 4;
            int indexOffset = i * 6;

            // Quad indices (two triangles)
            indices[indexOffset + 0] = (ushort)(vertexOffset + 0);
            indices[indexOffset + 1] = (ushort)(vertexOffset + 1);
            indices[indexOffset + 2] = (ushort)(vertexOffset + 2);
            indices[indexOffset + 3] = (ushort)(vertexOffset + 2);
            indices[indexOffset + 4] = (ushort)(vertexOffset + 1);
            indices[indexOffset + 5] = (ushort)(vertexOffset + 3);

            // Quad UVs (standard quad mapping)
            uvs[vertexOffset + 0] = new Vector2(0, 0); // Bottom-left
            uvs[vertexOffset + 1] = new Vector2(1, 0); // Bottom-right
            uvs[vertexOffset + 2] = new Vector2(0, 1); // Top-left
            uvs[vertexOffset + 3] = new Vector2(1, 1); // Top-right
        }

        // Build per-frame vertex positions
        List<Vector3[]> frameVertices = new List<Vector3[]>();

        foreach (var frame in _recordedFrames)
        {
            Vector3[] vertices = new Vector3[totalVertices];

            // Initialize all vertices to origin (inactive particles)
            for (int i = 0; i < totalVertices; i++)
            {
                vertices[i] = Vector3.zero;
            }

            // Position active particles
            for (int i = 0; i < frame.particles.Count; i++)
            {
                ParticleInstance particle = frame.particles[i];
                int vertexOffset = i * 4;

                // Build a quad centered at particle position
                Vector3 right = Vector3.right * particle.scale.x * 0.5f;
                Vector3 up = Vector3.up * particle.scale.y * 0.5f;

                // Apply rotation if needed
                if (particle.rotation != Vector3.zero)
                {
                    Quaternion rot = Quaternion.Euler(particle.rotation);
                    right = rot * right;
                    up = rot * up;
                }

                vertices[vertexOffset + 0] = particle.position - right - up; // Bottom-left
                vertices[vertexOffset + 1] = particle.position + right - up; // Bottom-right
                vertices[vertexOffset + 2] = particle.position - right + up; // Top-left
                vertices[vertexOffset + 3] = particle.position + right + up; // Top-right

                // Set colors for this particle's quad
                for (int v = 0; v < 4; v++)
                {
                    colors[vertexOffset + v] = particle.color;
                }
            }

            frameVertices.Add(vertices);
        }

        // Generate texture filename based on SDF shape
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

        string textureFilename = _sdfShapeTemplate != null 
            ? Path.GetFileName(_sdfShapeTemplate.BuildOutputFilename(width, height))
            : $"sdf_{particleShapeType.ToString().ToLower()}_{width}x{height}.png";

        // Ensure the SDF texture PNG is generated
        if (_sdfShapeTemplate != null)
        {
            _sdfShapeTemplate.UpdateMaterial();
            // The texture should be automatically generated when the shape is rendered
        }

        Debug.Log($"SDFParticleRecorder: Using texture: {textureFilename}");
        Debug.Log($"SDFParticleRecorder: Total vertices: {totalVertices}, Frames: {frameVertices.Count}");

        // Compress animation using RAT compression
        string meshDataFilename = $"{baseFilename}.ratmesh";
        Rat.CompressedAnimation compressed;

        try
        {
            compressed = Rat.CommandLine.GLBToRAT.CompressFrames(
                frameVertices,
                indices,
                uvs,
                colors,
                $"assets/{textureFilename}",
                meshDataFilename
            );
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SDFParticleRecorder: Compression failed: {e.Message}");
            return;
        }

        // Write RAT files with size splitting
        string generatedDataPath = Path.Combine(Application.dataPath.Replace("/Assets", ""), "GeneratedData");
        if (!Directory.Exists(generatedDataPath))
        {
            Directory.CreateDirectory(generatedDataPath);
        }

        List<string> createdRatFiles = Rat.Tool.WriteRatFileWithSizeSplitting(baseFilename, compressed, maxFileSizeKB);

        Debug.Log($"SDFParticleRecorder: Created {createdRatFiles.Count} file(s):");
        foreach (string file in createdRatFiles)
        {
            Debug.Log($"  - {file}");
        }

        // Filter out .ratmesh file - only keep .rat animation files for Actor
        List<string> ratAnimationFiles = createdRatFiles.Where(f => f.EndsWith(".rat")).ToList();

        // Create Actor animation data
        ActorAnimationData actorData = new ActorAnimationData();
        actorData.framerate = captureFramerate;
        actorData.ratFilePaths.AddRange(ratAnimationFiles.ConvertAll(path => Path.GetFileName(path)));

        // Create transform keyframes (particle system is stationary, but we need frame mapping)
        uint globalFrame = 0;
        uint currentFileIndex = 0;
        uint localFrameIndex = 0;
        uint framesPerFile = ratAnimationFiles.Count > 0 ? compressed.num_frames / (uint)ratAnimationFiles.Count : compressed.num_frames;

        for (int i = 0; i < _recordedFrames.Count; i++)
        {
            // Calculate which RAT file this frame belongs to
            if (localFrameIndex >= framesPerFile && currentFileIndex < ratAnimationFiles.Count - 1)
            {
                currentFileIndex++;
                localFrameIndex = 0;
            }

            ActorTransformFloat transform = new ActorTransformFloat
            {
                position = this.transform.position,
                rotation = this.transform.eulerAngles,
                scale = this.transform.localScale,
                rat_file_index = currentFileIndex,
                rat_local_frame = localFrameIndex
            };

            actorData.transforms.Add(transform);
            
            globalFrame++;
            localFrameIndex++;
        }

        // Save .act file
        string actFilePath = Path.Combine(generatedDataPath, $"{baseFilename}.act");
        Actor.SaveActorData(actFilePath, actorData);

        Debug.Log($"SDFParticleRecorder: Export complete!");
        Debug.Log($"  - ACT file: {actFilePath}");
        Debug.Log($"  - Total keyframes: {actorData.transforms.Count}");
        Debug.Log($"  - Max particles: {maxParticles}");
        Debug.Log($"  - Texture: {textureFilename}");
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
                Debug.Log($"SDFParticleRecorder: Auto-exporting on play mode exit ({_recordedFrames.Count} frames recorded)");
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
