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
/// Compatible with C-based engines
/// 
/// Current format (version 5): Mesh data (UVs, colors, indices) is EMBEDDED in the .act file.
/// This eliminates the need for separate .ratmodel files - everything is in .act and .rat files.
/// Only version 5 is supported - older versions are not supported.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ActorHeader
{
    public uint magic;              // 'ACTR' = 0x52544341
    public uint version;            // File format version (always 5)
    public uint num_rat_files;      // Number of RAT files referenced
    public uint rat_filenames_length; // Total length of all RAT filename strings
    public uint num_keyframes;      // Number of transform keyframes
    public float framerate;         // Animation framerate
    public uint transforms_offset;  // Offset to transform data array
    
    // Fixed-point scaling factors for decoding 16-bit values back to floats
    public float position_min_x, position_min_y, position_min_z;
    public float position_max_x, position_max_y, position_max_z;
    public float scale_min, scale_max;
    
    // Embedded mesh data offsets and counts (replaces separate .ratmodel files)
    public uint num_vertices;       // Number of vertices in mesh
    public uint num_indices;        // Number of triangle indices
    public uint mesh_uvs_offset;    // Offset to UV data
    public uint mesh_colors_offset; // Offset to color data
    public uint mesh_indices_offset; // Offset to triangle indices
    public uint texture_filename_offset; // Offset to texture filename
    public uint texture_filename_length; // Length of texture filename
    
    // Rendering mode for material/lighting settings
    public byte rendering_mode;     // ActorRenderingMode enum value (0-7)
    
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
    public byte[] reserved;         // Reduced reserved space to fit rendering_mode
}

/// <summary>
/// Transform data for a single keyframe
/// Uses 16-bit fixed-point values for compact storage
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ActorTransform
{
    public ushort position_x, position_y, position_z;    // World position (16-bit fixed-point) - represents model center
    public ushort rotation_x, rotation_y, rotation_z;    // World rotation (16-bit fixed-point, 0-360 degrees)
    public ushort scale_x, scale_y, scale_z;             // World scale (16-bit fixed-point)
    public ushort rat_file_index;                        // Index into the RAT file list (16-bit, up to 65,535 files)
    public ushort rat_local_frame;                       // Frame index within the specific RAT file (16-bit)
}

/// <summary>
/// Data structure to hold all actor animation data before saving
/// </summary>
[System.Serializable]
public class ActorAnimationData
{
    public List<string> ratFilePaths = new List<string>();       // List of RAT file paths (for split files)
    public float framerate;                 // Animation framerate
    public List<ActorTransformFloat> transforms = new List<ActorTransformFloat>(); // Transform for each keyframe (stored as floats)
    
    public ActorAnimationData()
    {
        framerate = 30f;
    }
    
    // Legacy compatibility property
    public string ratFilePath 
    { 
        get => ratFilePaths.Count > 0 ? ratFilePaths[0] : "";
        set 
        {
            if (ratFilePaths.Count == 0)
                ratFilePaths.Add(value);
            else
                ratFilePaths[0] = value;
        }
    }
}

/// <summary>
/// Floating-point transform data used during recording (before compression)
/// </summary>
[System.Serializable]
public struct ActorTransformFloat
{
    public Vector3 position;        // World position (represents model center)
    public Vector3 rotation;        // World rotation (Euler angles in degrees)
    public Vector3 scale;           // World scale
    public uint rat_file_index;     // Index into the RAT file list (0-based)
    public uint rat_local_frame;    // Frame index within the specific RAT file
}

/// <summary>
/// Material and rendering mode options for Actor rendering
/// </summary>
public enum ActorRenderingMode
{
    VertexColoursOnly,
    VertexColoursWithDirectionalLight,
    VertexColoursWithVertexLighting,
    TextureOnly,
    TextureAndVertexColours,
    TextureWithDirectionalLight,
    TextureAndVertexColoursAndDirectionalLight,
    MatCap
}

public class Actor : MonoBehaviour
{
    public ActorAnimationData AnimationData { get; private set; } = new ActorAnimationData();
    
    [Header("Recording Settings")]
    public bool record = false;

    [Header("Material & Rendering Settings")]
    [Tooltip("Choose the material and lighting mode for this actor")]
    public ActorRenderingMode renderingMode = ActorRenderingMode.TextureWithDirectionalLight;

    // Auto-generated fields (not shown in Inspector)
    private string ratFilePath = "";    // Auto-generated from transform name
    private string baseFilename = "";   // Auto-generated from transform name
    private string textureFilename = "";// Auto-generated texture filename

    // Non-serialized fields
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
    private uint recordedFrameCount;
    private float animationDuration = 0f;  // Total duration of the animation
    private RatRecorder ratRecorder;

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
        
        Debug.Log($"Actor '{name}': Auto-generated filenames - Base: '{baseFilename}', RAT: '{ratFilePath}', Texture: '{textureFilename}'");
    }
    
    /// <summary>
    /// Generates optimized texture filename and processes texture using TextureProcessor
    /// </summary>
    private void GenerateAndProcessTexture(string cleanName)
    {
        // MatCap rendering mode doesn't use _MainTex, so skip texture processing
        if (renderingMode == ActorRenderingMode.MatCap)
        {
            Debug.Log($"Actor '{name}': Using MatCap rendering mode - no main texture needed.");
            textureFilename = "";
            return;
        }
        
        try
        {
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
                        Debug.Log($"Actor '{name}': Exported readable texture '{sourceTexture.name}' to '{outputPath}' ({pngData.Length} bytes)");
                    }
                    else
                    {
                        Debug.LogWarning($"Actor '{name}': Failed to encode texture to PNG.");
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
                        Debug.Log($"Actor '{name}': Exported non-readable texture '{sourceTexture.name}' to '{outputPath}' ({pngData.Length} bytes)");
                    }
                    else
                    {
                        Debug.LogWarning($"Actor '{name}': Failed to encode texture to PNG.");
                        GenerateFallbackTexture(cleanName);
                    }
                    
                    DestroyImmediate(readableTexture);
                }
            }
            else
            {
                Debug.LogWarning($"Actor '{name}': No texture found on GameObject, material, or children. Using fallback texture naming.");
                GenerateFallbackTexture(cleanName);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Actor '{name}': Failed to export texture: {e.Message}\n{e.StackTrace}");
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
        Debug.Log($"Actor '{name}': Using fallback texture filename: '{textureFilename}'");
    }

    /// <summary>
    /// Validates that we have the required components and sets them up
    /// </summary>
    private void ValidateAndSetupComponents()
    {
        // Get mesh renderer components
        meshRenderer = GetComponent<MeshRenderer>();
        skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
        
        // Search for animator in this GameObject and its parents
        animator = GetComponentInParent<Animator>();
        
        // REQUIRE at least one renderer - this is non-negotiable
        if (meshRenderer == null && skinnedMeshRenderer == null)
        {
            Debug.LogError($"Actor '{name}' REQUIRES either a MeshRenderer or SkinnedMeshRenderer component!");
            
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
            Debug.LogWarning($"Actor '{name}' has no Animator component in this GameObject or its parents. Consider adding one for animation control.");
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
        
        if (renderer == null)
        {
            Debug.LogError($"Actor '{name}': No renderer found for shader setup!");
            return;
        }
        
        // Map rendering mode to shader name
        string shaderName = GetShaderNameForRenderingMode(renderingMode);
        
        if (string.IsNullOrEmpty(shaderName))
        {
            Debug.LogError($"Actor '{name}': Unknown rendering mode: {renderingMode}");
            return;
        }
        
        // Load the shader
        Shader shader = Shader.Find(shaderName);
        
        if (shader == null)
        {
            Debug.LogWarning($"Actor '{name}': Shader '{shaderName}' not found yet (may not be imported). Skipping material setup.");
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
                Debug.Log($"Actor '{name}': Applied texture '{texture.name}' to {renderingMode} shader");
            }
        }
        
        // Load and assign matcap texture if needed
        if (renderingMode == ActorRenderingMode.MatCap)
        {
            Texture2D matcap = LoadMatcapTexture();
            if (matcap != null)
            {
                material.SetTexture("_Matcap", matcap);
                Debug.Log($"Actor '{name}': Applied matcap texture '{matcap.name}'");
            }
        }
        
        // Apply the material to all materials on the renderer
        renderer.material = material;
        
        Debug.Log($"Actor '{name}': Applied shader '{shaderName}' for rendering mode '{renderingMode}'");
    }
    
    /// <summary>
    /// Gets the shader name for a given rendering mode
    /// </summary>
    private static string GetShaderNameForRenderingMode(ActorRenderingMode mode)
    {
        return mode switch
        {
            ActorRenderingMode.VertexColoursOnly => "Actor/VertexColoursOnly",
            ActorRenderingMode.VertexColoursWithDirectionalLight => "Actor/VertexColoursWithDirectionalLight",
            ActorRenderingMode.VertexColoursWithVertexLighting => "Actor/VertexColoursWithVertexLighting",
            ActorRenderingMode.TextureOnly => "Actor/TextureOnly",
            ActorRenderingMode.TextureAndVertexColours => "Actor/TextureAndVertexColours",
            ActorRenderingMode.TextureWithDirectionalLight => "Actor/TextureWithDirectionalLight",
            ActorRenderingMode.TextureAndVertexColoursAndDirectionalLight => "Actor/TextureAndVertexColoursAndDirectionalLight",
            ActorRenderingMode.MatCap => "Actor/MatCap",
            _ => null
        };
    }
    
    /// <summary>
    /// Determines if a rendering mode requires a main texture
    /// </summary>
    private static bool RequiresTexture(ActorRenderingMode mode)
    {
        return mode switch
        {
            ActorRenderingMode.TextureOnly => true,
            ActorRenderingMode.TextureAndVertexColours => true,
            ActorRenderingMode.TextureWithDirectionalLight => true,
            ActorRenderingMode.TextureAndVertexColoursAndDirectionalLight => true,
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
#if UNITY_EDITOR
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
#endif
        
        Debug.LogWarning($"Actor '{name}': Could not find matcap texture. Using white texture as fallback.");
        return null;
    }
    
    /// <summary>
    /// Converts a float value to 16-bit fixed-point within the given range
    /// </summary>
    private static ushort FloatToFixed16(float value, float minValue, float maxValue)
    {
        if (maxValue <= minValue) return 0;
        
        // Clamp value to range
        value = Mathf.Clamp(value, minValue, maxValue);
        
        // Normalize to 0-1 range
        float normalized = (value - minValue) / (maxValue - minValue);
        
        // Convert to 16-bit (0-65535)
        return (ushort)Mathf.RoundToInt(normalized * 65535f);
    }
    
    /// <summary>
    /// Converts a 16-bit fixed-point value back to float within the given range
    /// </summary>
    private static float Fixed16ToFloat(ushort fixedValue, float minValue, float maxValue)
    {
        if (maxValue <= minValue) return minValue;
        
        // Convert from 16-bit to normalized 0-1 range
        float normalized = fixedValue / 65535f;
        
        // Scale back to original range
        return minValue + normalized * (maxValue - minValue);
    }
    
    /// <summary>
    /// Converts rotation from degrees (0-360) to 16-bit fixed-point
    /// </summary>
    private static ushort DegreesToFixed16(float degrees)
    {
        // Normalize to 0-360 range
        degrees = degrees % 360f;
        if (degrees < 0) degrees += 360f;
        
        // Convert to 16-bit (0-65535 represents 0-360 degrees)
        return (ushort)Mathf.RoundToInt((degrees / 360f) * 65535f);
    }
    
    /// <summary>
    /// Converts 16-bit fixed-point back to degrees (0-360)
    /// </summary>
    private static float Fixed16ToDegrees(ushort fixedValue)
    {
        // Convert from 16-bit to 0-360 degree range
        return (fixedValue / 65535f) * 360f;
    }

    public void Update()
    {
        position = transform.position;
        rotation = transform.eulerAngles;
        scale = transform.localScale;
        
        // Check if transform name changed and update filenames accordingly
        if (transform.name != lastTransformName)
        {
            UpdateFilenamesFromTransform();
            lastTransformName = transform.name;
        }
        
        // Update current keyframe based on animator state
        UpdateCurrentKeyFrame();
        
        // Always start recording transforms when in play mode and animator is available
        if (!isRecording && Application.isPlaying && animator != null)
        {
            StartTransformRecording();
        }
        
        if (isRecording)
        {
            RecordCurrentTransform();
            
            // Stop recording after duration
            if (Time.time - recordingStartTime >= animationDuration)
            {
                StopTransformRecording();
            }
        }
    }
    
    private void OnApplicationPause(bool pauseStatus)
    {
        // Save files when application is paused (which includes exiting play mode)
        if (pauseStatus && isRecording)
        {
            StopTransformRecording();
        }
    }
    
    private void OnDestroy()
    {
        // Save files when component is destroyed (including when exiting play mode)
        if (isRecording && AnimationData != null && AnimationData.transforms.Count > 0)
        {
            StopTransformRecording();
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
    /// Starts recording transform data for each frame
    /// </summary>
    private void StartTransformRecording()
    {
        if (isRecording) return;
        
        // Update filenames based on current transform name (in case it changed)
        UpdateFilenamesFromTransform();
        
        // Get or create RatRecorder component
        ratRecorder = GetComponent<RatRecorder>();
        if (ratRecorder == null)
        {
            Debug.LogWarning($"Actor '{name}': No RatRecorder found. Adding one automatically...");
            ratRecorder = gameObject.AddComponent<RatRecorder>();
            
            // Configure RatRecorder with animation settings
            if (skinnedMeshRenderer != null)
                ratRecorder.targetSkinnedMeshRenderer = skinnedMeshRenderer;
            else if (meshRenderer != null)
                ratRecorder.targetMeshFilter = GetComponent<MeshFilter>();
        }
        
        // Calculate animation duration and framerate from animator
        animationDuration = GetAnimationDuration();
        float frameRate = GetCurrentFrameRate();
        
        // Set up RatRecorder with animation parameters
        ratRecorder.recordingDuration = animationDuration;
        ratRecorder.captureFramerate = frameRate;
        ratRecorder.baseFilename = baseFilename;
        
        // Try to get model center from RatRecorder (no longer stored, but used for validation)
        Vector3 ratModelCenter = ratRecorder.GetModelCenter();
        Debug.Log($"Actor '{name}': RAT model center (for reference): {ratModelCenter}");
        
        // Initialize animation data
        AnimationData = new ActorAnimationData();
        AnimationData.ratFilePath = ratFilePath;
        AnimationData.framerate = frameRate;
        
        isRecording = true;
        recordingStartTime = Time.time;
        recordedFrameCount = 0;
        
        Debug.Log($"Actor '{name}': Started recording transforms at {AnimationData.framerate} FPS for {animationDuration} seconds");
        Debug.Log($"Actor '{name}': Will save to {baseFilename}.rat and {baseFilename}.act");
        Debug.Log($"Actor '{name}': Position represents model center for RAT vertex deltas");
    }
    
    /// <summary>
    /// Records the current transform as a keyframe
    /// </summary>
    private void RecordCurrentTransform()
    {
        if (!isRecording) return;
        
        var transform = new ActorTransformFloat
        {
            position = new Vector3(position.x, position.y, -position.z), // Flip Z for right-handed coordinates
            rotation = rotation,
            scale = scale,
            rat_file_index = 0, // Will be updated later when RAT files are created
            rat_local_frame = recordedFrameCount // Will be updated later with correct local frame index
        };
        
        AnimationData.transforms.Add(transform);
        recordedFrameCount++;
    }
    
    /// <summary>
    /// Stops recording and saves both RAT and Actor data to files
    /// </summary>
    private void StopTransformRecording()
    {
        if (!isRecording) return;
        
        isRecording = false;
        
        // Save both files together
        SaveBothFiles();
        
        Debug.Log($"Actor '{name}': Recorded {recordedFrameCount} transform keyframes");
    }
    
    /// <summary>
    /// Saves both RAT and Actor files together with synchronized data
    /// </summary>
    public void SaveBothFiles()
    {
        if (AnimationData == null || AnimationData.transforms.Count == 0)
        {
            Debug.LogError($"Actor '{name}': No animation data to save!");
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
            // Check if RatRecorder is available and configure it
            if (ratRecorder != null)
            {
                // Ensure RatRecorder uses the same base filename
                ratRecorder.baseFilename = baseFilename;
                
                Debug.Log($"Actor '{name}': RatRecorder is configured to save RAT file as {baseFilename}.rat");
            }
            else
            {
                Debug.LogWarning($"Actor '{name}': No RatRecorder found. Only saving Actor file.");
            }
            
            // Update the RAT file paths in our animation data
            if (AnimationData.ratFilePaths.Count == 0)
            {
                AnimationData.ratFilePaths.Add(ratFilePath);
            }
            
            // Update transform data with RAT file mapping information
            UpdateTransformRatFileReferences();
            
            // RIGHT BEFORE calling SaveActorData, add this diagnostic block:
            Debug.Log($"=== DIAGNOSTIC: RAT File Paths Before Save ===");
            Debug.Log($"AnimationData.ratFilePaths.Count = {AnimationData.ratFilePaths.Count}");
            for (int i = 0; i < AnimationData.ratFilePaths.Count; i++)
            {
                string path = AnimationData.ratFilePaths[i];
                Debug.Log($"  [{i}] = '{path}' (length: {path?.Length ?? -1})");
                
                // Log byte-by-byte breakdown
                if (!string.IsNullOrEmpty(path))
                {
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(path);
                    System.Text.StringBuilder hex = new System.Text.StringBuilder();
                    for (int b = 0; b < Math.Min(bytes.Length, 32); b++)
                    {
                        hex.AppendFormat("{0:X2} ", bytes[b]);
                    }
                    Debug.Log($"      Hex: {hex}");
                }
            }
            Debug.Log($"=== END DIAGNOSTIC ===");
            
            // Save Actor data with .act extension (include rendering mode)
            SaveActorData(actorFilePath, AnimationData, renderingMode);
            
            Debug.Log($"Actor '{name}': Actor file saved successfully:");
            Debug.Log($"  - Actor file: {actorFilePath} (transform animation)");
            Debug.Log($"  - References RAT file: {ratFilePath} (vertex animation)");
            Debug.Log($"  - Total frames: {AnimationData.transforms.Count}");
            Debug.Log($"  - Framerate: {AnimationData.framerate} FPS");
            Debug.Log($"  - Duration: {(AnimationData.transforms.Count / AnimationData.framerate):F2} seconds");
            
            if (ratRecorder != null)
            {
                Debug.Log($"  - RAT file will be saved by RatRecorder when recording completes");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Actor '{name}': Failed to save Actor file: {e.Message}");
        }
    }
    
    /// <summary>
    /// Saves actor animation data to a binary file with 16-bit fixed-point compression
    /// and embedded mesh data (Version 5 format with rendering mode)
    /// </summary>
    public static void SaveActorData(string filePath, ActorAnimationData data, ActorRenderingMode renderingMode = ActorRenderingMode.TextureWithDirectionalLight, bool embedMeshData = true)
    {
        if (data.transforms.Count == 0)
        {
            Debug.LogError("No transform data to save!");
            return;
        }
        
        // First pass: Calculate bounds for all transform data
        Vector3 positionMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 positionMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        float scaleMin = float.MaxValue;
        float scaleMax = float.MinValue;
        
        foreach (var transform in data.transforms)
        {
            positionMin = Vector3.Min(positionMin, transform.position);
            positionMax = Vector3.Max(positionMax, transform.position);
            
            scaleMin = Mathf.Min(scaleMin, Mathf.Min(transform.scale.x, Mathf.Min(transform.scale.y, transform.scale.z)));
            scaleMax = Mathf.Max(scaleMax, Mathf.Max(transform.scale.x, Mathf.Max(transform.scale.y, transform.scale.z)));
        }
        
        // Get mesh data - we'll need to extract this from the RAT files or generate default data
        var meshData = embedMeshData ? ExtractMeshDataFromRatFiles(data.ratFilePaths) : (new Rat.VertexUV[0], new Rat.VertexColor[0], new ushort[0], "");
        
        // Extract mesh data components
        var meshUvs = meshData.Item1;
        var meshColors = meshData.Item2;
        var meshIndices = meshData.Item3;
        var textureFilename = meshData.Item4;
        
        // Ensure meshData components are never null (only if embedding mesh data)
        if (embedMeshData)
        {
            if (meshUvs == null || meshUvs.Length == 0)
            {
                meshUvs = new Rat.VertexUV[] { new Rat.VertexUV { u = 0, v = 0 } };
            }
            if (meshColors == null || meshColors.Length == 0)
            {
                meshColors = new Rat.VertexColor[] { new Rat.VertexColor { r = 1, g = 1, b = 1, a = 1 } };
            }
            if (meshIndices == null || meshIndices.Length == 0)
            {
                meshIndices = new ushort[] { 0 };
            }
        }
        else
        {
            // For non-embedded mesh data, use empty arrays
            meshUvs = meshUvs ?? new Rat.VertexUV[0];
            meshColors = meshColors ?? new Rat.VertexColor[0];
            meshIndices = meshIndices ?? new ushort[0];
        }
        if (textureFilename == null)
        {
            textureFilename = "";
        }
        
        // Ensure texture is saved if rendering mode requires it
        if (RequiresTexture(renderingMode) && string.IsNullOrEmpty(textureFilename))
        {
            Debug.LogWarning("Rendering mode requires texture but none found in RAT data. Texture will be empty in .act file.");
        }
        
        using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            // Convert all RAT filenames to UTF-8 bytes with null terminators
            // CRITICAL FIX: Ensure each filename is properly null-terminated and correctly extracted
            var ratFileNameBytes = new List<byte>();
            
            Debug.Log($"ACT Export: Writing {data.ratFilePaths.Count} RAT file references:");
            
            foreach (string ratPath in data.ratFilePaths)
            {
                // DIAGNOSTIC: Log the original path before processing
                Debug.Log($"  - Original RAT path from data: '{ratPath}'");
                
                // Clean the path - ensure it's just the filename without directory prefixes
                // Use Path.GetFileName which handles both Windows and Unix paths correctly
                string cleanPath = Path.GetFileName(ratPath);
                
                // DIAGNOSTIC: Verify the extracted filename
                Debug.Log($"  - Extracted filename: '{cleanPath}' (length: {cleanPath?.Length ?? 0})");
                
                // Additional safety check - if GetFileName returns empty, use the original path
                if (string.IsNullOrEmpty(cleanPath))
                {
                    Debug.LogWarning($"  - Path.GetFileName returned empty! Using original path: '{ratPath}'");
                    cleanPath = ratPath;
                }
                
                // Convert to UTF-8 and add null terminator
                byte[] pathBytes = Encoding.UTF8.GetBytes(cleanPath);
                Debug.Log($"  - UTF-8 byte count: {pathBytes.Length}");
                
                ratFileNameBytes.AddRange(pathBytes);
                ratFileNameBytes.Add(0); // Null terminator
            }
            
            byte[] allRatFileNames = ratFileNameBytes.ToArray();
            
            // Debug: Log the actual bytes being written
            Debug.Log($"ACT Export: RAT filenames blob size: {allRatFileNames.Length} bytes");
            Debug.Log($"ACT Export: Hex dump of RAT filenames blob:");
            StringBuilder hexDump = new StringBuilder();
            StringBuilder asciiDump = new StringBuilder();
            for (int i = 0; i < Mathf.Min(allRatFileNames.Length, 128); i++)
            {
                hexDump.AppendFormat("{0:X2} ", allRatFileNames[i]);
                char c = (char)allRatFileNames[i];
                asciiDump.Append(c >= 32 && c < 127 ? c.ToString() : (c == 0 ? "\\0" : "?"));
            }
            Debug.Log($"  Hex: {hexDump}");
            Debug.Log($"  ASCII: {asciiDump}");
            
            // Convert texture filename to UTF-8 bytes
            byte[] textureFilenameBytes = Encoding.UTF8.GetBytes(textureFilename ?? "");
            
            // Calculate offsets
            uint headerSize = (uint)Marshal.SizeOf<ActorHeader>();
            uint ratFilenamesOffset = headerSize;
            uint meshUvsOffset = embedMeshData ? (ratFilenamesOffset + (uint)allRatFileNames.Length) : 0;
            uint meshColorsOffset = embedMeshData ? (meshUvsOffset + (uint)(meshUvs.Length * 8)) : 0; // 2 floats per UV
            uint meshIndicesOffset = embedMeshData ? (meshColorsOffset + (uint)(meshColors.Length * 16)) : 0; // 4 floats per color
            uint textureFilenameOffset = embedMeshData ? (meshIndicesOffset + (uint)(meshIndices.Length * 2)) : 0; // 2 bytes per index
            uint transformsOffset = embedMeshData ? (textureFilenameOffset + (uint)textureFilenameBytes.Length) : (ratFilenamesOffset + (uint)allRatFileNames.Length);
            
            // Create header (version 5 for embedded mesh data and rendering mode)
            var header = new ActorHeader
            {
                magic = 0x52544341, // 'ACTR'
                version = 5, // Version 5 with embedded mesh data and rendering mode
                num_rat_files = (uint)data.ratFilePaths.Count,
                rat_filenames_length = (uint)allRatFileNames.Length,
                num_keyframes = (uint)data.transforms.Count,
                framerate = data.framerate,
                transforms_offset = transformsOffset,
                
                // Store bounds for fixed-point conversion
                position_min_x = positionMin.x, position_min_y = positionMin.y, position_min_z = positionMin.z,
                position_max_x = positionMax.x, position_max_y = positionMax.y, position_max_z = positionMax.z,
                scale_min = scaleMin, scale_max = scaleMax,
                
                // Mesh data information
                num_vertices = embedMeshData ? (uint)meshUvs.Length : 0,
                num_indices = embedMeshData ? (uint)meshIndices.Length : 0,
                mesh_uvs_offset = embedMeshData ? meshUvsOffset : 0,
                mesh_colors_offset = embedMeshData ? meshColorsOffset : 0,
                mesh_indices_offset = embedMeshData ? meshIndicesOffset : 0,
                texture_filename_offset = embedMeshData ? textureFilenameOffset : 0,
                texture_filename_length = embedMeshData ? (uint)textureFilenameBytes.Length : 0,
                
                // Rendering mode
                rendering_mode = (byte)renderingMode,
                
                reserved = new byte[7]
            };
            
            // Write header
            WriteStruct(writer, header);
            
            // Write RAT filenames blob
            if (allRatFileNames.Length > 0)
            {
                writer.Write(allRatFileNames);
                Debug.Log($"ACT Export: Wrote {allRatFileNames.Length} bytes of RAT filenames at offset {ratFilenamesOffset}");
            }
            
            // Write mesh data only if embedding
            if (embedMeshData)
            {
                // Write mesh UV data
                foreach (var uv in meshUvs)
                {
                    writer.Write(uv.u);
                    writer.Write(uv.v);
                }
                
                // Write mesh color data
                foreach (var color in meshColors)
                {
                    writer.Write(color.r);
                    writer.Write(color.g);
                    writer.Write(color.b);
                    writer.Write(color.a);
                }
                
                // Write mesh indices
                foreach (var index in meshIndices)
                {
                    writer.Write(index);
                }
                
                // Write texture filename
                if (textureFilenameBytes.Length > 0)
                {
                    writer.Write(textureFilenameBytes);
                }
            }
            
            // Write transform data converted to 16-bit fixed-point
            for (int i = 0; i < data.transforms.Count; i++)
            {
                var floatTransform = data.transforms[i];
                
                var fixedTransform = new ActorTransform
                {
                    position_x = FloatToFixed16(floatTransform.position.x, positionMin.x, positionMax.x),
                    position_y = FloatToFixed16(floatTransform.position.y, positionMin.y, positionMax.y),
                    position_z = FloatToFixed16(floatTransform.position.z, positionMin.z, positionMax.z),
                    
                    rotation_x = DegreesToFixed16(floatTransform.rotation.x),
                    rotation_y = DegreesToFixed16(floatTransform.rotation.y),
                    rotation_z = DegreesToFixed16(floatTransform.rotation.z),
                    
                    scale_x = FloatToFixed16(floatTransform.scale.x, scaleMin, scaleMax),
                    scale_y = FloatToFixed16(floatTransform.scale.y, scaleMin, scaleMax),
                    scale_z = FloatToFixed16(floatTransform.scale.z, scaleMin, scaleMax),
                    
                    rat_file_index = (ushort)Mathf.Min(floatTransform.rat_file_index, 65535),
                    rat_local_frame = (ushort)Mathf.Min(floatTransform.rat_local_frame, 65535)
                };
                
                WriteStruct(writer, fixedTransform);
            }
        }
        
        Debug.Log($"Saved Actor data (v5{(embedMeshData ? " with embedded mesh" : " without embedded mesh")}, rendering mode): {data.transforms.Count} keyframes, {meshUvs.Length} vertices");
        Debug.Log($"  - Rendering mode: {renderingMode}");
        Debug.Log($"  - Texture: {textureFilename}");
        Debug.Log($"  - RAT files: {string.Join(", ", data.ratFilePaths)}");
    }

    /// <summary>
    /// Extract mesh data from RAT files for embedding in .act file
    /// </summary>
    private static (Rat.VertexUV[] uvs, Rat.VertexColor[] colors, ushort[] indices, string textureFilename) ExtractMeshDataFromRatFiles(List<string> ratFilePaths)
    {
        // For now, just return empty data
        return (new Rat.VertexUV[0], new Rat.VertexColor[0], new ushort[0], "");
    }
    
    /// <summary>
    /// Updates transform data with RAT file mapping information.
    /// This assigns each transform to the appropriate RAT file and local frame index.
    /// </summary>
    private void UpdateTransformRatFileReferences()
    {
        if (AnimationData == null || AnimationData.transforms.Count == 0)
            return;
            
        // For now, assign all transforms to the first RAT file
        for (int i = 0; i < AnimationData.transforms.Count; i++)
        {
            var transform = AnimationData.transforms[i];
            transform.rat_file_index = 0;
            transform.rat_local_frame = (uint)i;
            AnimationData.transforms[i] = transform;
        }
        
        Debug.Log($"Actor '{name}': Updated {AnimationData.transforms.Count} transforms with RAT file references");
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
        
        uint globalFrameIndex = 0;
        
        for (int fileIndex = 0; fileIndex < framesPerFile.Count && fileIndex < createdRatFiles.Count; fileIndex++)
        {
            uint framesInThisFile = framesPerFile[fileIndex];
            
            for (uint localFrame = 0; localFrame < framesInThisFile && globalFrameIndex < AnimationData.transforms.Count; localFrame++)
            {
                var transform = AnimationData.transforms[(int)globalFrameIndex];
                transform.rat_file_index = (uint)fileIndex;
                transform.rat_local_frame = localFrame;
                AnimationData.transforms[(int)globalFrameIndex] = transform;
                
                globalFrameIndex++;
            }
        }
        
        Debug.Log($"Actor '{name}': Updated RAT file references - {createdRatFiles.Count} files, {globalFrameIndex} total frames mapped");
    }
    
    /// <summary>
    /// Sets the base filename for saving RAT and Actor files
    /// </summary>
    public void SetBaseFilename(string filename)
    {
        baseFilename = filename;
        ratFilePath = $"GeneratedData/{filename}.rat";
        
        Debug.Log($"Actor '{name}': Updated filenames - Base: '{baseFilename}', RAT: '{ratFilePath}'");
    }
    
    /// <summary>
    /// Starts recording both vertex animation (RAT) and transform animation (Actor) data
    /// </summary>
    public void StartRecording()
    {
        if (isRecording)
        {
            Debug.LogWarning($"Actor '{name}': Already recording!");
            return;
        }
        
        if (animator == null)
        {
            Debug.LogError($"Actor '{name}': Cannot start recording without an Animator!");
            return;
        }
        
        StartTransformRecording();
        
        Debug.Log($"Actor '{name}': Starting combined RAT and Actor recording...");
    }
    
    /// <summary>
    /// Stops recording and saves both files
    /// </summary>
    public void StopRecording()
    {
        if (!isRecording)
        {
            Debug.LogWarning($"Actor '{name}': Not currently recording!");
            return;
        }
        
        StopTransformRecording();
    }
    
    /// <summary>
    /// Applies a specific keyframe's transform to this actor
    /// </summary>
    public void ApplyKeyframe(uint keyframeIndex)
    {
        if (AnimationData == null || keyframeIndex >= AnimationData.transforms.Count)
            return;
            
        var keyframe = AnimationData.transforms[(int)keyframeIndex];
        
        // Convert from right-handed coordinates back to Unity left-handed
        transform.position = new Vector3(keyframe.position.x, keyframe.position.y, -keyframe.position.z);
        transform.eulerAngles = keyframe.rotation;
        transform.localScale = keyframe.scale;
    }
    
    /// <summary>
    /// Gets the current position which represents the model center for RAT calculations.
    /// </summary>
    public Vector3 GetModelCenter()
    {
        return position;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Unity Editor validation - shows errors/warnings in Inspector and updates shader when rendering mode changes
    /// </summary>
    private void OnValidate()
    {
        // Only validate in editor mode
        if (!Application.isPlaying)
        {
            var mr = GetComponent<MeshRenderer>();
            var smr = GetComponent<SkinnedMeshRenderer>();
            
            if (mr == null && smr == null)
            {
                Debug.LogError($"Actor '{name}' requires either a MeshRenderer or SkinnedMeshRenderer component!", this);
            }
            
            var anim = GetComponentInParent<Animator>();
            if (anim == null)
            {
                Debug.LogWarning($"Actor '{name}' has no Animator in this GameObject or its parents. Consider adding one.", this);
            }
            
            // Update shader when rendering mode changes in the Inspector
            ValidateAndSetupComponents();
            SetupShaderAndMaterial();
        }
    }
#endif

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
    
    /// <summary>
    /// Validates that Actor and RAT data are synchronized for C engine compatibility.
    /// For multi-RAT setups, validates all referenced RAT files.
    /// </summary>
    /// <param name="ratFilePath">Path to a single RAT file (for legacy validation) or ignored for multi-RAT</param>
    /// <returns>True if data is synchronized, false otherwise</returns>
    public bool ValidateWithRatFile(string ratFilePath = null)
    {
        if (AnimationData == null || AnimationData.transforms.Count == 0)
        {
            Debug.LogError($"Actor '{name}': No animation data to validate");
            return false;
        }
        
        // Determine which RAT files to validate
        List<string> ratFilesToValidate = new List<string>();
        
        if (AnimationData.ratFilePaths.Count > 0)
        {
            // Multi-RAT setup - validate all referenced files
            ratFilesToValidate.AddRange(AnimationData.ratFilePaths);
            Debug.Log($"Actor '{name}': Validating multi-RAT setup with {ratFilesToValidate.Count} files");
        }
        else if (!string.IsNullOrEmpty(ratFilePath))
        {
            // Legacy single RAT file validation
            ratFilesToValidate.Add(ratFilePath);
            Debug.Log($"Actor '{name}': Validating single RAT file: {ratFilePath}");
        }
        else
        {
            Debug.LogError($"Actor '{name}': No RAT files specified for validation");
            return false;
        }
        
        uint totalRatFrames = 0;
        bool allFilesValid = true;
        
        // Validate each RAT file
        for (int fileIndex = 0; fileIndex < ratFilesToValidate.Count; fileIndex++)
        {
            string fullPath = ratFilesToValidate[fileIndex];
            
            // Convert relative path to absolute if needed
            if (!fullPath.StartsWith("/") && !fullPath.Contains(":"))
            {
                fullPath = System.IO.Path.Combine(Application.dataPath.Replace("Assets", ""), ratFilesToValidate[fileIndex]);
            }
            
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"Actor '{name}': RAT file {fileIndex + 1} not found: {fullPath}");
                allFilesValid = false;
                continue;
            }
            
            try
            {
                // Read RAT file header to get frame count
                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(stream))
                {
                    // Read RAT header
                    uint magic = reader.ReadUInt32();
                    if (magic != 0x31544152) // "RAT1"
                    {
                        Debug.LogError($"Actor '{name}': Invalid RAT file magic in {fullPath}: 0x{magic:X8}");
                        allFilesValid = false;
                        continue;
                    }
                    
                    reader.ReadUInt32(); // num_vertices
                    uint ratFrames = reader.ReadUInt32(); // num_frames
                    totalRatFrames += ratFrames;
                    
                    Debug.Log($"Actor '{name}': RAT file {fileIndex + 1} ({Path.GetFileName(fullPath)}) has {ratFrames} frames");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Actor '{name}': Error reading RAT file {fileIndex + 1}: {e.Message}");
                allFilesValid = false;
            }
        }
        
        // Validate total frame count matches
        if (totalRatFrames != AnimationData.transforms.Count)
        {
            Debug.LogError($"Actor '{name}': Total frame count mismatch! RAT files: {totalRatFrames}, Actor transforms: {AnimationData.transforms.Count}");
            allFilesValid = false;
        }
        
        // Validate transform RAT file references
        for (int i = 0; i < AnimationData.transforms.Count; i++)
        {
            var transform = AnimationData.transforms[i];
            if (transform.rat_file_index >= ratFilesToValidate.Count)
            {
                Debug.LogError($"Actor '{name}': Transform {i} references invalid RAT file index {transform.rat_file_index} (max: {ratFilesToValidate.Count - 1})");
                allFilesValid = false;
            }
        }
        
        if (allFilesValid)
        {
            Debug.Log($"Actor '{name}': Validation successful!");
            Debug.Log($"  - {ratFilesToValidate.Count} RAT file(s) with {totalRatFrames} total frames");
            Debug.Log($"  - {AnimationData.transforms.Count} Actor transforms at {AnimationData.framerate} FPS");
            Debug.Log($"  - Position represents model center for RAT vertex calculations");
            Debug.Log($"  - C engine ready for loading!");
        }
        
        return allFilesValid;
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
    public static void ExportMeshToRatAct(string baseFilename, Mesh mesh, Transform transform, Color objectColor, ActorRenderingMode renderingMode = ActorRenderingMode.TextureWithDirectionalLight)
    {
        if (mesh == null)
        {
            Debug.LogError("ExportStaticShape: Mesh is null!");
            return;
        }
        
        try
        {
            // Prepare mesh data using existing Rat utilities
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var uvs = mesh.uv.Length > 0 ? mesh.uv : new Vector2[vertices.Length];
            var colors = mesh.colors.Length > 0 ? mesh.colors : new Color[vertices.Length];
            
            // Fill in defaults if needed (this logic is Shape-specific but minimal)
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
                    colors[i] = objectColor; // Use object's base color
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
            
            // Compress animation data using Rat utilities
            var compressed = Rat.CommandLine.GLBToRAT.CompressFrames(
                frames, indices, uvs, colors, textureFilename, meshDataFilename);
            
            // Write RAT file with size splitting using Rat utilities
            var createdRatFiles = Rat.Tool.WriteRatFileWithSizeSplitting(baseFilename, compressed, 64);
            
            // Create Actor animation data for the .act file
            var actorData = new ActorAnimationData();
            actorData.framerate = 30.0f; // Static shape, framerate doesn't matter
            actorData.ratFilePaths.AddRange(createdRatFiles.ConvertAll(path => System.IO.Path.GetFileName(path)));
            
            // Create single transform keyframe for the shape
            var transformKeyframe = new ActorTransformFloat
            {
                position = new Vector3(transform.position.x, transform.position.y, -transform.position.z), // Flip Z for right-handed coordinates
                rotation = transform.eulerAngles,
                scale = transform.lossyScale,
                rat_file_index = 0,
                rat_local_frame = 0
            };
            actorData.transforms.Add(transformKeyframe);
            
            Debug.Log($"ExportStaticShape '{baseFilename}': Transform - pos {transformKeyframe.position:F2}, rot {transformKeyframe.rotation:F2}, scale {transformKeyframe.scale:F2}");
            
            // Save .act file using Actor utilities
            string actFilePath = $"GeneratedData/{baseFilename}.act";
            SaveActorData(actFilePath, actorData, renderingMode, embedMeshData: false);
            
            Debug.Log($"ExportStaticShape '{baseFilename}': Exported to .act file system:");
            Debug.Log($"  - RAT files: {string.Join(", ", createdRatFiles)}");
            Debug.Log($"  - ACT file: {actFilePath}");
            Debug.Log($"  - Texture: {textureFilename}");
            
        }
        catch (System.Exception e)
        {
            Debug.LogError($"ExportStaticShape '{baseFilename}': Failed to export: {e.Message}");
        }
    }
}
