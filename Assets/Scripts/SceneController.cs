using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System;

/// <summary>
/// Binary file header for Scene data files (.scn)
/// Compatible with C-based engines
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SceneHeader
{
    public uint magic;              // 'SCNE' = 0x454E4353
    public uint version;            // File format version (1)
    public uint num_components;     // Total number of tracked components
    public uint num_keyframes;      // Number of state keyframes
    public float framerate;         // Recording framerate
    public uint keyframes_offset;   // Offset to keyframe data array
    
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] reserved;         // Reserved for future use
}

/// <summary>
/// Component reference data
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SceneComponent
{
    public byte component_type;     // 0=Camera, 1=Actor, 2=Level
    public ushort component_id;     // Unique ID for this component instance
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
    public byte[] component_name;   // UTF-8 name (null-terminated)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
    public byte[] file_path;        // UTF-8 file path (null-terminated)
}

/// <summary>
/// State keyframe for all components at a specific time
/// Uses bit-packed activation states for memory efficiency
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SceneKeyframe
{
    public float timestamp;         // Time in seconds from recording start
    public uint active_mask_count;  // Number of uint32 masks that follow
    // Followed by variable number of uint32 activation masks
    // Each bit represents one component's active state
}

/// <summary>
/// Floating-point keyframe data used during recording (before compression)
/// </summary>
[System.Serializable]
public struct SceneKeyframeFloat
{
    public float timestamp;
    public List<bool> componentStates;
    
    public SceneKeyframeFloat(float time, int componentCount)
    {
        timestamp = time;
        componentStates = new List<bool>(new bool[componentCount]);
    }
}

/// <summary>
/// SceneController manages activation/deactivation of cameras, actors, and levels
/// and exports this data to a binary format for C engine integration
/// </summary>
public class SceneController : MonoBehaviour
{
    [Header("Recording Settings")]
    public string fileName = "scene_state";
    public float recordingFPS = 30f;
    public bool autoStartRecording = true;
    
    [Header("Component Discovery")]
    public bool scanChildrenForComponents = true;
    public bool trackCameras = true;
    public bool trackActors = true;
    public bool trackLevels = true;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool logStateChanges = true;
    
    // File format constants
    private const uint SCENE_FILE_MAGIC = 0x454E4353; // "SCNE"
    private const uint SCENE_FILE_VERSION = 1;
    
    // Component tracking
    private List<TrackedComponent> trackedComponents = new List<TrackedComponent>();
    private List<SceneKeyframeFloat> floatKeyframes = new List<SceneKeyframeFloat>();
    
    // Recording state
    private bool isRecording = false;
    private float recordingStartTime;
    private float lastRecordTime;
    private int keyframeCount = 0;
    
    [System.Serializable]
    private class TrackedComponent
    {
        public enum ComponentType : byte { Camera = 0, Actor = 1, Level = 2 }
        
        public ComponentType type;
        public ushort id;
        public string name;
        public string filePath;
        public MonoBehaviour component;
        public bool lastKnownState;
        
        public TrackedComponent(ComponentType t, ushort componentId, string componentName, MonoBehaviour comp)
        {
            type = t;
            id = componentId;
            name = componentName;
            component = comp;
            lastKnownState = comp.enabled;
            filePath = GetComponentFilePath(comp);
        }
        
        private string GetComponentFilePath(MonoBehaviour comp)
        {
            if (comp is RecordCamera camera)
                return camera.fileName + ".cam";
            else if (comp is Actor actor)
                return actor.BaseFilename + ".act";
            else if (comp is Level level)
                return level.outputFileName;
            else
                return "";
        }
        
        public bool IsActive()
        {
            return component != null && component.enabled && component.gameObject.activeInHierarchy;
        }
    }
    
    void Start()
    {
        DiscoverComponents();
        
        if (autoStartRecording)
        {
            StartRecording();
        }
        
        Debug.Log($"SceneController initialized with {trackedComponents.Count} components");
    }
    
    void Update()
    {
        if (!isRecording) return;
        
        // Check if enough time has passed for next frame
        float timeSinceLastRecord = Time.time - lastRecordTime;
        float targetInterval = 1f / recordingFPS;
        
        if (timeSinceLastRecord >= targetInterval)
        {
            RecordCurrentFrame();
            lastRecordTime = Time.time;
        }
        
        // Update debug info
        keyframeCount = floatKeyframes.Count;
    }
    
    void OnApplicationQuit()
    {
        if (isRecording && floatKeyframes.Count > 0)
        {
            Debug.Log("=== APPLICATION ENDING - SAVING SCENE DATA ===");
            SaveSceneFile();
        }
    }
    
    void OnDestroy()
    {
        if (isRecording && floatKeyframes.Count > 0)
        {
            SaveSceneFile();
        }
    }
    
    /// <summary>
    /// Discovers all trackable components in the scene
    /// </summary>
    private void DiscoverComponents()
    {
        trackedComponents.Clear();
        ushort componentId = 0;
        
        // Find all components to track
        MonoBehaviour[] allComponents = scanChildrenForComponents 
            ? FindObjectsOfType<MonoBehaviour>() 
            : GetComponentsInChildren<MonoBehaviour>();
        
        foreach (var comp in allComponents)
        {
            if (trackCameras && comp is RecordCamera camera)
            {
                trackedComponents.Add(new TrackedComponent(
                    TrackedComponent.ComponentType.Camera, 
                    componentId++, 
                    camera.gameObject.name, 
                    camera));
            }
            else if (trackActors && comp is Actor actor)
            {
                trackedComponents.Add(new TrackedComponent(
                    TrackedComponent.ComponentType.Actor, 
                    componentId++, 
                    actor.gameObject.name, 
                    actor));
            }
            else if (trackLevels && comp is Level level)
            {
                trackedComponents.Add(new TrackedComponent(
                    TrackedComponent.ComponentType.Level, 
                    componentId++, 
                    level.gameObject.name, 
                    level));
            }
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"Discovered {trackedComponents.Count} trackable components:");
            foreach (var comp in trackedComponents)
            {
                Debug.Log($"  {comp.type} #{comp.id}: {comp.name} -> {comp.filePath}");
            }
        }
    }
    
    /// <summary>
    /// Starts recording component states
    /// </summary>
    public void StartRecording()
    {
        if (isRecording)
        {
            Debug.LogWarning("SceneController is already recording");
            return;
        }
        
        isRecording = true;
        recordingStartTime = Time.time;
        lastRecordTime = Time.time;
        floatKeyframes.Clear();
        
        Debug.Log("=== SCENE RECORDING STARTED ===");
        Debug.Log($"Recording at {recordingFPS} FPS");
        Debug.Log($"Tracking {trackedComponents.Count} components");
        
        // Record initial state
        RecordCurrentFrame();
    }
    
    /// <summary>
    /// Stops recording and saves the scene file
    /// </summary>
    public void StopRecording()
    {
        if (!isRecording)
        {
            Debug.LogWarning("SceneController is not recording");
            return;
        }
        
        isRecording = false;
        
        if (floatKeyframes.Count > 0)
        {
            SaveSceneFile();
        }
        
        Debug.Log("=== SCENE RECORDING STOPPED ===");
    }
    
    /// <summary>
    /// Records the current state of all tracked components
    /// </summary>
    private void RecordCurrentFrame()
    {
        float currentTime = Time.time - recordingStartTime;
        SceneKeyframeFloat keyframe = new SceneKeyframeFloat(currentTime, trackedComponents.Count);
        
        bool stateChanged = false;
        
        for (int i = 0; i < trackedComponents.Count; i++)
        {
            var comp = trackedComponents[i];
            bool currentState = comp.IsActive();
            keyframe.componentStates[i] = currentState;
            
            if (currentState != comp.lastKnownState)
            {
                if (logStateChanges)
                {
                    Debug.Log($"State change: {comp.name} ({comp.type}) -> {(currentState ? "ACTIVE" : "INACTIVE")}");
                }
                comp.lastKnownState = currentState;
                stateChanged = true;
            }
        }
        
        floatKeyframes.Add(keyframe);
        
        if (showDebugInfo && floatKeyframes.Count % 60 == 0)
        {
            int activeCount = 0;
            for (int i = 0; i < trackedComponents.Count; i++)
            {
                if (keyframe.componentStates[i]) activeCount++;
            }
            
            Debug.Log($"Recording frame {floatKeyframes.Count}: {activeCount}/{trackedComponents.Count} components active");
        }
    }
    
    /// <summary>
    /// Saves scene state data to binary file
    /// </summary>
    private void SaveSceneFile()
    {
        if (floatKeyframes.Count == 0)
        {
            Debug.LogWarning("No scene data to save");
            return;
        }
        
        // Create GeneratedData directory
        string generatedDataPath = Path.Combine(Application.dataPath.Replace("Assets", ""), "GeneratedData");
        if (!Directory.Exists(generatedDataPath))
        {
            Directory.CreateDirectory(generatedDataPath);
        }
        
        string filePath = Path.Combine(generatedDataPath, fileName + ".scn");
        
        try
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                // Write header
                SceneHeader header = new SceneHeader
                {
                    magic = SCENE_FILE_MAGIC,
                    version = SCENE_FILE_VERSION,
                    num_components = (uint)trackedComponents.Count,
                    num_keyframes = (uint)floatKeyframes.Count,
                    framerate = recordingFPS,
                    keyframes_offset = (uint)(Marshal.SizeOf<SceneHeader>() + 
                                            (trackedComponents.Count * Marshal.SizeOf<SceneComponent>())),
                    reserved = new byte[32]
                };
                
                WriteSceneHeader(writer, header);
                
                // Write component definitions
                foreach (var comp in trackedComponents)
                {
                    WriteSceneComponent(writer, comp);
                }
                
                // Write keyframe data
                foreach (var keyframe in floatKeyframes)
                {
                    WriteSceneKeyframe(writer, keyframe);
                }
                
                writer.Flush();
                fs.Flush();
            }
            
            long fileSize = new FileInfo(filePath).Length;
            float duration = floatKeyframes.Count / recordingFPS;
            
            Debug.Log("=== SCENE DATA SAVED ===");
            Debug.Log($"Saved {floatKeyframes.Count} keyframes to {filePath}");
            Debug.Log($"Components tracked: {trackedComponents.Count}");
            Debug.Log($"Recording duration: {duration:F1} seconds at {recordingFPS} FPS");
            Debug.Log($"File size: {fileSize} bytes");
            Debug.Log($"Average state changes per second: {CalculateStateChangesPerSecond():F1}");
            
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save scene file: {e.Message}");
        }
    }
    
    /// <summary>
    /// Writes scene header to binary stream
    /// </summary>
    private void WriteSceneHeader(BinaryWriter writer, SceneHeader header)
    {
        writer.Write(header.magic);
        writer.Write(header.version);
        writer.Write(header.num_components);
        writer.Write(header.num_keyframes);
        writer.Write(header.framerate);
        writer.Write(header.keyframes_offset);
        writer.Write(header.reserved);
    }
    
    /// <summary>
    /// Writes component definition to binary stream
    /// </summary>
    private void WriteSceneComponent(BinaryWriter writer, TrackedComponent comp)
    {
        writer.Write((byte)comp.type);
        writer.Write(comp.id);
        
        // Write name as fixed-size UTF-8 string
        byte[] nameBytes = new byte[64];
        var nameUtf8 = System.Text.Encoding.UTF8.GetBytes(comp.name);
        Array.Copy(nameUtf8, nameBytes, Math.Min(nameUtf8.Length, 63));
        writer.Write(nameBytes);
        
        // Write file path as fixed-size UTF-8 string
        byte[] pathBytes = new byte[64];
        var pathUtf8 = System.Text.Encoding.UTF8.GetBytes(comp.filePath);
        Array.Copy(pathUtf8, pathBytes, Math.Min(pathUtf8.Length, 63));
        writer.Write(pathBytes);
    }
    
    /// <summary>
    /// Writes keyframe data to binary stream with bit-packed states
    /// </summary>
    private void WriteSceneKeyframe(BinaryWriter writer, SceneKeyframeFloat keyframe)
    {
        writer.Write(keyframe.timestamp);
        
        // Calculate number of 32-bit masks needed
        uint maskCount = (uint)((trackedComponents.Count + 31) / 32);
        writer.Write(maskCount);
        
        // Pack activation states into 32-bit masks
        for (uint maskIndex = 0; maskIndex < maskCount; maskIndex++)
        {
            uint mask = 0;
            uint baseIndex = maskIndex * 32;
            
            for (uint bitIndex = 0; bitIndex < 32; bitIndex++)
            {
                uint componentIndex = baseIndex + bitIndex;
                if (componentIndex < trackedComponents.Count && 
                    componentIndex < keyframe.componentStates.Count &&
                    keyframe.componentStates[(int)componentIndex])
                {
                    mask |= (1u << (int)bitIndex);
                }
            }
            
            writer.Write(mask);
        }
    }
    
    /// <summary>
    /// Calculates average state changes per second for performance metrics
    /// </summary>
    private float CalculateStateChangesPerSecond()
    {
        if (floatKeyframes.Count < 2) return 0f;
        
        int totalChanges = 0;
        float totalTime = floatKeyframes[floatKeyframes.Count - 1].timestamp;
        
        for (int frame = 1; frame < floatKeyframes.Count; frame++)
        {
            for (int comp = 0; comp < trackedComponents.Count; comp++)
            {
                if (floatKeyframes[frame].componentStates[comp] != 
                    floatKeyframes[frame - 1].componentStates[comp])
                {
                    totalChanges++;
                }
            }
        }
        
        return totalTime > 0 ? totalChanges / totalTime : 0f;
    }
    
    /// <summary>
    /// Gets current activation state of a specific component
    /// </summary>
    /// <param name="componentName">Name of the component to check</param>
    /// <returns>True if component is active, false otherwise</returns>
    public bool IsComponentActive(string componentName)
    {
        var comp = trackedComponents.Find(c => c.name == componentName);
        return comp?.IsActive() ?? false;
    }
    
    /// <summary>
    /// Sets activation state of a specific component
    /// </summary>
    /// <param name="componentName">Name of the component to modify</param>
    /// <param name="active">Desired activation state</param>
    public void SetComponentActive(string componentName, bool active)
    {
        var comp = trackedComponents.Find(c => c.name == componentName);
        if (comp?.component != null)
        {
            comp.component.enabled = active;
            
            if (logStateChanges)
            {
                Debug.Log($"Manual state change: {comp.name} ({comp.type}) -> {(active ? "ACTIVE" : "INACTIVE")}");
            }
        }
    }
    
    /// <summary>
    /// Gets list of all tracked component names
    /// </summary>
    /// <returns>Array of component names</returns>
    public string[] GetTrackedComponentNames()
    {
        string[] names = new string[trackedComponents.Count];
        for (int i = 0; i < trackedComponents.Count; i++)
        {
            names[i] = trackedComponents[i].name;
        }
        return names;
    }
    
    void OnGUI()
    {
        if (!showDebugInfo) return;
        
        GUILayout.BeginArea(new Rect(10, 200, 350, 200));
        GUILayout.Label("Scene Controller", GUI.skin.box);
        GUILayout.Label($"Recording: {(isRecording ? "ON" : "OFF")}");
        GUILayout.Label($"Components: {trackedComponents.Count}");
        GUILayout.Label($"Keyframes: {floatKeyframes.Count}");
        GUILayout.Label($"Duration: {(floatKeyframes.Count > 0 ? floatKeyframes.Count / recordingFPS : 0):F1}s");
        
        if (isRecording)
        {
            int activeCount = 0;
            foreach (var comp in trackedComponents)
            {
                if (comp.IsActive()) activeCount++;
            }
            GUILayout.Label($"Active: {activeCount}/{trackedComponents.Count}");
        }
        
        GUILayout.Label("");
        if (GUILayout.Button(isRecording ? "Stop Recording" : "Start Recording"))
        {
            if (isRecording) StopRecording();
            else StartRecording();
        }
        
        GUILayout.EndArea();
    }
    
#if UNITY_EDITOR
    /// <summary>
    /// Menu option to add SceneController to active GameObject
    /// </summary>
    [UnityEditor.MenuItem("Tools/Add Scene Controller")]
    public static void AddSceneController()
    {
        var activeGO = UnityEditor.Selection.activeGameObject;
        if (activeGO == null)
        {
            Debug.LogError("Please select a GameObject to add SceneController to");
            return;
        }
        
        if (activeGO.GetComponent<SceneController>() != null)
        {
            Debug.LogWarning("GameObject already has a SceneController component");
            return;
        }
        
        activeGO.AddComponent<SceneController>();
        Debug.Log($"Added SceneController to {activeGO.name}");
    }
    
    /// <summary>
    /// Menu option to find and create a scene controller in the current scene
    /// </summary>
    [UnityEditor.MenuItem("Tools/Create Scene Controller")]
    public static void CreateSceneController()
    {
        // Check if one already exists
        var existing = FindObjectOfType<SceneController>();
        if (existing != null)
        {
            Debug.LogWarning($"SceneController already exists on {existing.gameObject.name}");
            UnityEditor.Selection.activeGameObject = existing.gameObject;
            return;
        }
        
        // Create new GameObject with SceneController
        GameObject controllerGO = new GameObject("SceneController");
        controllerGO.AddComponent<SceneController>();
        UnityEditor.Selection.activeGameObject = controllerGO;
        Debug.Log("Created new SceneController GameObject");
    }
#endif
    
    /// <summary>
    /// C Engine Integration Documentation - Scene File Format V1
    /// 
    /// SCENE FILE FORMAT (.scn):
    /// The scene file stores component activation states over time with bit-packed
    /// compression for memory efficiency.
    /// 
    /// FILE STRUCTURE:
    /// 1. Header (56 bytes):
    ///    - magic: 'SCNE' (0x454E4353)
    ///    - version: 1
    ///    - num_components: Number of tracked components
    ///    - num_keyframes: Number of state keyframes
    ///    - framerate: Recording frame rate (float)
    ///    - keyframes_offset: Offset to keyframe data
    ///    - reserved: 32 bytes for future use
    /// 
    /// 2. Component Definitions (130 bytes each):
    ///    - component_type: 0=Camera, 1=Actor, 2=Level
    ///    - component_id: Unique identifier (uint16)
    ///    - component_name: UTF-8 name (64 bytes, null-terminated)
    ///    - file_path: UTF-8 file path (64 bytes, null-terminated)
    /// 
    /// 3. Keyframe Data (8 + 4*mask_count bytes per keyframe):
    ///    - timestamp: Time in seconds (float)
    ///    - active_mask_count: Number of uint32 masks that follow
    ///    - activation_masks: Bit-packed component states (uint32 array)
    /// 
    /// BIT-PACKING FORMAT:
    /// - Each uint32 mask contains up to 32 component states
    /// - Bit 0 = component ID 0, Bit 1 = component ID 1, etc.
    /// - 1 = component active, 0 = component inactive
    /// - Total masks = (num_components + 31) / 32
    /// 
    /// C LOADING EXAMPLE:
    /// ```c
    /// typedef struct {
    ///     uint32_t magic;
    ///     uint32_t version;
    ///     uint32_t num_components;
    ///     uint32_t num_keyframes;
    ///     float framerate;
    ///     uint32_t keyframes_offset;
    ///     uint8_t reserved[32];
    /// } SceneHeader;
    /// 
    /// typedef struct {
    ///     uint8_t component_type;
    ///     uint16_t component_id;
    ///     char component_name[64];
    ///     char file_path[64];
    /// } SceneComponent;
    /// 
    /// typedef struct {
    ///     float timestamp;
    ///     uint32_t active_mask_count;
    /// } SceneKeyframeHeader;
    /// 
    /// // Load scene file
    /// SceneHeader header;
    /// fread(&header, sizeof(SceneHeader), 1, file);
    /// 
    /// SceneComponent* components = malloc(sizeof(SceneComponent) * header.num_components);
    /// fread(components, sizeof(SceneComponent), header.num_components, file);
    /// 
    /// // Read keyframe
    /// SceneKeyframeHeader kf_header;
    /// fread(&kf_header, sizeof(SceneKeyframeHeader), 1, file);
    /// 
    /// uint32_t* masks = malloc(sizeof(uint32_t) * kf_header.active_mask_count);
    /// fread(masks, sizeof(uint32_t), kf_header.active_mask_count, file);
    /// 
    /// // Check if component ID 5 is active
    /// uint32_t mask_index = 5 / 32;
    /// uint32_t bit_index = 5 % 32;
    /// bool is_active = (masks[mask_index] & (1 << bit_index)) != 0;
    /// 
    /// // Use timestamp for interpolation
    /// float time_between_frames = 1.0f / header.framerate;
    /// ```
    /// 
    /// BENEFITS:
    /// - Bit-packing: Up to 32x compression vs boolean arrays
    /// - Time-based: Supports variable frame rates and interpolation
    /// - Component metadata: Direct mapping to asset files
    /// - Efficient seeking: Fixed-size records for fast random access
    /// - Scalable: Handles hundreds of components efficiently
    /// </summary>
}
