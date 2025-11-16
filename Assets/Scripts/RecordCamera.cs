using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;

[System.Serializable]
public struct CameraKeyframe
{
    public short posX, posY, posZ;     // Camera position (scaled)
    public short yaw, pitch, roll;     // Camera orientation (scaled radians)
    public byte fov;                   // Field of view (0-255, representing 0-255 degrees)
}

/// <summary>
/// Floating-point camera data used during recording (before compression)
/// </summary>
[System.Serializable]
public struct CameraKeyframeFloat
{
    public Vector3 position;    // World position
    public Vector3 rotation;    // World rotation (Euler angles in degrees)
    public float fov;          // Field of view in degrees
}

public class RecordCamera : MonoBehaviour
{
    [Header("Recording Settings")]
    public string fileName = "camera_track";
    public float recordingFPS = 30f; // Target recording frame rate

    // export options for coordinate conventions
    [Tooltip("Flip Z axis on export (Unity -> right-handed)")]
    public bool flipZ = true;

    [Tooltip("Invert Y/Z Euler angles when flipping Z")]
    public bool flipRotationWithZ = true;

    [Header("Debug Info")]
    private int keyframeCount = 0; // Read-only field (not shown in Inspector)
    public bool showDebugInfo = true;
    
    private List<CameraKeyframe> keyframes = new List<CameraKeyframe>();
    private bool isRecording = true; // Start recording immediately
    private Camera targetCamera; // Cache the camera component
    
    // Dynamic bounds for position scaling (calculated from actual movement)
    private Vector3 positionMin = Vector3.positiveInfinity;
    private Vector3 positionMax = Vector3.negativeInfinity;
    
    // Temporary storage of float data before compression
    private List<CameraKeyframeFloat> floatKeyframes = new List<CameraKeyframeFloat>();
    
    // For frame rate limiting
    private float lastRecordTime = 0f;
    
    // File format constants
    private const uint CAM_FILE_MAGIC = 0x43414D30; // "CAM0"
    private const uint CAM_FILE_VERSION = 1;
    
    void Start()
    {
        // Get the camera component attached to this GameObject
        targetCamera = GetComponent<Camera>();
        
        if (targetCamera == null)
        {
            Debug.LogError("RecordCamera: No Camera component found! This component must be attached to a GameObject with a Camera.");
            return;
        }
        
        // Start recording immediately
        isRecording = true;
        lastRecordTime = Time.time; // Initialize recording timer
    Debug.Log("Camera recording started");
    Debug.Log($"Recording at {recordingFPS} FPS");
    Debug.Log("Recording camera keyframes");
    Debug.Log("File will be saved on application quit");
    }
    
    void Update()
    {
        if (targetCamera == null || !isRecording) return;
        
        // Check if enough time has passed for next frame
        float timeSinceLastRecord = Time.time - lastRecordTime;
        float targetInterval = 1f / recordingFPS;
        
        if (timeSinceLastRecord >= targetInterval)
        {
            // Record current frame at target FPS
            RecordCurrentFrame();
            lastRecordTime = Time.time;
        }
        
        // Update debug info
        keyframeCount = floatKeyframes.Count;
        
        // Debug: Create test file with T key
        if (Input.GetKeyDown(KeyCode.T))
        {
            Debug.Log("Creating test file...");
            SaveTestFile();
        }
    }
    
    void OnApplicationQuit()
    {
        // Save the recording when the application quits
        if (floatKeyframes.Count > 0)
        {
            Debug.Log("Application ending - saving camera data");
            SaveCameraFile();
        }
        else
        {
            Debug.Log("No camera data recorded during session");
        }
    }
    
    void OnDestroy()
    {
        // Also save when this component is destroyed
        if (floatKeyframes.Count > 0)
        {
            SaveCameraFile();
        }
    }
    
    void ToggleRecording()
    {
        // Keep for compatibility but not used in auto mode
        isRecording = !isRecording;
        
        if (isRecording)
        {
            Debug.Log("Camera recording resumed");
        }
        else
        {
            Debug.Log("Camera recording paused");
        }
    }
    
    void RecordCurrentFrame()
    {
        Transform camTransform = targetCamera.transform;
        
        // Get position
        Vector3 position = camTransform.position;
        
        // Get rotation in Euler angles 
        Vector3 eulerAngles = camTransform.eulerAngles;
        
        // Get field of view
        float fov = targetCamera.fieldOfView;
        
        // Apply optional coordinate conversion for export
        Vector3 exportPos = position;
        Vector3 exportRot = eulerAngles;
        if (flipZ)
        {
            exportPos.z = -exportPos.z;
            if (flipRotationWithZ)
            {
                // Reflect rotation to match the Z-flip: invert yaw and roll (Y and Z Euler)
                exportRot.y = -exportRot.y;
                exportRot.z = -exportRot.z;
            }
        }
        
        // Create float keyframe (export-space)
        CameraKeyframeFloat floatKeyframe = new CameraKeyframeFloat
        {
            position = exportPos,
            rotation = exportRot,
            fov = fov
        };
        
        floatKeyframes.Add(floatKeyframe);
        
        // Update position bounds for dynamic scaling (use export position for consistency)
        positionMin = Vector3.Min(positionMin, exportPos);
        positionMax = Vector3.Max(positionMax, exportPos);
        
        // Debug output every 60 frames
        if (showDebugInfo && floatKeyframes.Count % 60 == 0)
        {
            float recordingDuration = Time.time - (lastRecordTime - (floatKeyframes.Count / recordingFPS));
            Debug.Log($"Recording frame {floatKeyframes.Count} at {recordingFPS} FPS: pos=({position.x:F2}, {position.y:F2}, {position.z:F2}) " +
                     $"rot=({eulerAngles.x:F1}, {eulerAngles.y:F1}, {eulerAngles.z:F1}) fov={fov:F1}");
            Debug.Log($"Recording duration: {recordingDuration:F1}s, Estimated final keyframes: {recordingDuration * recordingFPS:F0}");
            Debug.Log($"Position bounds: min=({positionMin.x:F2}, {positionMin.y:F2}, {positionMin.z:F2}) " +
                     $"max=({positionMax.x:F2}, {positionMax.y:F2}, {positionMax.z:F2})");
        }
    }
    
    void SaveCameraFile()
    {
        if (floatKeyframes.Count == 0)
        {
            Debug.LogWarning("No camera data to save");
            return;
        }
        
        // Compress float data to fixed-point format
        CompressFloatDataToKeyframes();
        
        // Create GeneratedData directory if it doesn't exist
        string generatedDataPath = Path.Combine(Application.dataPath.Replace("Assets", ""), "GeneratedData");
        if (!Directory.Exists(generatedDataPath))
        {
            Directory.CreateDirectory(generatedDataPath);
            Debug.Log($"Created GeneratedData directory at: {generatedDataPath}");
        }
        
        string filePath = Path.Combine(generatedDataPath, fileName + ".cam");
        
        try
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                // Write header - ensure exact byte layout
                writer.Write(CAM_FILE_MAGIC);      // 4 bytes
                writer.Write(CAM_FILE_VERSION);    // 4 bytes  
                writer.Write((uint)keyframes.Count); // 4 bytes
                writer.Write((uint)recordingFPS);  // 4 bytes - frame rate as integer
                
                // Write position bounds for decompression (6 floats = 24 bytes)
                writer.Write(positionMin.x);       // 4 bytes
                writer.Write(positionMin.y);       // 4 bytes
                writer.Write(positionMin.z);       // 4 bytes
                writer.Write(positionMax.x);       // 4 bytes
                writer.Write(positionMax.y);       // 4 bytes
                writer.Write(positionMax.z);       // 4 bytes
                // Header total: 16 + 24 = 40 bytes
                
                // Write keyframes - ensure exact struct layout without any padding
                foreach (var keyframe in keyframes)
                {
                    // Write each field individually to guarantee order and no padding
                    writer.Write(keyframe.posX);   // 2 bytes
                    writer.Write(keyframe.posY);   // 2 bytes
                    writer.Write(keyframe.posZ);   // 2 bytes
                    writer.Write(keyframe.yaw);    // 2 bytes
                    writer.Write(keyframe.pitch);  // 2 bytes
                    writer.Write(keyframe.roll);   // 2 bytes
                    writer.Write(keyframe.fov);    // 1 byte
                    // Total: 13 bytes per keyframe, no padding
                }
                
                // Flush to ensure all data is written
                writer.Flush();
                fs.Flush();
            } 
            
            long fileSize = new FileInfo(filePath).Length;
            long expectedSize = 40 + (keyframes.Count * 13); // Header: 40 bytes, Keyframe: 13 bytes
            
            Debug.Log("Camera data saved");
            Debug.Log($"Saved {keyframes.Count} camera keyframes to {filePath}");
            Debug.Log($"Recording FPS: {recordingFPS} (captured at {recordingFPS} frames per second)");
            Debug.Log($"Total recording duration: {keyframes.Count / recordingFPS:F1} seconds");
            Debug.Log($"File size: {fileSize} bytes (expected: {expectedSize} bytes)");
            Debug.Log($"Position bounds: min=({positionMin.x:F2}, {positionMin.y:F2}, {positionMin.z:F2}) " +
                     $"max=({positionMax.x:F2}, {positionMax.y:F2}, {positionMax.z:F2})");
            
            if (fileSize != expectedSize)
            {
                Debug.LogError($"FILE SIZE MISMATCH! Got {fileSize}, expected {expectedSize}");
            }
            
            // Debug: Show first keyframe's raw bytes
            if (keyframes.Count > 0)
            {
                var firstFrame = keyframes[0];
                Debug.Log($"First keyframe raw values: posX={firstFrame.posX}, posY={firstFrame.posY}, posZ={firstFrame.posZ}");
                Debug.Log($"                           yaw={firstFrame.yaw}, pitch={firstFrame.pitch}, roll={firstFrame.roll}, fov={firstFrame.fov}");
            }
            
            Debug.Log($"Binary layout: CameraKeyframe = posX(2) + posY(2) + posZ(2) + yaw(2) + pitch(2) + roll(2) + fov(1) = 13 bytes");
            Debug.Log($"Header layout: magic(4) + version(4) + count(4) + fps(4) + position_bounds(24) = 40 bytes");
            Debug.Log("Copy this file to your C program's assets folder");
            Debug.Log("========================");
            
            // Also log the persistent data path for easy access
            Debug.Log($"File saved to: {filePath}");
            
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save camera file: {e.Message}");
        }
    }
    
    void ClearRecording()
    {
        keyframes.Clear();
        floatKeyframes.Clear();
        positionMin = Vector3.positiveInfinity;
        positionMax = Vector3.negativeInfinity;
        Debug.Log("Camera recording data cleared");
    }
    
    // Compression methods
    void CompressFloatDataToKeyframes()
    {
        keyframes.Clear();
        
        if (floatKeyframes.Count == 0) return;
        
        Debug.Log($"Compressing {floatKeyframes.Count} camera keyframes with dynamic scaling...");
        Debug.Log($"Position bounds: min=({positionMin.x:F3}, {positionMin.y:F3}, {positionMin.z:F3}) " +
                  $"max=({positionMax.x:F3}, {positionMax.y:F3}, {positionMax.z:F3})");
        
        foreach (var floatKeyframe in floatKeyframes)
        {
            CameraKeyframe fixedKeyframe = new CameraKeyframe
            {
                posX = FloatToFixed16(floatKeyframe.position.x, positionMin.x, positionMax.x),
                posY = FloatToFixed16(floatKeyframe.position.y, positionMin.y, positionMax.y),
                posZ = FloatToFixed16(floatKeyframe.position.z, positionMin.z, positionMax.z),
                
                yaw = DegreesToFixed16(floatKeyframe.rotation.y),
                pitch = DegreesToFixed16(floatKeyframe.rotation.x),
                roll = DegreesToFixed16(floatKeyframe.rotation.z),
                
                fov = FloatToUInt8FOV(floatKeyframe.fov)
            };
            
            keyframes.Add(fixedKeyframe);
        }
    }
    
    // Helper functions for 16-bit fixed-point conversion with dynamic bounds
    short FloatToFixed16(float value, float min, float max)
    {
        if (min >= max)
        {
            // No variation in this axis, return 0
            return 0;
        }
        
        // Normalize to 0-1 range
        float normalized = (value - min) / (max - min);
        
        // Scale to 16-bit range and convert to signed short
        float scaled = normalized * 65535f - 32768f;
        
        if (scaled > 32767f) return 32767;
        if (scaled < -32768f) return -32768;
        return (short)scaled;
    }
    
    // Helper function for degree to 16-bit fixed-point (handles modulation/wrapping)
    short DegreesToFixed16(float degrees)
    {
        // Normalize to 0-360 range
        float normalized = degrees % 360f;
        if (normalized < 0f) normalized += 360f;
        
        // Scale to 16-bit unsigned range (0-65535)
        float scaled = (normalized / 360f) * 65535f;
        
        return (short)((ushort)scaled - 32768); // Convert to signed short centered around 0
    }
    
    byte FloatToUInt8FOV(float fovDegrees)
    {
        if (fovDegrees < 0.0f) return 0;
        if (fovDegrees > 255.0f) return 255;
        return (byte)fovDegrees;
    }
    
    // Test function to create a simple file for debugging
    void SaveTestFile()
    {
        // Create test data with known values
        List<CameraKeyframeFloat> testFloatData = new List<CameraKeyframeFloat>
        {
            new CameraKeyframeFloat { position = new Vector3(1f, 2f, 3f), rotation = new Vector3(10f, 20f, 30f), fov = 60f },
            new CameraKeyframeFloat { position = new Vector3(4f, 5f, 6f), rotation = new Vector3(40f, 50f, 60f), fov = 90f }
        };
        
        // Calculate bounds
        Vector3 testMin = new Vector3(1f, 2f, 3f);
        Vector3 testMax = new Vector3(4f, 5f, 6f);
        
        // Convert to fixed-point
        List<CameraKeyframe> testKeyframes = new List<CameraKeyframe>();
        foreach (var floatFrame in testFloatData)
        {
            testKeyframes.Add(new CameraKeyframe
            {
                posX = FloatToFixed16(floatFrame.position.x, testMin.x, testMax.x),
                posY = FloatToFixed16(floatFrame.position.y, testMin.y, testMax.y),
                posZ = FloatToFixed16(floatFrame.position.z, testMin.z, testMax.z),
                yaw = DegreesToFixed16(floatFrame.rotation.y),
                pitch = DegreesToFixed16(floatFrame.rotation.x),
                roll = DegreesToFixed16(floatFrame.rotation.z),
                fov = FloatToUInt8FOV(floatFrame.fov)
            });
        }
        
        // Create GeneratedData directory if it doesn't exist
        string generatedDataPath = Path.Combine(Application.dataPath.Replace("Assets", ""), "GeneratedData");
        if (!Directory.Exists(generatedDataPath))
        {
            Directory.CreateDirectory(generatedDataPath);
        }
        
        string filePath = Path.Combine(generatedDataPath, "test.cam");
        
        try
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                // Write header
                writer.Write(CAM_FILE_MAGIC);      // 0x43414D30
                writer.Write(CAM_FILE_VERSION);    // 1
                writer.Write((uint)testKeyframes.Count); // 2 keyframes
                writer.Write((uint)30);            // 30 FPS as integer
                
                // Write position bounds
                writer.Write(testMin.x);           // 1.0f
                writer.Write(testMin.y);           // 2.0f
                writer.Write(testMin.z);           // 3.0f
                writer.Write(testMax.x);           // 4.0f
                writer.Write(testMax.y);           // 5.0f
                writer.Write(testMax.z);           // 6.0f
                
                // Write keyframes
                foreach (var keyframe in testKeyframes)
                {
                    writer.Write(keyframe.posX);
                    writer.Write(keyframe.posY);
                    writer.Write(keyframe.posZ);
                    writer.Write(keyframe.yaw);
                    writer.Write(keyframe.pitch);
                    writer.Write(keyframe.roll);
                    writer.Write(keyframe.fov);
                }
                
                writer.Flush();
                fs.Flush();
            }
            
            long fileSize = new FileInfo(filePath).Length;
            Debug.Log($"Test file created: {filePath}");
            Debug.Log($"Test file size: {fileSize} bytes (expected: 66 bytes)"); // 40 header + 2*13 keyframes
            Debug.Log($"Position bounds used: min=({testMin.x}, {testMin.y}, {testMin.z}) max=({testMax.x}, {testMax.y}, {testMax.z})");
            Debug.Log("Test data uses dynamic scaling for position and proper degree modulation for rotation");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save test file: {e.Message}");
        }
    }
    
    /// <summary>
    /// C Engine Integration Documentation - Camera File Format V1
    /// 
    /// CAMERA FILE FORMAT (.cam):
    /// The camera file stores compressed camera animation data with dynamic position scaling
    /// and proper rotation modulation handling.
    /// 
    /// FILE STRUCTURE:
    /// 1. Header (40 bytes):
    ///    - magic: 'CAM0' (0x43414D30)
    ///    - version: 1
    ///    - keyframe_count: Number of camera keyframes
    ///    - fps: Recording frame rate (uint32_t integer)
    ///    - position_min_x, position_min_y, position_min_z: Minimum position bounds (floats)
    ///    - position_max_x, position_max_y, position_max_z: Maximum position bounds (floats)
    /// 
    /// 2. Keyframe Data (13 bytes per keyframe):
    ///    - posX, posY, posZ: Position (16-bit fixed-point, dynamically scaled)
    ///    - yaw, pitch, roll: Rotation (16-bit fixed-point, 0-360° with modulation)
    ///    - fov: Field of view (8-bit, 0-255 degrees)
    /// 
    /// DECOMPRESSION FORMULAS (for your C engine):
    /// 
    /// Position (per axis):
    ///   float world_pos = position_min + ((fixed_value + 32768) / 65535.0f) * (position_max - position_min)
    /// 
    /// Rotation (per axis):
    ///   float degrees = ((fixed_value + 32768) / 65535.0f) * 360.0f
    /// 
    /// Field of View:
    ///   float fov_degrees = (float)fov_byte
    /// 
    /// C LOADING EXAMPLE:
    /// ```c
    /// typedef struct {
    ///     uint32_t magic;
    ///     uint32_t version;
    ///     uint32_t keyframe_count;
    ///     uint32_t fps;
    ///     float position_min_x, position_min_y, position_min_z;
    ///     float position_max_x, position_max_y, position_max_z;
    /// } CameraHeader;
    /// 
    /// typedef struct {
    ///     int16_t posX, posY, posZ;
    ///     int16_t yaw, pitch, roll;
    ///     uint8_t fov;
    /// } CameraKeyframe;
    /// 
    /// // Load camera file
    /// CameraHeader header;
    /// fread(&header, sizeof(CameraHeader), 1, file);
    /// 
    /// CameraKeyframe* keyframes = malloc(sizeof(CameraKeyframe) * header.keyframe_count);
    /// fread(keyframes, sizeof(CameraKeyframe), header.keyframe_count, file);
    /// 
    /// // Decompress keyframe
    /// float pos_x = header.position_min_x + ((keyframes[i].posX + 32768) / 65535.0f) * 
    ///               (header.position_max_x - header.position_min_x);
    /// float yaw_deg = ((keyframes[i].yaw + 32768) / 65535.0f) * 360.0f;
    /// float fov_deg = (float)keyframes[i].fov;
    /// 
    /// // Use the FPS for time calculations
    /// float time_between_frames = 1.0f / (float)header.fps;
    /// float keyframe_time = i * time_between_frames;
    /// ```
    /// 
    /// PRECISION ANALYSIS:
    /// - Position: Precision depends on camera movement bounds (smaller movement = higher precision)
    /// - Rotation: ~0.0055° precision per step (16-bit = 65,535 discrete values for 360°)
    /// - FOV: 1° precision per step (8-bit = 256 discrete values for 0-255°)
    /// 
    /// BENEFITS:
    /// - Dynamic scaling: Optimal precision for any camera movement range
    /// - Rotation modulation: Handles continuous rotation and values > 360°
    /// - Compact: 13 bytes per keyframe vs 28 bytes for uncompressed floats
    /// - Compatible: Direct binary loading in C with no endianness issues
    /// </summary>
    
    void OnGUI()
    {
        if (!showDebugInfo) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 180));
        GUILayout.Label("Camera Recorder (30 FPS Mode)", GUI.skin.box);
        GUILayout.Label($"Recording: {(isRecording ? "ON" : "OFF")}");
        GUILayout.Label($"Target FPS: {recordingFPS}");
        GUILayout.Label($"Keyframes: {floatKeyframes.Count}");
        GUILayout.Label($"Duration: {floatKeyframes.Count / recordingFPS:F1}s");
        GUILayout.Label($"Camera: {gameObject.name}");
        GUILayout.Label("");
        GUILayout.Label("Recording at reduced frame rate");
        GUILayout.Label("File will be saved on quit");
        GUILayout.EndArea();
    }
}
