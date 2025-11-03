using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System;
using UnityEngine;

/// <summary>
/// Binary file header for Actor data files (.act) 
/// Compatible with C-based engines
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ActorHeader
{
    public uint magic;              // 'ACTR' = 0x52544341
    public uint version;            // File format version (4 - includes mesh data)
    public uint num_rat_files;      // Number of RAT files referenced
    public uint rat_filenames_length; // Total length of all RAT filename strings
    public uint num_keyframes;      // Number of transform keyframes
    public float framerate;         // Animation framerate
    public uint transforms_offset;  // Offset to transform data array
    
    // Fixed-point scaling factors for decoding 16-bit values back to floats
    public float position_min_x, position_min_y, position_min_z;
    public float position_max_x, position_max_y, position_max_z;
    public float scale_min, scale_max;
    
    // New mesh data offsets and counts
    public uint num_vertices;       // Number of vertices in mesh
    public uint num_indices;        // Number of triangle indices
    public uint mesh_uvs_offset;    // Offset to UV data
    public uint mesh_colors_offset; // Offset to color data
    public uint mesh_indices_offset; // Offset to triangle indices
    public uint texture_filename_offset; // Offset to texture filename
    public uint texture_filename_length; // Length of texture filename
    
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] reserved;         // Reduced reserved space
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

public class Actor : MonoBehaviour
{
    public ActorAnimationData AnimationData { get; private set; } = new ActorAnimationData();
    
    [Header("Recording Settings")]
    public bool record = false;

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
        try
        {
            // Extract texture from the actor's material
            Texture2D sourceTexture = TextureProcessor.ExtractTextureFromGameObject(gameObject, true);
            
            if (sourceTexture != null)
            {
                // Determine optimal texture format for hardware constraints
                string outputPath = Path.Combine("GeneratedData", $"{cleanName}_optimized.png");
                
                // Process and optimize the texture
                var formatInfo = TextureProcessor.ProcessAndOptimizeTexture(
                    sourceTexture, 
                    outputPath, 
                    TextureProcessor.OptimizedTextureFormat.Auto,
                    0, // Use format default size
                    true // Enable palette formats
                );
                
                // Generate optimized filename based on the processing result
                textureFilename = TextureProcessor.GenerateOptimizedFilename(outputPath, formatInfo);
                
                Debug.Log($"Actor '{name}': Processed texture to '{textureFilename}' using format {formatInfo.format} ({formatInfo.size}x{formatInfo.size})");
            }
            else
            {
                Debug.LogWarning($"Actor '{name}': No texture found on GameObject or its children. Using fallback texture naming.");
                GenerateFallbackTexture(cleanName);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Actor '{name}': Failed to process texture with TextureProcessor: {e.Message}. Using fallback texture naming.");
            GenerateFallbackTexture(cleanName);
        }
    }
    
    /// <summary>
    /// Generates fallback texture filename when TextureProcessor fails or no texture is found
    /// </summary>
    private void GenerateFallbackTexture(string cleanName)
    {
        // Use naming convention: actor name + .png
        textureFilename = $"assets/{cleanName}.png";
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
    }
    
    // --- Fixed-Point Conversion Helpers ---
    
    /// <summary>
    /// Converts a float value to 16-bit fixed-point within the given range
    /// </summary>
    /// <param name="value">Float value to convert</param>
    /// <param name="minValue">Minimum value in the range</param>
    /// <param name="maxValue">Maximum value in the range</param>
    /// <returns>16-bit fixed-point representation</returns>
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
    /// <param name="fixedValue">16-bit fixed-point value</param>
    /// <param name="minValue">Minimum value in the range</param>
    /// <param name="maxValue">Maximum value in the range</param>
    /// <returns>Float representation</returns>
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
    /// <param name="degrees">Rotation in degrees</param>
    /// <returns>16-bit fixed-point representation</returns>
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
    /// <param name="fixedValue">16-bit fixed-point value</param>
    /// <returns>Rotation in degrees</returns>
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
    /// <returns>The frame rate of the current animation, or 30 FPS as fallback</returns>
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
    /// <param name="layerIndex">The animator layer index</param>
    /// <returns>The current keyframe for the specified layer</returns>
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
    /// <param name="keyFrame">The target keyframe</param>
    /// <param name="layerIndex">The animator layer index</param>
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
    /// <param name="layerIndex">The animator layer index</param>
    /// <returns>The frame rate of the animation on the specified layer, or 30 FPS as fallback</returns>
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
    /// <param name="layerIndex">The animator layer index</param>
    /// <returns>The frame rate of the current animation</returns>
    public float GetCurrentFrameRate(int layerIndex = 0)
    {
        return GetAnimationFrameRate(layerIndex);
    }
    
    /// <summary>
    /// Gets the duration of the currently playing animation clip
    /// </summary>
    /// <param name="layerIndex">The animator layer index</param>
    /// <returns>The duration of the current animation in seconds, or 5.0 as fallback</returns>
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
    /// <param name="layerIndex">The animator layer index</param>
    /// <returns>Total number of frames in the animation</returns>
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
            position = position,
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
                
                // Note: RatRecorder automatically handles saving when its recording duration is reached
                // We just need to make sure it's using the same filename
            }
            else
            {
                Debug.LogWarning($"Actor '{name}': No RatRecorder found. Only saving Actor file.");
            }
            
            // Update the RAT file paths in our animation data
            if (AnimationData.ratFilePaths.Count == 0)
            {
                // If no RAT files specified yet, add the default single file path for compatibility
                AnimationData.ratFilePaths.Add(ratFilePath);
            }
            
            // TODO: Get actual RAT file list from RatRecorder when it implements size-based splitting
            // For now, we'll update this when the RatRecorder integration is complete
            
            // Update transform data with RAT file mapping information
            UpdateTransformRatFileReferences();
            
            // Save Actor data with .act extension
            SaveActorData(actorFilePath, AnimationData);
            
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
    /// and embedded mesh data (Version 4 format)
    /// </summary>
    /// <param name="filePath">Path to save the .act file</param>
    /// <param name="data">Animation data to save</param>
    public static void SaveActorData(string filePath, ActorAnimationData data)
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
        var meshData = ExtractMeshDataFromRatFiles(data.ratFilePaths);
        
        using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            // Convert all RAT filenames to UTF-8 bytes with null terminators
            var ratFileNameBytes = new List<byte>();
            foreach (string ratPath in data.ratFilePaths)
            {
                byte[] pathBytes = Encoding.UTF8.GetBytes(ratPath + '\0');
                ratFileNameBytes.AddRange(pathBytes);
            }
            byte[] allRatFileNames = ratFileNameBytes.ToArray();
            
            // Convert texture filename to UTF-8 bytes
            byte[] textureFilenameBytes = Encoding.UTF8.GetBytes(meshData.textureFilename ?? "");
            
            // Calculate offsets
            uint headerSize = (uint)Marshal.SizeOf<ActorHeader>();
            uint ratFilenamesOffset = headerSize;
            uint meshUvsOffset = ratFilenamesOffset + (uint)allRatFileNames.Length;
            uint meshColorsOffset = meshUvsOffset + (uint)(meshData.uvs.Length * 8); // 2 floats per UV
            uint meshIndicesOffset = meshColorsOffset + (uint)(meshData.colors.Length * 16); // 4 floats per color
            uint textureFilenameOffset = meshIndicesOffset + (uint)(meshData.indices.Length * 2); // 2 bytes per index
            uint transformsOffset = textureFilenameOffset + (uint)textureFilenameBytes.Length;
            
            // Create header (version 4 for embedded mesh data)
            var header = new ActorHeader
            {
                magic = 0x52544341, // 'ACTR'
                version = 4, // Version 4 with embedded mesh data
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
                num_vertices = (uint)meshData.uvs.Length,
                num_indices = (uint)meshData.indices.Length,
                mesh_uvs_offset = meshUvsOffset,
                mesh_colors_offset = meshColorsOffset,
                mesh_indices_offset = meshIndicesOffset,
                texture_filename_offset = textureFilenameOffset,
                texture_filename_length = (uint)textureFilenameBytes.Length,
                
                reserved = new byte[8]
            };
            
            // Write header
            WriteStruct(writer, header);
            
            // Write RAT filenames
            writer.Write(allRatFileNames);
            
            // Write mesh UV data
            foreach (var uv in meshData.uvs)
            {
                writer.Write(uv.u);
                writer.Write(uv.v);
            }
            
            // Write mesh color data
            foreach (var color in meshData.colors)
            {
                writer.Write(color.r);
                writer.Write(color.g);
                writer.Write(color.b);
                writer.Write(color.a);
            }
            
            // Write mesh indices
            foreach (var index in meshData.indices)
            {
                writer.Write(index);
            }
            
            // Write texture filename
            if (textureFilenameBytes.Length > 0)
            {
                writer.Write(textureFilenameBytes);
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
        
        Debug.Log($"Saved Actor data (v4 with embedded mesh): {data.transforms.Count} keyframes, {meshData.uvs.Length} vertices");
        Debug.Log($"  - Texture: {meshData.textureFilename}");
        Debug.Log($"  - RAT files: {string.Join(", ", data.ratFilePaths)}");
    }

    /// <summary>
    /// Extract mesh data from RAT files for embedding in .act file
    /// </summary>
    private static (Rat.VertexUV[] uvs, Rat.VertexColor[] colors, ushort[] indices, string textureFilename) ExtractMeshDataFromRatFiles(List<string> ratFilePaths)
    {
        if (ratFilePaths.Count == 0)
        {
            // Return default mesh data
            return (
                new Rat.VertexUV[] { new Rat.VertexUV { u = 0, v = 0 } },
                new Rat.VertexColor[] { new Rat.VertexColor { r = 1, g = 1, b = 1, a = 1 } },
                new ushort[] { 0 },
                ""
            );
        }
        
        // Load the first RAT file to extract mesh data
        string firstRatPath = ratFilePaths[0];
        
        try
        {
            var animation = Rat.Core.ReadRatFile(firstRatPath);
            return (animation.uvs, animation.colors, animation.indices, animation.texture_filename);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to extract mesh data from RAT file '{firstRatPath}': {e.Message}");
            
            // Return minimal default data
            return (
                new Rat.VertexUV[] { new Rat.VertexUV { u = 0, v = 0 } },
                new Rat.VertexColor[] { new Rat.VertexColor { r = 1, g = 1, b = 1, a = 1 } },
                new ushort[] { 0 },
                ""
            );
        }
    }
    
    /// <summary>
    /// Loads actor animation data from a binary file with mesh data embedded (Version 4)
    /// </summary>
    public static ActorAnimationData LoadActorData(string filePath)
    {
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        using (var reader = new BinaryReader(stream))
        {
            // Read header
            var header = ReadStruct<ActorHeader>(reader);
            
            // Validate magic number
            if (header.magic != 0x52544341)
            {
                throw new System.Exception($"Invalid Actor file magic number: 0x{header.magic:X8}");
            }
            
            var data = new ActorAnimationData
            {
                framerate = header.framerate
            };
            
            if (header.version == 4)
            {
                // Version 4 format - embedded mesh data, 16-bit fixed-point
                byte[] allRatFileNameBytes = reader.ReadBytes((int)header.rat_filenames_length);
                string allRatFileNames = Encoding.UTF8.GetString(allRatFileNameBytes);
                
                string[] ratFileNames = allRatFileNames.Split('\0', StringSplitOptions.RemoveEmptyEntries);
                data.ratFilePaths.AddRange(ratFileNames);
                
                Debug.Log($"Loading Actor file (v4) with {data.ratFilePaths.Count} RAT files and embedded mesh data");
                
                // Skip mesh data for now (UV, colors, indices) - the C engine will read these
                // We could read them here if needed for Unity-side processing
                
                // Read texture filename
                if (header.texture_filename_length > 0)
                {
                    reader.BaseStream.Seek(header.texture_filename_offset, SeekOrigin.Begin);
                    byte[] textureBytes = reader.ReadBytes((int)header.texture_filename_length);
                    string textureFilename = Encoding.UTF8.GetString(textureBytes);
                    Debug.Log($"Texture filename: {textureFilename}");
                }
                
                // Read fixed-point transforms and convert back to float
                reader.BaseStream.Seek(header.transforms_offset, SeekOrigin.Begin);
                for (int i = 0; i < header.num_keyframes; i++)
                {
                    var fixedTransform = ReadStruct<ActorTransform>(reader);
                    
                    var floatTransform = new ActorTransformFloat
                    {
                        position = new Vector3(
                            Fixed16ToFloat(fixedTransform.position_x, header.position_min_x, header.position_max_x),
                            Fixed16ToFloat(fixedTransform.position_y, header.position_min_y, header.position_max_y),
                            Fixed16ToFloat(fixedTransform.position_z, header.position_min_z, header.position_max_z)
                        ),
                        rotation = new Vector3(
                            Fixed16ToDegrees(fixedTransform.rotation_x),
                            Fixed16ToDegrees(fixedTransform.rotation_y),
                            Fixed16ToDegrees(fixedTransform.rotation_z)
                        ),
                        scale = new Vector3(
                            Fixed16ToFloat(fixedTransform.scale_x, header.scale_min, header.scale_max),
                            Fixed16ToFloat(fixedTransform.scale_y, header.scale_min, header.scale_max),
                            Fixed16ToFloat(fixedTransform.scale_z, header.scale_min, header.scale_max)
                        ),
                        rat_file_index = fixedTransform.rat_file_index,
                        rat_local_frame = fixedTransform.rat_local_frame
                    };
                    
                    data.transforms.Add(floatTransform);
                }
            }
            else
            {
                throw new System.Exception($"Unsupported Actor file version: {header.version}. Version 4 required for embedded mesh data.");
            }
            
            return data;
        }
    }
    
    /// <summary>
    /// Updates transform data with RAT file mapping information.
    /// This assigns each transform to the appropriate RAT file and local frame index.
    /// </summary>
    private void UpdateTransformRatFileReferences()
    {
        if (AnimationData == null || AnimationData.transforms.Count == 0)
            return;
            
        // For now, since we don't have the actual RAT file frame counts,
        // we'll assign all transforms to the first RAT file
        // TODO: Update this when RatRecorder provides actual file splitting information
        
        for (int i = 0; i < AnimationData.transforms.Count; i++)
        {
            var transform = AnimationData.transforms[i];
            transform.rat_file_index = 0; // All frames go to first RAT file for now
            transform.rat_local_frame = (uint)i; // Frame index within that RAT file
            AnimationData.transforms[i] = transform;
        }
        
        Debug.Log($"Actor '{name}': Updated {AnimationData.transforms.Count} transforms with RAT file references");
    }

    /// <summary>
    /// Updates the Actor's RAT file references based on the files created by RatRecorder.
    /// Call this method after RatRecorder has completed its file creation.
    /// </summary>
    /// <param name="createdRatFiles">List of RAT file paths created by RatRecorder</param>
    /// <param name="framesPerFile">Number of frames in each RAT file</param>
    public void SetRatFileReferences(List<string> createdRatFiles, List<uint> framesPerFile)
    {
        if (AnimationData == null)
            return;
            
        // Update the RAT file paths
        AnimationData.ratFilePaths.Clear();
        AnimationData.ratFilePaths.AddRange(createdRatFiles);
        
        // Update transform data with correct RAT file and frame mappings
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
    /// <param name="filename">Base filename without extension</param>
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
        
        // Start the recording process
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
    /// <param name="keyframeIndex">The keyframe index to apply</param>
    public void ApplyKeyframe(uint keyframeIndex)
    {
        if (AnimationData == null || keyframeIndex >= AnimationData.transforms.Count)
            return;
            
        var keyframe = AnimationData.transforms[(int)keyframeIndex];
        
        transform.position = keyframe.position;
        transform.eulerAngles = keyframe.rotation;
        transform.localScale = keyframe.scale;
    }
    
    /// <summary>
    /// Gets the current position which represents the model center for RAT calculations.
    /// This is critical for your C engine to correctly transform vertices.
    /// </summary>
    /// <returns>The current position (model center point)</returns>
    public Vector3 GetModelCenter()
    {
        return position;
    }
    
    public void CEngineMultiRatIntegrationDocumentation() { }
    
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
    /// Gets information about which RAT file and local frame a specific keyframe uses.
    /// Useful for debugging and C engine integration.
    /// </summary>
    /// <param name="globalKeyframe">Global keyframe index (0-based)</param>
    /// <returns>Tuple of (rat_file_index, rat_local_frame, rat_filename) or null if invalid</returns>
    public (uint ratFileIndex, uint ratLocalFrame, string ratFileName)? GetRatFileInfoForKeyframe(uint globalKeyframe)
    {
        if (AnimationData == null || globalKeyframe >= AnimationData.transforms.Count)
            return null;
            
        var transform = AnimationData.transforms[(int)globalKeyframe];
        
        if (transform.rat_file_index >= AnimationData.ratFilePaths.Count)
            return null;
            
        string ratFileName = AnimationData.ratFilePaths[(int)transform.rat_file_index];
        
        return (transform.rat_file_index, transform.rat_local_frame, ratFileName);
    }
    
    /// <summary>
    /// Prints a summary of the RAT file mapping for debugging purposes.
    /// </summary>
    public void PrintRatFileMappingSummary()
    {
        if (AnimationData == null)
        {
            Debug.Log($"Actor '{name}': No animation data");
            return;
        }
        
        Debug.Log($"=== RAT File Mapping Summary for Actor '{name}' ===");
        Debug.Log($"Total keyframes: {AnimationData.transforms.Count}");
        Debug.Log($"RAT files referenced: {AnimationData.ratFilePaths.Count}");
        
        for (int i = 0; i < AnimationData.ratFilePaths.Count; i++)
        {
            Debug.Log($"  RAT File {i}: {AnimationData.ratFilePaths[i]}");
        }
        
        // Show first few and last few keyframe mappings
        int showCount = Mathf.Min(5, AnimationData.transforms.Count);
        
        Debug.Log($"First {showCount} keyframe mappings:");
        for (int i = 0; i < showCount; i++)
        {
            var info = GetRatFileInfoForKeyframe((uint)i);
            if (info.HasValue)
            {
                Debug.Log($"  Keyframe {i}: RAT file {info.Value.ratFileIndex}, local frame {info.Value.ratLocalFrame} ({Path.GetFileName(info.Value.ratFileName)})");
            }
        }
        
        if (AnimationData.transforms.Count > showCount * 2)
        {
            Debug.Log($"... (skipping middle keyframes) ...");
            
            Debug.Log($"Last {showCount} keyframe mappings:");
            for (int i = AnimationData.transforms.Count - showCount; i < AnimationData.transforms.Count; i++)
            {
                var info = GetRatFileInfoForKeyframe((uint)i);
                if (info.HasValue)
                {
                    Debug.Log($"  Keyframe {i}: RAT file {info.Value.ratFileIndex}, local frame {info.Value.ratLocalFrame} ({Path.GetFileName(info.Value.ratFileName)})");
                }
            }
        }
        
        Debug.Log("=== End RAT File Mapping Summary ===");
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
    
    /// <summary>
    /// Helper method to read a struct from binary stream
    /// </summary>
    private static T ReadStruct<T>(BinaryReader reader) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] bytes = reader.ReadBytes(size);
        
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(bytes, 0, ptr, size);
            return Marshal.PtrToStructure<T>(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Unity Editor validation - shows errors/warnings in Inspector
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
        }
    }
#endif
}
