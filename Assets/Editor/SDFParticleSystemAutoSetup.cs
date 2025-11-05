using UnityEngine;
using UnityEditor;

/// <summary>
/// Automatically adds SDFParticleRecorder to any ParticleSystem added to the scene.
/// This ensures all particle systems are ready for SDF export without manual setup.
/// </summary>
[InitializeOnLoad]
public class SDFParticleSystemAutoSetup
{
    static SDFParticleSystemAutoSetup()
    {
        // Subscribe to hierarchy changes
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        
        // Process existing particle systems when editor loads
        EditorApplication.delayCall += () => ProcessAllParticleSystems();
    }

    /// <summary>
    /// Menu item to manually add recorders to all particle systems
    /// </summary>
    [MenuItem("Tools/SDF Particles/Add Recorders to All Particle Systems")]
    public static void AddRecordersToAllParticleSystems()
    {
        int count = ProcessAllParticleSystems(true);
        EditorUtility.DisplayDialog(
            "SDF Particle Recorders", 
            $"Added SDFParticleRecorder to {count} ParticleSystem(s).", 
            "OK"
        );
    }

    /// <summary>
    /// Menu item to remove all recorders from particle systems
    /// </summary>
    [MenuItem("Tools/SDF Particles/Remove All Recorders")]
    public static void RemoveAllRecorders()
    {
        SDFParticleRecorder[] recorders = Object.FindObjectsOfType<SDFParticleRecorder>();
        int count = recorders.Length;
        
        foreach (SDFParticleRecorder recorder in recorders)
        {
            Object.DestroyImmediate(recorder);
        }
        
        EditorUtility.DisplayDialog(
            "SDF Particle Recorders", 
            $"Removed {count} SDFParticleRecorder component(s).", 
            "OK"
        );
    }

    private static void OnHierarchyChanged()
    {
        ProcessAllParticleSystems();
    }

    private static int ProcessAllParticleSystems(bool force = false)
    {
        // Find all ParticleSystem components in the scene
        ParticleSystem[] particleSystems = Object.FindObjectsOfType<ParticleSystem>();
        int addedCount = 0;
        
        foreach (ParticleSystem ps in particleSystems)
        {
            // Skip if already has recorder
            if (ps.GetComponent<SDFParticleRecorder>() != null)
                continue;
            
            // Skip prefabs in project (not in scene)
            if (PrefabUtility.IsPartOfPrefabAsset(ps))
                continue;
            
            // Add recorder component
            SDFParticleRecorder recorder = ps.gameObject.AddComponent<SDFParticleRecorder>();
            
            // Auto-configure with sensible defaults
            recorder.targetParticleSystem = ps;
            recorder.baseFilename = SanitizeFilename(ps.gameObject.name);
            recorder.particleShapeType = SDFShapeType.Circle; // Most common
            recorder.shapeResolution = SDFEmulatedResolution.Tex256x256; // Good balance
            recorder.autoStartRecording = true;
            recorder.autoExportOnPlayModeExit = true;
            
            // Mark as dirty so Unity saves the change
            EditorUtility.SetDirty(ps.gameObject);
            
            addedCount++;
            
            if (force)
            {
                Debug.Log($"SDFParticleSystemAutoSetup: Added SDFParticleRecorder to '{ps.gameObject.name}'");
            }
        }
        
        return addedCount;
    }
    
    private static string SanitizeFilename(string name)
    {
        // Remove invalid filename characters
        string sanitized = System.Text.RegularExpressions.Regex.Replace(name, @"[<>:""/\\|?*]", "_");
        return string.IsNullOrEmpty(sanitized) ? "particle_system" : sanitized;
    }
}
