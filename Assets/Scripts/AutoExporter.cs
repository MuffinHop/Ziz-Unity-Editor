#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO; // Add this for Path and Directory

/// <summary>
/// Automatically exports all animation data when exiting play mode.
/// No manual buttons or menu items - just works.
/// </summary>
[InitializeOnLoad]
public static class AutoExporter
{
    static AutoExporter()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingPlayMode) return;

        Debug.Log("=== Auto-Export: Exiting Play Mode ===");
        
        // 1. RatRecorders - handled automatically
        // 2. SDFParticleRecorders - handled automatically
        // 3. Actors - handled automatically
        
        // 4. Export all Shapes (single-frame static export)
        var shapes = Object.FindObjectsOfType<Shape>();
        foreach (var shape in shapes)
        {
            // mesh is now public, so this works
            if (shape.mesh != null && shape.mesh.vertexCount > 0)
            {
                string filename = shape.name;
                Actor.ExportMeshToRatAct(filename, shape.mesh, shape.transform, shape.color);
            }
        }
        
        // NEW: Validate all exported files
        ValidateExportedFiles();
        
        Debug.Log("=== Auto-Export: Complete ===");
    }
    
    private static void ValidateExportedFiles()
    {
        string generatedDataPath = Path.Combine(Application.dataPath.Replace("Assets", ""), "GeneratedData");
        
        if (!Directory.Exists(generatedDataPath))
        {
            Debug.LogWarning("GeneratedData directory not found for validation");
            return;
        }
        
        var actFiles = Directory.GetFiles(generatedDataPath, "*.act");
        var ratFiles = Directory.GetFiles(generatedDataPath, "*.rat");
        
        Debug.Log($"Validating {actFiles.Length} ACT and {ratFiles.Length} RAT files...");
        
        int totalErrors = 0;
        int totalWarnings = 0;
        
        // Quick validation of recent files
        foreach (string actFile in actFiles)
        {
            var result = AnimationValidator.ValidateActFile(actFile);
            totalErrors += result.errors.Count;
            totalWarnings += result.warnings.Count;
        }
        
        foreach (string ratFile in ratFiles)
        {
            var result = AnimationValidator.ValidateRatFile(ratFile);
            totalErrors += result.errors.Count;
            totalWarnings += result.warnings.Count;
        }
        
        if (totalErrors == 0 && totalWarnings == 0)
        {
            Debug.Log("✅ All exported files validated successfully!");
        }
        else
        {
            Debug.Log($"❌ Export validation: {totalErrors} errors, {totalWarnings} warnings");
        }
    }
}
#endif
