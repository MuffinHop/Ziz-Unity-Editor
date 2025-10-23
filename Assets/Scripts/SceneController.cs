using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System;
using System.Linq;

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
    public enum ComponentType
    {
        Camera = 0,
        Actor = 1,
        Level = 2,
        Shape = 3 // New component type for SDF Shapes
    }

    [System.Serializable]
    public class TrackedComponent
    {
        public ComponentType type;
        public ushort id;
        public byte[] name = new byte[64];
        public byte[] filePath = new byte[64];
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
            this.filePath = SceneController.PadAndTruncate(path, 64);
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
    public bool logStateChanges = true; // If true, only records when a component's state changes

    [Header("Debug Info")]
    [SerializeField] private bool isRecording = false;
    [SerializeField] private float recordingStartTime = 0f;
    [SerializeField] private int keyframeCount = 0;

    private List<TrackedComponent> trackedComponents = new List<TrackedComponent>();
    private List<SceneKeyframeFloat> keyframes = new List<SceneKeyframeFloat>();
    private float lastRecordTime = 0f;

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
        if (isRecording)
        {
            StartRecording();
        }
    }

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

    [ContextMenu("Start Recording")]
    public void StartRecording()
    {
        if (isRecording)
        {
            Debug.LogWarning("Scene recording is already in progress.");
            return;
        }

        Debug.Log("=== SCENE RECORDING STARTED ===");
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
            Debug.LogWarning("Scene recording is not in progress.");
            return;
        }

        isRecording = false;
        
        // One final record to capture the end state
        RecordCurrentFrame();

        if (keyframes.Count > 0)
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
            trackedComponents.Add(new TrackedComponent(actor, actor.gameObject.name, actor.AnimationData.ratFilePath, ComponentType.Actor, currentId++));
        }

        // Find all Level components
        Level[] levels = FindObjectsOfType<Level>();
        foreach (var level in levels)
        {
            string levelName = Path.GetFileNameWithoutExtension(level.outputFileName);
            trackedComponents.Add(new TrackedComponent(level, levelName, level.outputFileName, ComponentType.Level, currentId++));
        }

        // New: Find all SDFShape components
        SDFShape[] shapes = FindObjectsOfType<SDFShape>();
        var shapesByTexture = shapes
            .Where(s => s.emulatedResolution != SDFEmulatedResolution.None)
            .GroupBy(s => {
                int w = 0, h = 0;
                switch (s.emulatedResolution)
                {
                    case SDFEmulatedResolution.Tex512x512: w = h = 512; break;
                    case SDFEmulatedResolution.Tex256x256: w = h = 256; break;
                    case SDFEmulatedResolution.Tex128x64: w = 128; h = 64; break;
                }
                return s.BuildOutputFilename(w, h);
            });

        foreach (var group in shapesByTexture)
        {
            // All shapes in the group share the same texture and rat file.
            // We only need to add one component to the scene file to represent the whole group.
            SDFShape representative = group.First();
            string texturePath = group.Key; // This is the .png path
            string ratFileName = Path.GetFileNameWithoutExtension(texturePath) + ".rat";
            
            // We track the texture file; the C engine will derive the .rat name from it.
            trackedComponents.Add(new TrackedComponent(representative, representative.gameObject.name, texturePath, ComponentType.Shape, currentId++));
        }
        
        // Sort by ID to ensure consistent order
        trackedComponents.Sort((a, b) => a.id.CompareTo(b.id));
    }

    /// <summary>
    /// Saves the recorded scene data to a binary file
    /// </summary>
    private void SaveSceneFile()
    {
        string path = Path.Combine(Application.dataPath, "..", "GeneratedData", sceneName + ".scn");
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        try
        {
            using (FileStream fs = new FileStream(path, FileMode.Create))
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                // --- Write Header ---
                SceneHeader header = new SceneHeader
                {
                    magic = 0x454E4353, // "SCNE"
                    version = 1,
                    num_components = (uint)trackedComponents.Count,
                    num_keyframes = (uint)keyframes.Count,
                    framerate = recordingFPS,
                    reserved = new byte[32]
                };
                
                // Calculate offsets after header and component data
                uint componentDataSize = (uint)(Marshal.SizeOf(typeof(SceneComponent)) * trackedComponents.Count);
                header.keyframes_offset = (uint)Marshal.SizeOf(typeof(SceneHeader)) + componentDataSize;

                WriteStructure(writer, header);

                // --- Write Component Info ---
                foreach (var comp in trackedComponents)
                {
                    SceneComponent sc = new SceneComponent
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

                    SceneKeyframe sk = new SceneKeyframe
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
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save scene file: {e.Message}");
        }
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
