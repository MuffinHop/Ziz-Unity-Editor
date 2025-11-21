using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Binary file header for Actor data files (.act) 
/// Version 6: Mesh data only - all transforms baked into vertex animation in RAT files
/// No transform keyframes stored - animation is purely vertex-based
/// Texture filename stored in this header is the base filename only; the texture file is expected to be in the same folder as the .act file.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ActorHeader
{
    public uint magic;              // 'ACTR' = 0x52544341
    public uint version;            // File format version (6 - mesh data only)
    public uint num_rat_files;      // Number of RAT files referenced
    public uint rat_filenames_length; // Total length of all RAT filename strings
    public float framerate;         // Animation framerate (no keyframes needed)
    
    // Mesh data offsets and counts
    public uint num_vertices;       // Number of vertices in mesh
    public uint num_indices;        // Number of triangle indices
    public uint mesh_uvs_offset;    // Offset to UV data (float pairs)
    public uint mesh_colors_offset; // Offset to color data (float quads)
    public uint mesh_indices_offset; // Offset to triangle indices (ushort triplets)
    public uint texture_filename_offset; // Offset to texture filename string
    public uint texture_filename_length; // Length of texture filename
    
    // Rendering mode
    public byte rendering_mode;     // ActorRenderingMode enum value (0-7)
    
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
    public byte[] reserved;         // Padding
}

/// <summary>
/// Data structure to hold all actor animation data before saving
/// Version 6: Mesh data only, no transforms
/// </summary>
[System.Serializable]
public class ActorAnimationData
{
    public List<string> ratFilePaths = new List<string>();  // List of RAT file paths
    public float framerate;                                   // Animation framerate
    
    // Mesh data (static across all frames)
    public Vector2[] meshUVs;
    public Color[] meshColors;
    public int[] meshIndices;
    public string textureFilename = "";
    
    public ActorAnimationData()
    {
        framerate = 30f;
    }
}

// Actor rendering mode enum is provided by Rat.ActorRenderingMode

public class Actor : MonoBehaviour
{
    // Export: vertex animation and per-frame transforms are stored in RAT files (ACT contains mesh + RAT refs).
    // The .act file contains static mesh data + references to the RAT files (no transforms).
    // If an Actor has an Animator plus external transform modifications, recordUntilManualStop can be used
    // to ensure the full transform motion over the entire play session is captured (default: false).
    public ActorAnimationData AnimationData { get; private set; } = new ActorAnimationData();
    
    [Header("Recording Settings")]
    public bool record = false;
    [Tooltip("If true, recording continues until StopRecording is called, even if the animation clip is shorter.")]
    public bool recordUntilManualStop = true;
    public bool exportBinary = true; // If true, exports .act and .rat files. If false, only records in memory.

    [Tooltip("Target framerate for recording (e.g. 30 or 60). Set to 0 to capture every frame.")]
    public float targetFramerate = 30.0f;

    [Header("Compression Settings")]
    [Tooltip("Maximum bits per axis for delta compression (4-8). Lower = smaller files but less precision. 6 is good for most animations.")]
    [Range(4, 8)]
    public int maxBitsPerAxis = 6;  // Default to 6 bits instead of 8
    
    [Tooltip("Maximum size per RAT file chunk in KB. Smaller = more files but better for memory-constrained systems.")]
    [Range(8, 128)]
    public int maxChunkSizeKB = 32;  // Default to 32KB instead of 64KB
    
    [Tooltip("Capture every Nth frame (1 = all frames, 2 = every other frame). Higher = smaller files but choppier animation.")]
    [Range(1, 4)]
    public int frameCaptureInterval = 1;  // 1 = capture every frame, 2 = skip every other frame

    [Header("Material & Rendering Settings")]
    [Tooltip("Choose the material and lighting mode for this actor")]
    public Rat.ActorRenderingMode renderingMode = Rat.ActorRenderingMode.TextureWithDirectionalLight;

    // Auto-generated fields (not shown in Inspector)
    private string ratFilePath = "";    // Auto-generated from transform name
    private string baseFilename = "";   // Auto-generated from transform name
    [SerializeField]
    private string textureFilename = ""; // Auto-generated texture filename (serialized so editor changes persist)

    // Non-serialized fields
    // Note: recorded transform fields (position, rotation, scale) are stored in world space.
    // - position: world position (transform.position)
    // - rotation: world Euler angles (transform.eulerAngles)
    // - scale: effective world scale (transform.lossyScale)
    private long currentKeyFrame;
    private MeshRenderer meshRenderer;
    private SkinnedMeshRenderer skinnedMeshRenderer;
    private Animator animator;
    private Vector3 position;
    private Vector3 rotation;
    private Vector3 scale;
    private string lastTransformName = ""; // Track transform name changes
    
    // Animation recording data
    private bool isRecording = false;
    private float recordingStartTime;
    private float lastCaptureTime;
    private uint recordedFrameCount;
    private float animationDuration = 0f;  // Total duration of the animation
    private RatRecorder ratRecorder;
    
    // Per-frame vertex capture
    private List<Vector3[]> capturedVertexFrames = new List<Vector3[]>();
    private List<Rat.ActorTransformFloat> capturedTransformFrames = new List<Rat.ActorTransformFloat>();
    private List<Matrix4x4> capturedWorldMatrices = new List<Matrix4x4>(); // diagnostic only
    private Mesh workingMesh;  // Cache for accessing vertex data

    // Getters
    public Vector3 Position => position;
    public Vector3 Rotation => rotation;
    public Vector3 Scale => scale;
    public long CurrentKeyFrame => currentKeyFrame;
    public string RatFilePath => ratFilePath;
    public string TextureFilename => textureFilename;
    public bool IsRecording => isRecording;
    public string BaseFilename => baseFilename;
    public float AnimationDuration => animationDuration;
    
    // Renderer properties
    public MeshRenderer MeshRenderer => meshRenderer;
    public SkinnedMeshRenderer SkinnedMeshRenderer => skinnedMeshRenderer;
    public Animator Animator => animator;
    
    /// <summary>
    /// Returns true if this Actor has either a MeshRenderer or SkinnedMeshRenderer attached
    /// </summary>
    public bool HasValidRenderer => meshRenderer != null || skinnedMeshRenderer != null;

    public Actor()
    {
        currentKeyFrame = 0;
    }

    private void Awake()
    {
        // Validate and get required components
        ValidateAndSetupComponents();
        
        // Auto-generate filenames based on transform name
        UpdateFilenamesFromTransform();
        lastTransformName = transform.name;
        
        // Initialize working mesh cache for skinned meshes. Even if sharedMesh is not set yet
        // we still create a working mesh to be safe (BakeMesh will write into this mesh).
        if (skinnedMeshRenderer != null)
        {
            workingMesh = new Mesh();
        }
    }
    
    /// <summary>
    /// Updates the RAT file path and base filename based on the transform's name
    /// </summary>
    private void UpdateFilenamesFromTransform()
    {
        string transformName = transform.name;
        
        // Clean the name for use as filename (remove invalid characters)
        string cleanName = System.Text.RegularExpressions.Regex.Replace(transformName, @"[<>:""/\\|?*]", "_");
        
        baseFilename = cleanName;
        ratFilePath = $"GeneratedData/{cleanName}.rat";
        
        // Generate texture filename and process texture
        GenerateAndProcessTexture(cleanName);
        
    Debug.Log($"Actor {name} - generated filenames: Base={baseFilename}, RAT={ratFilePath}, Texture={textureFilename}");
    }
    
    /// <summary>
    /// Generates optimized texture filename and processes texture using TextureProcessor
    /// </summary>
    private void GenerateAndProcessTexture(string cleanName)
    {
    // MatCap mode samples _MainTex when provided. Allow texture processing for MatCap.
        
        // NOTE: matrix/transform detection intentionally moved into ExportCapturedFrames.
        try
        {
            // NOTE: matrix/transform detection intentionally moved into ExportCapturedFrames.
            // First, try to extract texture from the actor's material directly
            Texture2D sourceTexture = null;
            
            // Check the renderer's material
            if (meshRenderer != null && meshRenderer.sharedMaterial != null)
            {
                Texture mainTex = meshRenderer.sharedMaterial.GetTexture("_MainTex");
                if (mainTex != null)
                {
                    sourceTexture = mainTex as Texture2D;
                }
            }
            
            // Fallback: check skinned mesh renderer
            if (sourceTexture == null && skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMaterial != null)
            {
                Texture mainTex = skinnedMeshRenderer.sharedMaterial.GetTexture("_MainTex");
                if (mainTex != null)
                {
                    sourceTexture = mainTex as Texture2D;
                }
            }
            
            // Last resort: use TextureProcessor to extract from GameObject and children
            if (sourceTexture == null)
            {
                sourceTexture = TextureProcessor.ExtractTextureFromGameObject(gameObject, true);
            }
            
            if (sourceTexture != null)
            {
                // Create GeneratedData directory if it doesn't exist
                string generatedDataPath = System.IO.Path.Combine(Application.dataPath.Replace("Assets", ""), "GeneratedData");
                if (!System.IO.Directory.Exists(generatedDataPath))
                {
                    System.IO.Directory.CreateDirectory(generatedDataPath);
                }
                
                // Save texture directly to GeneratedData as PNG
                string outputPath = System.IO.Path.Combine("GeneratedData", $"{cleanName}.png");
                string fullPath = System.IO.Path.Combine(Application.dataPath.Replace("Assets", ""), outputPath);
                
                // Read the texture data
                byte[] pngData = null;
                
                // If the texture is readable, encode it directly
                if (sourceTexture.isReadable)
                {
                    pngData = sourceTexture.EncodeToPNG();
                    if (pngData != null && pngData.Length > 0)
                    {
                        System.IO.File.WriteAllBytes(fullPath, pngData);
                        textureFilename = $"{cleanName}.png";
                        Debug.Log($"Actor {name} - exported readable texture: {sourceTexture.name} to {outputPath} ({pngData.Length} bytes)");
                    }
                    else
                    {
                        Debug.LogWarning($"Actor {name} - failed to encode texture to PNG.");
                        GenerateFallbackTexture(cleanName);
                    }
                }
                else
                {
                    // If not readable, we need to read it via RenderTexture
                    RenderTexture tempRT = RenderTexture.GetTemporary(sourceTexture.width, sourceTexture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    Graphics.Blit(sourceTexture, tempRT);
                    
                    RenderTexture prev = RenderTexture.active;
                    RenderTexture.active = tempRT;
                    
                    Texture2D readableTexture = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.ARGB32, false);
                    readableTexture.ReadPixels(new Rect(0, 0, sourceTexture.width, sourceTexture.height), 0, 0, false);
                    readableTexture.Apply(false, false);
                    
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(tempRT);
                    
                    pngData = readableTexture.EncodeToPNG();
                    if (pngData != null && pngData.Length > 0)
                    {
                        System.IO.File.WriteAllBytes(fullPath, pngData);
                        textureFilename = $"{cleanName}.png";
                        Debug.Log($"Actor {name} - exported non-readable texture: {sourceTexture.name} to {outputPath} ({pngData.Length} bytes)");
                    }
                    else
                    {
                        Debug.LogWarning($"Actor {name} - failed to encode texture to PNG.");
                        GenerateFallbackTexture(cleanName);
                    }
                    
                    DestroyImmediate(readableTexture);
                }
            }
            else
            {
                Debug.LogWarning($"Actor {name} - no texture found on object or children; using fallback filename.");
                GenerateFallbackTexture(cleanName);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Actor {name} - texture export failed: {e.Message}\n{e.StackTrace}");
            GenerateFallbackTexture(cleanName);
        }
    }
    
    /// <summary>
    /// Generates fallback texture filename when texture export fails or no texture is found
    /// </summary>
    private void GenerateFallbackTexture(string cleanName)
    {
        // Use naming convention: actor name + .png
        textureFilename = $"{cleanName}.png";
    Debug.Log($"Actor {name} - using fallback texture filename: {textureFilename}");
    }

    /// <summary>
    /// Validates that we have the required components and sets them up
    /// </summary>
    private void ValidateAndSetupComponents()
    {
        // Get mesh renderer components
        // Prefer components on the same GameObject but allow renderers in children (include inactive)
        meshRenderer = GetComponent<MeshRenderer>() ?? GetComponentInChildren<MeshRenderer>(true);
        skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>() ?? GetComponentInChildren<SkinnedMeshRenderer>(true);
        
        // Search for animator in this GameObject and its parents
        animator = GetComponentInParent<Animator>();
        
        // REQUIRE at least one renderer - this is non-negotiable
        if (meshRenderer == null && skinnedMeshRenderer == null)
        {
            // Try a best-effort fallback for odd hierarchies (imported rigs, nested renderers, etc.)
            var anyRenderer = GetComponentInChildren<Renderer>(true);
            if (anyRenderer != null)
            {
                if (anyRenderer is SkinnedMeshRenderer anySmr)
                {
                    skinnedMeshRenderer = anySmr;
                    Debug.LogWarning($"Actor {name} - detected a SkinnedMeshRenderer on a child; using it for recording and shader setup.");
                }
                else if (anyRenderer is MeshRenderer anyMr)
                {
                    meshRenderer = anyMr;
                    Debug.LogWarning($"Actor {name} - detected a MeshRenderer on a child; using it for recording and shader setup.");
                }
                else
                {
                    Debug.LogWarning($"Actor {name} - found a Renderer of type {anyRenderer.GetType().Name} on a child; attempting to use it.");
                }
            }
            Debug.LogError($"Actor {name} requires a MeshRenderer or SkinnedMeshRenderer component.");
            
#if UNITY_EDITOR
            // In editor, throw an exception to prevent the component from being added
            if (!Application.isPlaying)
            {
                throw new System.InvalidOperationException($"Actor component cannot be added to '{name}' without a MeshRenderer or SkinnedMeshRenderer!");
            }
#endif
            
            // At runtime, try to add a MeshRenderer as fallback
            Debug.LogWarning($"Adding MeshRenderer to '{name}' as fallback...");
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            
            // Also add MeshFilter if it doesn't exist (required by MeshRenderer)
            if (GetComponent<MeshFilter>() == null)
            {
                gameObject.AddComponent<MeshFilter>();
            }
        }
        
        // Warn if no animator is found (optional but recommended)
        if (animator == null)
        {
            Debug.LogWarning($"Actor {name} has no Animator; add one for animation control if needed.");
        }
        
        // Set up the shader and material for this rendering mode
        SetupShaderAndMaterial();
    }
    
    /// <summary>
    /// Sets up the correct shader and material for the current rendering mode
    /// </summary>
    private void SetupShaderAndMaterial()
    {
        // Get the renderer component
        Renderer renderer = meshRenderer != null ? (Renderer)meshRenderer : (Renderer)skinnedMeshRenderer;

        // As a fallback, try to find any renderer in children (include inactive) — some rigs place renderers deep in the hierarchy
        if (renderer == null)
        {
            var anyRenderer = GetComponentInChildren<Renderer>(true);
            if (anyRenderer != null)
            {
                renderer = anyRenderer;
                if (anyRenderer is SkinnedMeshRenderer anySmr) skinnedMeshRenderer = anySmr;
                else if (anyRenderer is MeshRenderer anyMr) meshRenderer = anyMr;
                Debug.LogWarning($"Actor {name} - SetupShaderAndMaterial found child renderer: {anyRenderer.GetType().Name}; using it for material setup.");
            }
        }

        if (renderer == null)
        {
            Debug.LogError($"Actor {name} - no renderer found for shader setup.");
            return;
        }
        
        // Map rendering mode to shader name
        string shaderName = GetShaderNameForRenderingMode(renderingMode);
        
        if (string.IsNullOrEmpty(shaderName))
        {
            Debug.LogError($"Actor {name} - unknown rendering mode: {renderingMode}");
            return;
        }
        
        // Load the shader
        Shader shader = Shader.Find(shaderName);
        
        if (shader == null)
        {
            Debug.LogWarning($"Actor {name} - shader '{shaderName}' not found yet; skipping material setup.");
            return;
        }
        
        // Create a new material with this shader
        Material material = new Material(shader);
        material.name = $"{gameObject.name}_{renderingMode}";
        
        // Load and assign texture if the rendering mode uses textures
        if (RequiresTexture(renderingMode))
        {
            Texture2D texture = LoadTextureForActor();
            if (texture != null)
            {
                material.SetTexture("_MainTex", texture);
                Debug.Log($"Actor {name} - applied texture {texture.name} to {renderingMode} shader");
                // If we have a material-assigned main texture but no explicit textureFilename configured, set it (Editor only)
#if UNITY_EDITOR
                if (string.IsNullOrEmpty(textureFilename))
                {
                    string path = UnityEditor.AssetDatabase.GetAssetPath(texture);
                    if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/"))
                        textureFilename = path.Substring("Assets/".Length);
                }
#endif
            }
        }
        
        // Load and assign matcap texture if needed
        if (renderingMode == Rat.ActorRenderingMode.MatCap)
        {
            // Prefer the explicit main texture set on the actor (if any)
            Texture2D matcap = LoadTextureForActor();
            if (matcap == null)
            {
                // fallback to a discovered matcap in the project
                matcap = LoadMatcapTexture();
            }
            if (matcap != null)
            {
                // Modern MatCap shaders sample from _MainTex - ensure both properties are set so both shader variants work
                material.SetTexture("_MainTex", matcap);
                material.SetTexture("_Matcap", matcap);
                Debug.Log($"Actor {name} - applied matcap: {matcap.name}");
            }
        }
        
        // Apply the material to all materials on the renderer
        // For multi-submesh renderers we create clones of the base material for each slot
        try
        {
            int slots = 1;
            if (renderer is SkinnedMeshRenderer smr)
            {
                slots = Mathf.Max(1, smr.sharedMaterials?.Length ?? 1);
            }
            else if (renderer is MeshRenderer mr)
            {
                slots = Mathf.Max(1, mr.sharedMaterials?.Length ?? 1);
            }

            Material[] mats = new Material[slots];
            for (int i = 0; i < slots; ++i)
            {
                // Create an instance per slot so artists can tweak them at runtime independently
                Material instance = new Material(material);
                instance.name = (slots == 1) ? material.name : $"{material.name}_slot{i}";
                // If we have a texture to apply, set it on each instance
                if (RequiresTexture(renderingMode))
                {
                    Texture2D tex = LoadTextureForActor();
                    if (tex != null) instance.SetTexture("_MainTex", tex);
                }
                // Also set matcap if necessary
                if (renderingMode == Rat.ActorRenderingMode.MatCap)
                {
                    Texture2D matcap = LoadMatcapTexture();
                    if (matcap != null)
                    {
                        instance.SetTexture("_Matcap", matcap);
                    }
                }

                mats[i] = instance;
            }

            // Assign the created material instances to the renderer
            renderer.materials = mats;
        }
        catch (System.Exception e)
        {
            // Fallback: assign single material
            Debug.LogWarning($"Actor {name} - failed to apply multi-material instances, falling back to single material: {e.Message}");
            renderer.material = material;
        }
        
    Debug.Log($"Actor {name} - shader '{shaderName}' set for rendering mode {renderingMode}");
    }

    /// <summary>
    /// Sets the actor's texture filename (path relative to Assets/) and applies it to material.
    /// </summary>
    public void SetTextureFilename(string relativePath)
    {
        textureFilename = relativePath ?? "";
        // Re-apply shader/material to pick up texture change
        SetupShaderAndMaterial();
    }

    /// <summary>
    /// Sets rendering mode and updates the material/shader.
    /// </summary>
    public void SetRenderingMode(Rat.ActorRenderingMode mode)
    {
        renderingMode = mode;
        // If switching to MatCap, ensure we have a texture. In the Editor, try to auto-select a project MatCap if none set.
#if UNITY_EDITOR
        if (renderingMode == Rat.ActorRenderingMode.MatCap && string.IsNullOrEmpty(textureFilename))
        {
            // Try to auto-select a MatCap texture in the project
            string[] guids = UnityEditor.AssetDatabase.FindAssets("MatCap t:Texture2D");
            if (guids.Length > 0)
            {
                string matcapPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                if (!string.IsNullOrEmpty(matcapPath) && matcapPath.StartsWith("Assets/"))
                {
                    SetTextureFilename(matcapPath.Substring("Assets/".Length));
                }
            }
        }
#endif
        SetupShaderAndMaterial();
    }

    /// <summary>
    /// Returns the currently assigned main texture (either from chosen textureFilename or from material).
    /// </summary>
    public Texture2D GetAssignedTexture()
    {
        return LoadTextureForActor();
    }
    
    /// <summary>
    /// Gets the shader name for a given rendering mode
    /// </summary>
    private static string GetShaderNameForRenderingMode(Rat.ActorRenderingMode mode)
    {
        return mode switch
        {
            Rat.ActorRenderingMode.VertexColoursOnly => "Actor/VertexColoursOnly",
            Rat.ActorRenderingMode.VertexColoursWithDirectionalLight => "Actor/VertexColoursWithDirectionalLight",
            Rat.ActorRenderingMode.VertexColoursWithVertexLighting => "Actor/VertexColoursWithVertexLighting",
            Rat.ActorRenderingMode.TextureOnly => "Actor/TextureOnly",
            Rat.ActorRenderingMode.TextureAndVertexColours => "Actor/TextureAndVertexColours",
            Rat.ActorRenderingMode.TextureWithDirectionalLight => "Actor/TextureWithDirectionalLight",
            Rat.ActorRenderingMode.TextureAndVertexColoursAndDirectionalLight => "Actor/TextureAndVertexColoursAndDirectionalLight",
            Rat.ActorRenderingMode.MatCap => "Actor/MatCap",
            _ => null
        };
    }
    
    /// <summary>
    /// Determines if a rendering mode requires a main texture
    /// </summary>
    private static bool RequiresTexture(Rat.ActorRenderingMode mode)
    {
        return mode switch
        {
            Rat.ActorRenderingMode.TextureOnly => true,
            Rat.ActorRenderingMode.TextureAndVertexColours => true,
            Rat.ActorRenderingMode.TextureWithDirectionalLight => true,
            Rat.ActorRenderingMode.TextureAndVertexColoursAndDirectionalLight => true,
            _ => false
        };
    }
    
    /// <summary>
    /// Loads a texture for this actor from the file system
    /// </summary>
    private Texture2D LoadTextureForActor()
    {
#if UNITY_EDITOR
        // First try the generated/optimized texture filename
        if (!string.IsNullOrEmpty(textureFilename))
        {
            string texturePath = Path.Combine("Assets", textureFilename);
            Texture2D texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture != null)
            {
                return texture;
            }
        }
#endif
        
        // Try to extract from existing material
        if (meshRenderer != null && meshRenderer.sharedMaterial != null)
        {
            Texture mainTex = meshRenderer.sharedMaterial.GetTexture("_MainTex");
            if (mainTex != null)
            {
                return mainTex as Texture2D;
            }
        }
        
        if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMaterial != null)
        {
            Texture mainTex = skinnedMeshRenderer.sharedMaterial.GetTexture("_MainTex");
            if (mainTex != null)
            {
                return mainTex as Texture2D;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Loads a matcap texture for this actor
    /// </summary>
    private Texture2D LoadMatcapTexture()
    {
        // Try to find a matcap texture in the project
        string[] matcapGuids = UnityEditor.AssetDatabase.FindAssets("MatCap t:Texture2D");
        
        if (matcapGuids.Length > 0)
        {
            // Use the first matcap found
            string matcapPath = UnityEditor.AssetDatabase.GUIDToAssetPath(matcapGuids[0]);
            Texture2D matcap = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(matcapPath);
            if (matcap != null)
            {
                return matcap;
            }
        }
        
        // Try specific matcap paths
        string[] potentialPaths = new[]
        {
            "Assets/MatCaps/default.png",
            "Assets/MatCaps/matcap.png",
            "Assets/Textures/MatCap.png",
            "Assets/Shaders/MatCap.png"
        };
        
        foreach (string path in potentialPaths)
        {
            Texture2D matcap = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (matcap != null)
            {
                return matcap;
            }
        }
        
    Debug.LogWarning($"Actor {name} - no matcap texture found; using white fallback.");
        return null;
    }
    
    /// <summary>
    /// Records the current transform as a keyframe
    /// </summary>
    private void RecordCurrentTransform()
    {
        if (!isRecording) return;
        
        // v6 ACT: No transforms stored, all animation is in RAT vertex data
        recordedFrameCount++;
    }

    /// <summary>
    /// Converts a float value to 16-bit fixed-point within the given range
    /// </summary>
    private static ushort FloatToFixed16(float value, float minValue, float maxValue)
    {
        if (maxValue <= minValue) return 0;
        
        value = Mathf.Clamp(value, minValue, maxValue);
        float normalized = (value - minValue) / (maxValue - minValue);
        return (ushort)Mathf.RoundToInt(normalized * 65535f);
    }
    
    /// <summary>
    /// Converts a 16-bit fixed-point value back to float within the given range
    /// </summary>
    private static float Fixed16ToFloat(ushort fixedValue, float minValue, float maxValue)
    {
        if (maxValue <= minValue) return minValue;
        float normalized = fixedValue / 65535f;
        return minValue + normalized * (maxValue - minValue);
    }
    
    /// <summary>
    /// Converts rotation from degrees (0-360) to 16-bit fixed-point
    /// </summary>
    private static ushort DegreesToFixed16(float degrees)
    {
        degrees = degrees % 360f;
        if (degrees < 0) degrees += 360f;
        return (ushort)Mathf.RoundToInt((degrees / 360f) * 65535f);
    }
    
    /// <summary>
    /// Converts 16-bit fixed-point back to degrees (0-360)
    /// </summary>
    private static float Fixed16ToDegrees(ushort fixedValue)
    {
        return (fixedValue / 65535f) * 360f;
    }

    public void Update()
    {
        position = transform.position;
        rotation = transform.eulerAngles;
    scale = transform.lossyScale;
        
        // Check if transform name changed and update filenames accordingly
        if (transform.name != lastTransformName)
        {
            UpdateFilenamesFromTransform();
            lastTransformName = transform.name;
        }
        
        // Update current keyframe based on animator state
        UpdateCurrentKeyFrame();
        
        // Start recording when entering play mode
        if (!isRecording && Application.isPlaying && HasValidRenderer)
        {
            StartVertexRecording();
        }
        
    }

    private void LateUpdate()
    {
        if (!isRecording)
            return;

        bool shouldCapture = false;
        float rate = GetTargetRecordingFramerate();

        if (rate > 0)
        {
            // Time-based capture
            if (Time.time - lastCaptureTime >= (1f / rate))
            {
                shouldCapture = true;
                lastCaptureTime = Time.time;
            }
        }
        else
        {
            // Frame-based capture (every frame, subject to frameCaptureInterval in CaptureCurrentFrame)
            shouldCapture = true;
        }

        if (shouldCapture)
        {
            CaptureCurrentFrame();
        }
    }
    
    private void OnApplicationPause(bool pauseStatus)
    {
        // Save files when application is paused (which includes exiting play mode)
        if (pauseStatus && isRecording)
        {
            StopVertexRecording();
        }
    }
    
    private void OnDestroy()
    {
        // Save files when component is destroyed (including when exiting play mode)
        if (isRecording && capturedVertexFrames.Count > 0)
        {
            StopVertexRecording();
        }
    }
    
    /// <summary>
    /// Updates the current keyframe based on the animator's current state
    /// </summary>
    private void UpdateCurrentKeyFrame()
    {
        if (animator == null)
        {
            // If no animator, keyframe remains static
            return;
        }
        
        // Get the current animator state info
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        
        if (stateInfo.length > 0)
        {
            // Get the actual frame rate from the current animation clip
            float frameRate = GetCurrentAnimationFrameRate();
            float currentTime = stateInfo.normalizedTime * stateInfo.length;
            currentKeyFrame = (long)(currentTime * frameRate);
        }
        else
        {
            currentKeyFrame = 0;
        }
    }
    
    /// <summary>
    /// Gets the frame rate of the currently playing animation clip
    /// </summary>
    private float GetCurrentAnimationFrameRate()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return 30f; // Fallback to 30 FPS
            
        // Get current state info
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        
        // Try to get the animation clip from the animator controller
        AnimatorClipInfo[] clipInfos = animator.GetCurrentAnimatorClipInfo(0);
        
        if (clipInfos.Length > 0)
        {
            AnimationClip clip = clipInfos[0].clip;
            if (clip != null)
            {
                return clip.frameRate;
            }
        }
        
        // Fallback to 30 FPS if we can't determine the actual frame rate
        return 30f;
    }
    
    /// <summary>
    /// Gets the current keyframe for a specific animation layer
    /// </summary>
    public long GetCurrentKeyFrame(int layerIndex = 0)
    {
        if (animator == null) return 0;
        
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(layerIndex);
        if (stateInfo.length > 0)
        {
            float frameRate = GetAnimationFrameRate(layerIndex);
            float currentTime = stateInfo.normalizedTime * stateInfo.length;
            return (long)(currentTime * frameRate);
        }
        
        return 0;
    }
    
    /// <summary>
    /// Sets the animator to a specific keyframe (useful for scene editing)
    /// </summary>
    public void SetKeyFrame(long keyFrame, int layerIndex = 0)
    {
        if (animator == null) return;
        
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(layerIndex);
        if (stateInfo.length > 0)
        {
            float frameRate = GetAnimationFrameRate(layerIndex);
            float targetTime = keyFrame / frameRate;
            float normalizedTime = targetTime / stateInfo.length;
            
            // Play the animation at the specific normalized time
            animator.Play(stateInfo.fullPathHash, layerIndex, normalizedTime);
            currentKeyFrame = keyFrame;
        }
    }
    
    /// <summary>
    /// Gets the frame rate of the animation playing on a specific layer
    /// </summary>
    private float GetAnimationFrameRate(int layerIndex = 0)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return 30f; // Fallback to 30 FPS
            
        // Try to get the animation clip from the animator controller
        AnimatorClipInfo[] clipInfos = animator.GetCurrentAnimatorClipInfo(layerIndex);
        
        if (clipInfos.Length > 0)
        {
            AnimationClip clip = clipInfos[0].clip;
            if (clip != null)
            {
                return clip.frameRate;
            }
        }
        
        // Fallback to 30 FPS if we can't determine the actual frame rate
        return 30f;
    }
    
    /// <summary>
    /// Gets the current animation's frame rate (public for debugging/inspection)
    /// </summary>
    public float GetCurrentFrameRate(int layerIndex = 0)
    {
        return GetAnimationFrameRate(layerIndex);
    }
    
    /// <summary>
    /// Gets the duration of the currently playing animation clip
    /// </summary>
    public float GetAnimationDuration(int layerIndex = 0)
    {
        // Return recorded duration instead of animator clip duration
        if (isRecording && recordingStartTime > 0)
        {
            return Time.time - recordingStartTime;
        }
        
        if (animator == null || animator.runtimeAnimatorController == null)
            return 5.0f; // Fallback duration
            
        // Try to get the animation clip from the animator controller
        AnimatorClipInfo[] clipInfos = animator.GetCurrentAnimatorClipInfo(layerIndex);
        
        if (clipInfos.Length > 0)
        {
            AnimationClip clip = clipInfos[0].clip;
            if (clip != null)
            {
                return clip.length;
            }
        }
        
        // Fallback to 5 seconds if we can't determine the actual duration
        return 5.0f;
    }
    
    /// <summary>
    /// Calculates the total number of frames in the animation
    /// </summary>
    public uint GetTotalFrames(int layerIndex = 0)
    {
        float duration = GetAnimationDuration(layerIndex);
        float frameRate = GetAnimationFrameRate(layerIndex);
        return (uint)Mathf.CeilToInt(duration * frameRate);
    }
    
    /// <summary>
    /// Determines the target framerate for recording.
    /// Prioritizes Animator's clip framerate if available, otherwise uses targetFramerate setting.
    /// </summary>
    private float GetTargetRecordingFramerate()
    {
        // 1. If Animator is present and playing a clip, use that clip's framerate
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            // Check if we can actually get a clip framerate
            AnimatorClipInfo[] clipInfos = animator.GetCurrentAnimatorClipInfo(0);
            if (clipInfos.Length > 0 && clipInfos[0].clip != null)
            {
                return clipInfos[0].clip.frameRate;
            }
        }
        
        // 2. Otherwise use the manual setting
        return targetFramerate;
    }

    /// <summary>
    /// Starts recording vertex positions every frame
    /// </summary>
    private void StartVertexRecording()
    {
        if (isRecording) return;
        
        // Update filenames based on current transform name
        UpdateFilenamesFromTransform();
        
        // Clear any previous capture data
        capturedVertexFrames.Clear();
        capturedTransformFrames.Clear();
        capturedWorldMatrices.Clear();
        
        isRecording = true;
        recordingStartTime = Time.time;
        
        // Initialize lastCaptureTime to ensure immediate capture of the first frame
        float rate = GetTargetRecordingFramerate();
        float interval = rate > 0 ? (1f / rate) : 0f;
        lastCaptureTime = Time.time - interval;
        
        recordedFrameCount = 0;
        frameSkipCounter = 0;
        
        Debug.Log($"Actor {name} - started vertex recording (local vertices + per-frame world transforms)");
    }
    
    /// <summary>
    /// Captures the current frame's vertex positions in world space
    /// </summary>
    private void CaptureCurrentFrame()
    {
        // Skip frames based on frameCaptureInterval setting ONLY if not using targetFramerate
        float rate = GetTargetRecordingFramerate();
        if (rate <= 0)
        {
            frameSkipCounter++;
            if (frameSkipCounter < frameCaptureInterval)
            {
                return;
            }
            frameSkipCounter = 0;
        }
        
        Vector3[] frameVertices = null;
        
        if (skinnedMeshRenderer != null)
        {
            // For skinned mesh, bake the current pose into working mesh
            skinnedMeshRenderer.BakeMesh(workingMesh);
            
            // Get vertices in local space
            Vector3[] localVertices = workingMesh.vertices;
            frameVertices = new Vector3[localVertices.Length];
            Array.Copy(localVertices, frameVertices, localVertices.Length);
        }
        else if (meshRenderer != null)
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                Vector3[] localVertices = meshFilter.sharedMesh.vertices;
                frameVertices = new Vector3[localVertices.Length];
                Array.Copy(localVertices, frameVertices, localVertices.Length);
            }
        }
        
        if (frameVertices != null)
        {
            uint localFrameIndex = (uint)capturedVertexFrames.Count;
            capturedVertexFrames.Add(frameVertices);
            capturedTransformFrames.Add(new Rat.ActorTransformFloat
            {
                position = transform.position,
                rotation = transform.eulerAngles,
                scale = transform.lossyScale,
                rat_file_index = 0,
                rat_local_frame = localFrameIndex
            });
            capturedWorldMatrices.Add(transform.localToWorldMatrix);
            recordedFrameCount++;
        }
    }
    
    /// <summary>
    /// Stops recording and saves both RAT and Actor data to files
    /// </summary>
    private void StopVertexRecording()
    {
        if (!isRecording) return;
        
        isRecording = false;
        
        float recordingDuration = Time.time - recordingStartTime;
        
        // Determine the framerate to use for export
        float effectiveFramerate;
        float targetRate = GetTargetRecordingFramerate();
        
        if (targetRate > 0)
        {
            // If we were targeting a specific framerate, use that exactly
            // This avoids floating point jitter and lag artifacts in the recorded framerate
            effectiveFramerate = targetRate;
        }
        else
        {
            // If capturing every frame (rate <= 0), calculate the effective playback framerate
            effectiveFramerate = capturedVertexFrames.Count / Mathf.Max(recordingDuration, 0.001f);
        }
        
        // ExportCapturedFrames expects a "source" framerate that it will divide by frameCaptureInterval
        // So we multiply here to counteract that division
        float exportFramerate = effectiveFramerate * frameCaptureInterval;
        
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            float animRate = GetCurrentAnimationFrameRate();
            Debug.Log($"Actor {name} - framerate: {effectiveFramerate:F1} FPS (Animator target was {animRate:F1} FPS)");
        }
        else
        {
            Debug.Log($"Actor {name} - framerate: {effectiveFramerate:F1} FPS");
        }
        
        Debug.Log($"Actor {name} - recorded {recordedFrameCount} updates over {recordingDuration:F2}s");
        Debug.Log($"Actor {name} - captured {capturedVertexFrames.Count} vertex frames with {capturedTransformFrames.Count} transform snapshots");
        
        // Export to RAT and ACT files with calculated framerate
        ExportCapturedFrames(exportFramerate);
    }
    
    /// <summary>
    /// Exports captured vertex frames to RAT and ACT files
    /// </summary>
    private void ExportCapturedFrames(float framerate)
    {
        if (capturedVertexFrames.Count == 0)
        {
            Debug.LogWarning($"Actor {name} - no frames captured, skipping export");
            return;
        }
        
        // Get source mesh for UVs, colors, and indices
        Mesh sourceMesh = null;
        Vector2[] uvs = null;
        Color[] colors = null;
        
        if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null)
        {
            sourceMesh = skinnedMeshRenderer.sharedMesh;
        }
        else if (meshRenderer != null)
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                sourceMesh = meshFilter.sharedMesh;
            }
        }
        
        if (sourceMesh == null)
        {
            Debug.LogError($"Actor {name} - cannot export: no source mesh found");
            return;
        }
        
        // Extract UVs and colors from source mesh
        uvs = sourceMesh.uv.Length > 0 ? sourceMesh.uv : new Vector2[sourceMesh.vertexCount];
        colors = sourceMesh.colors.Length > 0 ? sourceMesh.colors : Enumerable.Repeat(Color.white, sourceMesh.vertexCount).ToArray();

        if (capturedTransformFrames.Count == 0)
        {
            Debug.LogError($"Actor {name} - no transform frames captured; cannot bake world transforms.");
            return;
        }

        List<Vector3[]> framesToExport = capturedVertexFrames;
        List<Rat.ActorTransformFloat> transformsToExport = capturedTransformFrames;

        if (framesToExport.Count != transformsToExport.Count)
        {
            int alignedCount = Mathf.Min(framesToExport.Count, transformsToExport.Count);
            Debug.LogWarning($"Actor {name} - frame/transform count mismatch ({framesToExport.Count} vs {transformsToExport.Count}); truncating to {alignedCount} frames.");
            framesToExport = framesToExport.Take(alignedCount).ToList();
            transformsToExport = transformsToExport.Take(alignedCount).ToList();
            if (capturedWorldMatrices.Count > alignedCount)
                capturedWorldMatrices = capturedWorldMatrices.Take(alignedCount).ToList();
        }

        // Diagnostic: compare TRS-built matrix vs captured localToWorldMatrix for scale/rotation mismatch
        bool useMatricesForBaking = false;
        #if UNITY_EDITOR
        if (capturedWorldMatrices != null && capturedWorldMatrices.Count == transformsToExport.Count)
        {
            for (int i = 0; i < transformsToExport.Count; i++)
            {
                var t = transformsToExport[i];
                Matrix4x4 trs = Matrix4x4.TRS(t.position, Quaternion.Euler(t.rotation), t.scale);
                Matrix4x4 captured = capturedWorldMatrices[i];
                // Compare scales roughly by extracting basis lengths
                Vector3 s_trs = new Vector3(trs.GetColumn(0).magnitude, trs.GetColumn(1).magnitude, trs.GetColumn(2).magnitude);
                Vector3 s_cap = new Vector3(captured.GetColumn(0).magnitude, captured.GetColumn(1).magnitude, captured.GetColumn(2).magnitude);
                if (!Mathf.Approximately(s_trs.x, s_cap.x) || !Mathf.Approximately(s_trs.y, s_cap.y) || !Mathf.Approximately(s_trs.z, s_cap.z))
                {
                    useMatricesForBaking = true;
                    Debug.LogWarning($"Actor {name} - TRS vs matrix scale mismatch at frame {i}: TRS={s_trs} captured={s_cap}. Will use matrix baking for accurate export.");
                    break;
                }
            }
        }
        #endif
        
        // Adjust framerate based on frame skip interval
        float adjustedFramerate = framerate / frameCaptureInterval;
        
        Debug.Log($"Actor {name} - starting export of {framesToExport.Count} frames at {adjustedFramerate:F1} FPS (capture interval: {frameCaptureInterval})...");
        
        try
        {
            if (useMatricesForBaking)
            {
                // Use matrix baking for exact world transforms (handles complex parent non-uniform scales)
                Rat.Tool.ExportAnimationWithMaxBits(
                    baseFilename,
                    framesToExport,
                    sourceMesh,
                    uvs,
                    colors,
                    adjustedFramerate,
                    textureFilename,
                    maxChunkSizeKB,
                    renderingMode,
                    maxBitsPerAxis,
                    transformsToExport,
                    flipZ: true,
                    skipValidation: false,
                    yieldPerChunk: false,
                    onComplete: null,
                    customMatrices: capturedWorldMatrices
                );
            }
            else
            {
                Rat.Tool.ExportAnimationWithMaxBits(
                baseFilename,
                framesToExport,
                sourceMesh,
                uvs,
                colors,
                adjustedFramerate,  // Use adjusted framerate
                textureFilename,
                maxChunkSizeKB,  // Use configured chunk size instead of hardcoded 64
                renderingMode,
                maxBitsPerAxis,  // Apply the configured bit width limit
                    transformsToExport,
                    flipZ: true, // Enable Z-flipping for OpenGL compatibility
                    skipValidation: false,
                    yieldPerChunk: false,
                    onComplete: null
            );
            }
            
            // Manually determine created RAT files after export completes
            string generatedDataPath = Path.Combine(Application.dataPath.Replace("Assets", ""), "GeneratedData");
            var ratFiles = new List<string>();
            
            // Check for single file or multi-part files
            string singleFile = Path.Combine(generatedDataPath, $"{baseFilename}.rat");
            if (File.Exists(singleFile))
            {
                ratFiles.Add(Path.GetFileName(singleFile));
            }
            else
            {
                // Check for multi-part files
                int partIndex = 1;
                while (true)
                {
                    string partFile = Path.Combine(generatedDataPath, $"{baseFilename}_part{partIndex:D2}of*.rat");
                    var matches = Directory.GetFiles(generatedDataPath, $"{baseFilename}_part{partIndex:D2}of*.rat");
                    if (matches.Length == 0) break;
                    
                    foreach (var match in matches)
                    {
                        ratFiles.Add(Path.GetFileName(match));
                    }
                    partIndex++;
                }
            }
            
            Debug.Log($"Actor {name} - export complete: {ratFiles.Count} RAT file(s) created");
            
            // Update AnimationData with created files
            AnimationData.ratFilePaths.Clear();
            AnimationData.ratFilePaths.AddRange(ratFiles);
            AnimationData.framerate = adjustedFramerate; // Use the actual playback framerate
            AnimationData.meshUVs = uvs;
            AnimationData.meshColors = colors;
            AnimationData.meshIndices = sourceMesh.triangles;
            AnimationData.textureFilename = textureFilename;
            capturedWorldMatrices.Clear();
            
            // Now save the ACT file with the updated AnimationData
            SaveBothFiles();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Actor {name} - export failed: {e.Message}\n{e.StackTrace}");
        }
    }
    
    /// <summary>
    /// Saves both RAT and Actor files together with synchronized data
    /// </summary>
    public void SaveBothFiles()
    {
        if (AnimationData == null || AnimationData.ratFilePaths.Count == 0)
        {
            Debug.LogError($"Actor {name} - no animation data to save.");
            return;
        }
        
        // Ensure filenames are up to date
        UpdateFilenamesFromTransform();
        
        // Create GeneratedData directory if it doesn't exist
        string generatedDataPath = Path.Combine(Application.dataPath.Replace("Assets", ""), "GeneratedData");
        if (!Directory.Exists(generatedDataPath))
        {
            Directory.CreateDirectory(generatedDataPath);
            Debug.Log($"Created GeneratedData directory at: {generatedDataPath}");
        }
        
        string actorFilePath = $"GeneratedData/{baseFilename}.act";
        
        try
        {
            // Update the RAT file paths in our animation data
            if (AnimationData.ratFilePaths.Count == 0)
            {
                AnimationData.ratFilePaths.Add(ratFilePath);
            }
            
            // Extract mesh data from source mesh if not already present
            if (AnimationData.meshUVs == null || AnimationData.meshIndices == null)
            {
                MeshFilter meshFilter = GetComponent<MeshFilter>();
                SkinnedMeshRenderer skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
                
                Mesh sourceMesh = null;
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    sourceMesh = meshFilter.sharedMesh;
                }
                else if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null)
                {
                    sourceMesh = skinnedMeshRenderer.sharedMesh;
                }
                
                if (sourceMesh != null)
                {
                    // Extract mesh data
                    AnimationData.meshUVs = sourceMesh.uv.Length > 0 ? sourceMesh.uv : new Vector2[sourceMesh.vertexCount];
                    AnimationData.meshColors = sourceMesh.colors.Length > 0 ? sourceMesh.colors : new Color[sourceMesh.vertexCount];
                    AnimationData.meshIndices = sourceMesh.triangles;
                    
                    Debug.Log($"Actor {name} - extracted mesh data: {AnimationData.meshUVs.Length} UVs, {AnimationData.meshIndices.Length} indices");
                }
                else
                {
                    Debug.LogError($"Actor {name} - no source mesh found to extract data from.");
                    return;
                }
            }
            
            // Log diagnostic info
            Debug.Log($"ACT export diagnostic:");
            Debug.Log($"Base filename: {baseFilename}");
            Debug.Log($"RAT file paths: {string.Join(", ", AnimationData.ratFilePaths)}");
            Debug.Log($"Framerate: {AnimationData.framerate}");
            
            // Ensure that MatCap rendering mode has a texture assigned
            if (renderingMode == Rat.ActorRenderingMode.MatCap)
            {
                // Check assigned texture filename or try to load assigned texture from material
                if (string.IsNullOrEmpty(AnimationData.textureFilename))
                {
#if UNITY_EDITOR
                    var assigned = GetAssignedTexture();
                    if (assigned != null)
                    {
                        string path = UnityEditor.AssetDatabase.GetAssetPath(assigned);
                        if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/"))
                            AnimationData.textureFilename = path.Substring("Assets/".Length);
                    }
#endif
                }

                if (string.IsNullOrEmpty(AnimationData.textureFilename))
                {
                    Debug.LogError($"Actor {name} - MatCap mode requires a texture. Assign one before exporting.");
                    return;
                }
            }

            // Save Actor data with mesh information only (no transforms)
            SaveActorData(actorFilePath, AnimationData, renderingMode);
            
            Debug.Log($"Actor {name} - actor file saved:");
            Debug.Log($"  - file: {actorFilePath}");
            Debug.Log($"  - RAT references: {string.Join(", ", AnimationData.ratFilePaths)}");
            Debug.Log($"  - framerate: {AnimationData.framerate} FPS");
            Debug.Log($"  - mesh vertices: {AnimationData.meshUVs?.Length ?? 0}");
            Debug.Log($"  - mesh indices: {AnimationData.meshIndices?.Length ?? 0}");
            Debug.Log($"  - texture: {AnimationData.textureFilename}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Actor {name} - failed to save actor file: {e.Message}");
        }
    }
    
    /// <summary>
    /// Saves actor animation data to a binary file (Version 6 - mesh data only)
    /// All vertex animation and transforms are baked into RAT files
    /// </summary>
    public static void SaveActorData(string filePath, ActorAnimationData data, Rat.ActorRenderingMode renderingMode = Rat.ActorRenderingMode.TextureWithDirectionalLight, bool embedMeshData = true)
    {
        if (!embedMeshData || data.meshUVs == null || data.meshIndices == null)
        {
            Debug.LogError("ActorAnimationData v6 requires mesh data (UVs, colors, indices)!");
            return;
        }
        
        // Convert all RAT filenames to UTF-8 bytes with null terminators
        var ratFileNameBytes = new List<byte>();
        
    Debug.Log($"ACT export: writing {data.ratFilePaths.Count} RAT file references");
        
        foreach (string ratPath in data.ratFilePaths)
        {
            string cleanPath = System.IO.Path.GetFileName(ratPath);
            if (string.IsNullOrEmpty(cleanPath))
            {
                Debug.LogWarning($"  - Path.GetFileName returned empty! Using original path: '{ratPath}'");
                cleanPath = ratPath;
            }
            
            byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(cleanPath);
            Debug.Log($"  - RAT: '{cleanPath}' ({pathBytes.Length} bytes)");
            
            ratFileNameBytes.AddRange(pathBytes);
            ratFileNameBytes.Add(0); // Null terminator
        }
        
        byte[] allRatFileNames = ratFileNameBytes.ToArray();
        
    // Convert texture filename to UTF-8 bytes
    // Store only the base filename (no directory path) because textures are expected to sit alongside the .act file.
    string textureFilenameToWrite = string.IsNullOrEmpty(data.textureFilename) ? string.Empty : Path.GetFileName(data.textureFilename);
    byte[] textureFilenameBytes = System.Text.Encoding.UTF8.GetBytes(textureFilenameToWrite ?? "");
        
        // Calculate offsets
        uint headerSize = (uint)Marshal.SizeOf<ActorHeader>();
        uint ratFilenamesOffset = headerSize;
        uint meshUvsOffset = ratFilenamesOffset + (uint)allRatFileNames.Length;
        uint meshColorsOffset = meshUvsOffset + (uint)(data.meshUVs.Length * 8); // 2 floats per UV
        uint meshIndicesOffset = meshColorsOffset + (uint)(data.meshColors.Length * 16); // 4 floats per color
        uint textureFilenameOffset = meshIndicesOffset + (uint)(data.meshIndices.Length * 2); // 2 bytes per index
        
        // Create header (version 6 - mesh data only, no transforms)
        var header = new ActorHeader
        {
            magic = 0x52544341, // 'ACTR'
            version = 6, // Version 6: mesh data only, no transforms
            num_rat_files = (uint)data.ratFilePaths.Count,
            rat_filenames_length = (uint)allRatFileNames.Length,
            framerate = data.framerate,
            
            // Mesh data information
            num_vertices = (uint)data.meshUVs.Length,
            num_indices = (uint)data.meshIndices.Length,
            mesh_uvs_offset = meshUvsOffset,
            mesh_colors_offset = meshColorsOffset,
            mesh_indices_offset = meshIndicesOffset,
            texture_filename_offset = textureFilenameOffset,
            texture_filename_length = (uint)textureFilenameBytes.Length,
            
            // Rendering mode
            rendering_mode = (byte)renderingMode,
            
            reserved = new byte[7]
        };
        
        using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            // Write header
            WriteStruct(writer, header);
            
            // Write RAT filenames blob
            if (allRatFileNames.Length > 0)
            {
                writer.Write(allRatFileNames);
                Debug.Log($"ACT export: wrote {allRatFileNames.Length} bytes of RAT filenames at offset {ratFilenamesOffset}");
            }
            
            // Write mesh UV data
            foreach (var uv in data.meshUVs)
            {
                writer.Write(uv.x);
                writer.Write(uv.y);
            }
            Debug.Log($"ACT export: wrote {data.meshUVs.Length} UVs at offset {meshUvsOffset}");
            
            // Write mesh color data
            foreach (var color in data.meshColors)
            {
                writer.Write(color.r);
                writer.Write(color.g);
                writer.Write(color.b);
                writer.Write(color.a);
            }
            Debug.Log($"ACT export: wrote {data.meshColors.Length} colors at offset {meshColorsOffset}");
            
            // Write mesh indices
            foreach (var index in data.meshIndices)
            {
                writer.Write((ushort)index);
            }
            Debug.Log($"ACT export: wrote {data.meshIndices.Length} indices at offset {meshIndicesOffset}");
            
            // Write texture filename (base filename only)
            if (textureFilenameBytes.Length > 0)
            {
                writer.Write(textureFilenameBytes);
                Debug.Log($"ACT export: wrote texture filename '{textureFilenameToWrite}' ({textureFilenameBytes.Length} bytes) at offset {textureFilenameOffset} (textures should be in the same folder as the .act file)");
            }
        }
        
    Debug.Log($"Saved Actor data v6: {data.meshUVs.Length} vertices, {data.meshIndices.Length} indices, {data.ratFilePaths.Count} RAT files");
    Debug.Log($"  - framerate: {data.framerate} FPS");
    Debug.Log($"  - rendering mode: {renderingMode}");
    Debug.Log($"  - texture: {data.textureFilename}");
            Debug.Log($"  - Transforms and vertex animation are baked into RAT files; ACT contains mesh data and RAT references");
    }

    /// <summary>
    /// Extract mesh data from RAT files for embedding in .act file
    /// </summary>
    private static (Rat.VertexUV[] uvs, Rat.VertexColor[] colors, ushort[] indices, string textureFilename) ExtractMeshDataFromRatFiles(List<string> ratFilePaths)
    {
        return (new Rat.VertexUV[0], new Rat.VertexColor[0], new ushort[0], "");
    }
    
    /// <summary>
    /// Updates the Actor's RAT file references based on the files created by RatRecorder.
    /// Call this method after RatRecorder has completed its file creation.
    /// </summary>
    public void SetRatFileReferences(List<string> createdRatFiles, List<uint> framesPerFile)
    {
        if (AnimationData == null)
            return;
            
        AnimationData.ratFilePaths.Clear();
        AnimationData.ratFilePaths.AddRange(createdRatFiles);
        
    Debug.Log($"Actor {name} - updated RAT references: {createdRatFiles.Count} files");
    }
    
    /// <summary>
    /// Sets the base filename for saving RAT and Actor files
    /// </summary>
    public void SetBaseFilename(string filename)
    {
        baseFilename = filename;
        ratFilePath = $"GeneratedData/{filename}.rat";
        
    Debug.Log($"Actor {name} - updated filenames: Base={baseFilename}, RAT={ratFilePath}");
    }
    
    /// <summary>
    /// Starts recording both vertex animation (RAT) and transform animation (Actor) data
    /// </summary>
    public void StartRecording()
    {
        if (isRecording)
        {
            Debug.LogWarning($"Actor {name} - already recording");
            return;
        }
        
        StartVertexRecording();
        
        Debug.Log($"Actor {name} - starting vertex frame recording...");
    }
    
    /// <summary>
    /// Stops recording and saves both files
    /// </summary>
    public void StopRecording()
    {
        if (!isRecording)
        {
            Debug.LogWarning($"Actor {name} - not currently recording");
            return;
        }
        
        StopVertexRecording();
    }
    
    /// <summary>
    /// Applies a specific keyframe's transform to this actor
    /// </summary>
    public void ApplyKeyframe(uint keyframeIndex)
    {
        // Note: v6 ACT files have all transforms baked into RAT vertex data
        // This method is kept for compatibility but doesn't apply stored transforms
    Debug.LogWarning($"Actor {name} - ApplyKeyframe called; v6 ACT has no transforms stored (use AnimationPlayer)");
    }
    
    /// <summary>
    /// Validates that Actor and RAT data are synchronized for C engine compatibility.
    /// For multi-RAT setups, validates all referenced RAT files.
    /// </summary>
    /// <param name="ratFilePath">Path to a single RAT file (for legacy validation) or ignored for multi-RAT</param>
    /// <returns>True if data is synchronized, false otherwise</returns>
    public bool ValidateWithRatFile(string ratFilePath = null)
    {
        if (AnimationData == null || AnimationData.ratFilePaths.Count == 0)
        {
            Debug.LogError($"Actor {name} - no RAT file references");
            return false;
        }
        
        // Validate that all referenced RAT files exist
        string directory = Path.GetDirectoryName(ratFilePath ?? "");
        
        foreach (string ratPath in AnimationData.ratFilePaths)
        {
            string fullPath = Path.Combine(directory, ratPath);
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"Actor {name} - referenced RAT not found: {ratPath}");
                return false;
            }
        }
        
    Debug.Log($"Actor {name} - validation successful");
    Debug.Log($"  - {AnimationData.ratFilePaths.Count} RAT files found");
    Debug.Log($"  - framerate: {AnimationData.framerate} FPS");
    Debug.Log($"  - transforms and vertex animation are stored in RAT files");
        
        return true;
    }
    
    /// <summary>
    /// Static utility function to export a mesh (single frame) to RAT and ACT files.
    /// This encapsulates the common logic for exporting mesh data to the C engine format.
    /// Used by Shape.ExportForCEngine() and can be used for any single-frame mesh export.
    /// </summary>
    /// <param name="baseFilename">Base filename for the exported files</param>
    /// <param name="mesh">Unity mesh to export</param>
    /// <param name="transform">Transform of the object</param>
    /// <param name="objectColor">Base color of the object</param>
    /// <param name="renderingMode">Rendering mode for the ACT file</param>
    public static void ExportMeshToRatAct(string baseFilename, Mesh mesh, Transform transform, Color objectColor, Rat.ActorRenderingMode renderingMode = Rat.ActorRenderingMode.TextureWithDirectionalLight)
    {
        if (mesh == null)
        {
            Debug.LogError("ExportMeshToRatAct: Mesh is null!");
            return;
        }
        
        var uvs = mesh.uv.Length > 0 ? mesh.uv : new Vector2[mesh.vertexCount];
        var colors = mesh.colors.Length > 0 ? mesh.colors : Enumerable.Repeat(objectColor, mesh.vertexCount).ToArray();
        
        Rat.Tool.ExportAnimation(
            baseFilename,
            new List<Vector3[]> { mesh.vertices },
            mesh,
            uvs,
            colors,
            30f,
            $"assets/{baseFilename}.png",
            64,
            renderingMode
        );
    }
    
    /// <summary>
    /// Helper method to write a struct to binary stream
    /// </summary>
    private static void WriteStruct<T>(BinaryWriter writer, T structData) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] bytes = new byte[size];
        
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(structData, ptr, false);
            Marshal.Copy(ptr, bytes, 0, size);
            writer.Write(bytes);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
    
    private int frameSkipCounter = 0;
}