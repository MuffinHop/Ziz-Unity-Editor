using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System;
using System.Linq;
using ZizSceneEditor.Assets.Scripts.Shapes;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

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
    public byte[] reserved;         // Reserved for future use (matching C struct)
}

/// <summary>
/// Keyframe state data for scene timeline
/// </summary>
[System.Serializable]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SceneKeyframeData
{
    public float timestamp;                           // Time in seconds from start of recording
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public bool[] activeComponentIndices;             // Bit-packed as bools in C# for clarity
    
    public SceneKeyframeData(float time, int componentCount)
    {
        timestamp = time;
        activeComponentIndices = new bool[componentCount];
    }
}

/// <summary>
/// Component data for scene tracking (binary format - must be struct for marshaling)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SceneComponentData
{
    public byte component_type;     // 0=Camera, 1=Actor, 2=Level, 3=Shape
    public ushort component_id;     // Unique ID for this component instance
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
    public byte[] component_name;   // UTF-8 name (null-terminated)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
    public byte[] file_path;        // UTF-8 file path (null-terminated)
}

/// <summary>
/// Keyframe state for scene timeline (binary format - must be struct for marshaling)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SceneKeyframeHeaderData
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
/// Component type enumeration (must be outside SceneController for use in other scripts)
/// Matches C enum values exactly for binary compatibility
/// </summary>
public enum ComponentType
{
    Camera = 0,  // Must be 0
    Actor = 1,   // Must be 1
    Level = 2,   // Must be 2
    Shape = 3    // Must be 3
}

/// <summary>
/// SceneController manages activation/deactivation of cameras, actors, and levels
/// and exports this data to a binary format for C engine integration
/// </summary>
public class SceneController : MonoBehaviour
{
    [System.Serializable]
    public class TrackedComponent
    {
        public ComponentType type;
        public ushort id;
        public byte[] name = new byte[64];
        public byte[] filePath = new byte[256];
        public GameObject obj;
        public MonoBehaviour component;
        public bool wasActive;

        public TrackedComponent(MonoBehaviour component, string name, string path, ComponentType type, ushort id)
        {
            this.obj = component.gameObject;
            this.component = component;
            this.type = type;
            this.id = id;
            this.name = SceneController.PadAndTruncate(name, 64);
            this.filePath = SceneController.PadAndTruncate(path, 256);
            this.wasActive = component.gameObject.activeSelf;
        }

        public bool IsActive()
        {
            return obj != null && obj.activeInHierarchy;
        }

        public void UpdateWasActive()
        {
            wasActive = IsActive();
        }

        public string GetName()
        {
            return System.Text.Encoding.UTF8.GetString(name).TrimEnd('\0');
        }
    }
    
    [Header("Recording Settings")]
    public string sceneName = "scene";
    public float recordingFPS = 30f;
    public bool logStateChanges = false; // If true, only records when a component's state changes

    [Header("Debug Info")]
    [SerializeField] private bool isRecording = false;
    [SerializeField] private float recordingStartTime = 0f;
    [SerializeField] private int keyframeCount = 0;

    private List<TrackedComponent> trackedComponents = new List<TrackedComponent>();
    private List<SceneKeyframeFloat> keyframes = new List<SceneKeyframeFloat>();
    private float lastRecordTime = 0f;

    private const uint SCENE_FILE_MAGIC = 0x454E4353;  // "SCNE"
    private const uint SCENE_FILE_VERSION = 1;
    private const int MAX_COMPONENT_NAME_LENGTH = 64;
    private const int MAX_COMPONENT_PATH_LENGTH = 256;

    private static byte[] PadAndTruncate(string str, int length)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(str);
        if (bytes.Length == length)
        {
            return bytes;
        }
        if (bytes.Length > length)
        {
            Array.Resize(ref bytes, length);
            return bytes;
        }
        
        byte[] paddedBytes = new byte[length];
        Buffer.BlockCopy(bytes, 0, paddedBytes, 0, bytes.Length);
        return paddedBytes;
    }

    void Start()
    {
        StartRecording();
    }

#if UNITY_EDITOR
    private void OnEnable()
    {
    }

    private void OnDisable()
    {
        StopRecording();
        FileLogger.Close();
    }

#endif

    void Update()
    {
        if (!isRecording) return;

        float targetInterval = 1f / recordingFPS;
        float timeSinceLastRecord = Time.time - lastRecordTime;

        if (timeSinceLastRecord >= targetInterval)
        {
            RecordCurrentFrame();
            lastRecordTime = Time.time;
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 300, 150));
        GUILayout.Box("Scene Controller", GUILayout.Width(280));
        
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Start Recording", GUILayout.Height(40)))
        {
            StartRecording();
        }
        if (GUILayout.Button("Stop Recording", GUILayout.Height(40)))
        {
            StopRecording();
        }
        GUILayout.EndHorizontal();
        
        GUILayout.Label($"Recording: {(isRecording ? "YES" : "NO")}");
        GUILayout.Label($"Keyframes: {keyframeCount}");
        
        GUILayout.EndArea();
    }

    [ContextMenu("Start Recording")]
    public void StartRecording()
    {
        FileLogger.Initialize();
        
        if (isRecording)
        {
            FileLogger.LogWarning("Scene recording is already in progress.");
            return;
        }

        FileLogger.LogSection("SCENE RECORDING STARTED");
        isRecording = true;
        recordingStartTime = Time.time;
        lastRecordTime = Time.time;
        keyframeCount = 0;
        keyframes.Clear();
        
        InitializeTrackedComponents();
        
        // Record the initial state
        RecordCurrentFrame();
    }

    [ContextMenu("Stop Recording")]
    public void StopRecording()
    {
        if (!isRecording)
        {
            FileLogger.LogWarning("Scene recording is not in progress.");
            return;
        }

        isRecording = false;
        
        // One final record to capture the end state
        RecordCurrentFrame();

        if (keyframes.Count > 0)
        {
            SaveSceneFile();
        }
        
        FileLogger.LogSection("SCENE RECORDING STOPPED");
        FileLogger.Flush();
    }
    
    /// <summary>
    /// Records the current state of all tracked components
    /// </summary>
    private void RecordCurrentFrame()
    {
        float currentTime = Time.time - recordingStartTime;
        
        bool stateChanged = false;
        for (int i = 0; i < trackedComponents.Count; i++)
        {
            if (trackedComponents[i].IsActive() != trackedComponents[i].wasActive)
            {
                stateChanged = true;
                break;
            }
        }

        // Only add keyframe if there was a state change or if we're not just logging changes
        if (stateChanged || !logStateChanges)
        {
            SceneKeyframeFloat keyframe = new SceneKeyframeFloat(currentTime, trackedComponents.Count);
            for (int i = 0; i < trackedComponents.Count; i++)
            {
                var comp = trackedComponents[i];
                keyframe.componentStates[i] = comp.IsActive();
                comp.UpdateWasActive();
            }
            keyframes.Add(keyframe);
            keyframeCount = keyframes.Count;
        }
    }

    /// <summary>
    /// Finds all relevant components in the scene to track
    /// </summary>
    private void InitializeTrackedComponents()
    {
        trackedComponents.Clear();
        ushort currentId = 0;

        // Find all RecordCamera components
        RecordCamera[] cameras = FindObjectsOfType<RecordCamera>();
        foreach (var cam in cameras)
        {
            trackedComponents.Add(new TrackedComponent(cam, cam.fileName, cam.fileName + ".cam", ComponentType.Camera, currentId++));
        }

        // Find all Actor components
        Actor[] actors = FindObjectsOfType<Actor>();
        foreach (var actor in actors)
        {
            // Actors reference .act files (which contain transform and RAT file references)
            string actPath = actor.BaseFilename + ".act";
            trackedComponents.Add(new TrackedComponent(actor, actor.gameObject.name, actPath, ComponentType.Actor, currentId++));
            
            Debug.Log($"Scene will reference actor {actor.gameObject.name}: {actPath}");
        }

        // Find all Level components
        Level[] levels = FindObjectsOfType<Level>();
        foreach (var level in levels)
        {
            string levelName = Path.GetFileNameWithoutExtension(level.outputFileName);
            trackedComponents.Add(new TrackedComponent(level, levelName, level.outputFileName, ComponentType.Level, currentId++));
        }

        // Find all SDFShape components (NOT in particle systems)
        SDFShape[] allShapes = FindObjectsOfType<SDFShape>();
        var nonParticleShapes = allShapes.Where(s => s.GetComponent<SDFParticleRecorder>() == null).ToList();
        
        var shapesByName = nonParticleShapes
            .Where(s => s.emulatedResolution != SDFEmulatedResolution.None)
            .GroupBy(s => s.gameObject.name);

        foreach (var group in shapesByName)
        {
            SDFShape representative = group.First();
            
            // SDFShapes reference .act files in the root GeneratedData directory
            // The .act file is created by the ExportAllShapesToRATs function
            string actPath = $"{representative.gameObject.name}.act";
            
            Debug.Log($"Scene will reference shape {representative.gameObject.name}: {actPath}");
            
            trackedComponents.Add(new TrackedComponent(representative, representative.gameObject.name, actPath, ComponentType.Shape, currentId++));
        }

        // Find all SkyBox components
        SkyBox[] skyBoxes = FindObjectsOfType<SkyBox>();
        foreach (var skyBox in skyBoxes)
        {
            // SkyBox references .act files in the root GeneratedData directory
            // The .act file is created by the SkyBox.ExportToRAT function
            string actPath = $"{skyBox.gameObject.name}.act";
            
            Debug.Log($"Scene will reference skybox {skyBox.gameObject.name}: {actPath}");
            
            trackedComponents.Add(new TrackedComponent(skyBox, skyBox.gameObject.name, actPath, ComponentType.Shape, currentId++));
        }

        // Find all SDFParticleRecorder components
        SDFParticleRecorder[] particleRecorders = FindObjectsOfType<SDFParticleRecorder>();
        foreach (var recorder in particleRecorders)
        {
            // Particle systems reference their base filename .act files
            string actPath = $"{recorder.baseFilename}.act";
            trackedComponents.Add(new TrackedComponent(recorder, recorder.gameObject.name, actPath, ComponentType.Actor, currentId++));
            
            Debug.Log($"Scene will reference particle system {recorder.gameObject.name}: {actPath}");
        }
        
        // Sort by ID to ensure consistent order
        trackedComponents.Sort((a, b) => a.id.CompareTo(b.id));
        
    Debug.Log($"Initialized {trackedComponents.Count} tracked components");
    }

    /// <summary>
    /// Saves the recorded scene data to a binary file and prints detailed export information
    /// </summary>
    private void SaveSceneFile()
    {
        string path = Path.Combine(Application.dataPath, "..", "GeneratedData", sceneName + ".scn");
        Debug.Log("Attempting to save scene file to: " + path);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        try
        {
            using (FileStream fs = new FileStream(path, FileMode.Create))
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                // --- Write Header ---
                SceneHeader header = new SceneHeader
                {
                    magic = SCENE_FILE_MAGIC,      // 'SCNE'
                    version = SCENE_FILE_VERSION,    // Version 1
                    num_components = (uint)trackedComponents.Count,
                    num_keyframes = (uint)keyframes.Count,
                    framerate = recordingFPS,
                    reserved = new byte[32]
                };
                
                // Calculate offsets after header and component data
                uint componentDataSize = (uint)(Marshal.SizeOf(typeof(SceneComponentData)) * trackedComponents.Count);
                header.keyframes_offset = (uint)Marshal.SizeOf(typeof(SceneHeader)) + componentDataSize;

                WriteStructure(writer, header);

                // --- Write Component Info ---
                foreach (var comp in trackedComponents)
                {
                    SceneComponentData sc = new SceneComponentData
                    {
                        component_type = (byte)comp.type,
                        component_id = comp.id,
                        component_name = comp.name,
                        file_path = comp.filePath
                    };
                    WriteStructure(writer, sc);
                }

                // --- Write Keyframes ---
                foreach (var keyframe in keyframes)
                {
                    int numMasks = (trackedComponents.Count + 31) / 32;
                    uint[] activeMasks = new uint[numMasks];
                    for (int i = 0; i < trackedComponents.Count; i++)
                    {
                        if (keyframe.componentStates[i])
                        {
                            activeMasks[i / 32] |= (1u << (i % 32));
                        }
                    }

                    SceneKeyframeHeaderData sk = new SceneKeyframeHeaderData
                    {
                        timestamp = keyframe.timestamp,
                        active_mask_count = (uint)numMasks
                    };
                    WriteStructure(writer, sk);
                    
                    foreach(uint mask in activeMasks)
                    {
                        writer.Write(mask);
                    }
                }
            }
            
            Debug.Log($"Scene saved to {path}");
            
            // Print detailed export information
            PrintSceneFileDetails(path);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save scene file: {e.Message}");
        }
    }

    /// <summary>
    /// Prints comprehensive details about the exported .scn file to the console
    /// </summary>
    private void PrintSceneFileDetails(string filePath)
    {
        FileLogger.Initialize();
        FileLogger.LogSection("SCENE FILE EXPORT COMPLETE - DETAILED SUMMARY");
        
        FileInfo fileInfo = new FileInfo(filePath);
        float recordingDuration = keyframes.Count > 0 ? keyframes[keyframes.Count - 1].timestamp : 0f;

        // File Information
        FileLogger.LogSection("FILE INFORMATION");
        FileLogger.Log($"  File Path:              {filePath}");
        FileLogger.Log($"  File Size:              {fileInfo.Length:N0} bytes ({fileInfo.Length / 1024.0:F2} KB)");
        FileLogger.Log($"  File Name:              {fileInfo.Name}");
        
        // Header Information
        FileLogger.LogSection("HEADER STRUCTURE (64 bytes)");
        FileLogger.Log($"  Magic Number:           0x454E4353 (\"SCNE\")");
        FileLogger.Log($"  Version:                1");
        FileLogger.Log($"  Total Components:       {trackedComponents.Count}");
        FileLogger.Log($"  Total Keyframes:        {keyframes.Count}");
        FileLogger.Log($"  Recording Framerate:    {recordingFPS} FPS");
        FileLogger.Log($"  Keyframes Offset:       {Marshal.SizeOf(typeof(SceneHeader)) + (uint)(Marshal.SizeOf(typeof(SceneComponentData)) * trackedComponents.Count)} bytes");
        
        // Component Information
        FileLogger.LogSection($"TRACKED COMPONENTS ({trackedComponents.Count} components)");
        FileLogger.Log($"  Component Entry Size:   {Marshal.SizeOf(typeof(SceneComponentData))} bytes");
        FileLogger.Log($"  Total Component Data:   {trackedComponents.Count * Marshal.SizeOf(typeof(SceneComponentData)):N0} bytes");
        FileLogger.Log("");
        
        for (int i = 0; i < trackedComponents.Count; i++)
        {
            var comp = trackedComponents[i];
            string typeName = comp.type switch
            {
                ComponentType.Camera => "Camera",
                ComponentType.Actor => "Actor",
                ComponentType.Level => "Level",
                ComponentType.Shape => "SDF Shape",
                _ => "Unknown"
            };
            
            FileLogger.Log($"  [{i}] {typeName} \"{comp.GetName()}\"");
            FileLogger.Log($"      ID: {comp.id}");
            FileLogger.Log($"      File Path: {System.Text.Encoding.UTF8.GetString(comp.filePath).TrimEnd('\0')}");
        }
        
        // Keyframe Information
        FileLogger.LogSection($"KEYFRAME DATA ({keyframes.Count} keyframes)");
        
        int numMasks = (trackedComponents.Count + 31) / 32;
        uint keyframeDataSize = (uint)keyframes.Count * (8 + (uint)numMasks * 4);
        
        FileLogger.Log($"  Activation Mask Count:  {numMasks} uint32(s) per keyframe");
        FileLogger.Log($"  Keyframe Struct Size:   8 bytes (timestamp + mask_count)");
        FileLogger.Log($"  Mask Data Size:         {numMasks * 4} bytes per keyframe");
        FileLogger.Log($"  Total Keyframe Data:    {keyframeDataSize:N0} bytes");
        FileLogger.Log($"  Recording Duration:     {recordingDuration:F3} seconds");
        FileLogger.Log($"  Time Per Frame:         {(recordingDuration / keyframes.Count):F4} seconds");
        
        // Show first few keyframes
        FileLogger.Log("");
        FileLogger.Log("  First 5 Keyframes:");
        for (int i = 0; i < Mathf.Min(5, keyframes.Count); i++)
        {
            var kf = keyframes[i];
            
            string activeComponents = "";
            for (int c = 0; c < trackedComponents.Count; c++)
            {
                if (kf.componentStates[c])
                {
                    activeComponents += $"{c} ";
                }
            }
            
            FileLogger.Log($"    [{i}] Time: {kf.timestamp:F3}s | Active Components: [{activeComponents.Trim()}]");
        }
        
        if (keyframes.Count > 10)
        {
            FileLogger.Log($"    ... ({keyframes.Count - 10} more keyframes) ...");
        }
        
        // File Structure Layout
        FileLogger.LogSection("BINARY FILE LAYOUT");
        long headerSize = Marshal.SizeOf(typeof(SceneHeader));
        long componentSize = trackedComponents.Count * Marshal.SizeOf(typeof(SceneComponentData));
        long keyframesSize = keyframeDataSize;
        long totalCalculated = headerSize + componentSize + keyframesSize;
        
        FileLogger.Log($"  Offset 0x00:            Header ({headerSize} bytes)");
        FileLogger.Log($"  Offset 0x{headerSize:X2}:            Components ({componentSize:N0} bytes, {trackedComponents.Count} entries × {Marshal.SizeOf(typeof(SceneComponentData))} bytes each)");
        FileLogger.Log($"  Offset 0x{headerSize + componentSize:X2}:            Keyframes ({keyframesSize:N0} bytes, {keyframes.Count} entries)");
        FileLogger.Log($"  Total Calculated:       {totalCalculated:N0} bytes");
        FileLogger.Log($"  Actual File Size:       {fileInfo.Length:N0} bytes");
    FileLogger.Log($"  Match:                  {(totalCalculated == fileInfo.Length ? "<color=green>OK</color>" : "<color=red>NO</color>")}");
        
        // Memory Efficiency
        FileLogger.LogSection("COMPRESSION & EFFICIENCY");
        float bitsPerComponent = (keyframes.Count * numMasks * 32) / (float)trackedComponents.Count;
        float compressionRatio = 1.0f - (fileInfo.Length / (float)(keyframes.Count * trackedComponents.Count * 4));
        
        FileLogger.Log($"  Bits per Component per Frame: {bitsPerComponent:F1}");
        FileLogger.Log($"  Compression Ratio:      {(1.0f - compressionRatio) * 100:F1}% reduction from uncompressed");
        FileLogger.Log($"  Bytes per Keyframe:     {fileInfo.Length / (float)keyframes.Count:F1}");
        
        // Component References
        FileLogger.LogSection("REFERENCED ASSET FILES");
        HashSet<string> referencedFiles = new HashSet<string>();
        
        for (int i = 0; i < trackedComponents.Count; i++)
        {
            string filePath_component = System.Text.Encoding.UTF8.GetString(trackedComponents[i].filePath).TrimEnd('\0');
            if (!string.IsNullOrEmpty(filePath_component))
            {
                referencedFiles.Add(filePath_component);
            }
        }
        
        foreach (var file in referencedFiles.OrderBy(f => f))
        {
            FileLogger.Log($"  - {file}");
        }
        
        // Summary Statistics
        FileLogger.LogSection("SUMMARY STATISTICS");
        FileLogger.Log($"  Total Components:       {trackedComponents.Count}");
        FileLogger.Log($"  Total Keyframes:        {keyframes.Count}");
        FileLogger.Log($"  Recording Duration:     {recordingDuration:F2} seconds");
        FileLogger.Log($"  Framerate:              {recordingFPS} FPS");
        FileLogger.Log($"  File Size:              {fileInfo.Length / 1024.0:F2} KB");
        FileLogger.Log($"  Data Density:           {fileInfo.Length / (float)(keyframes.Count * trackedComponents.Count):F2} bytes per component-frame");
        
        FileLogger.LogSection("STATUS");
        FileLogger.Log("Ready for C Engine Integration!");
        
        FileLogger.Flush();
    }

    /// <summary>
    /// Helper to write a struct to a BinaryWriter
    /// </summary>
    private static void WriteStructure<T>(BinaryWriter writer, T structure) where T : struct
    {
        byte[] buffer = new byte[Marshal.SizeOf(typeof(T))];
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            Marshal.StructureToPtr(structure, handle.AddrOfPinnedObject(), false);
            writer.Write(buffer);
        }
        finally
        {
            handle.Free();
        }
    }
}