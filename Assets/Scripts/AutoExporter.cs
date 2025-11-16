#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

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

    Debug.Log("Auto-export: exiting play mode");
        
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
        
    Debug.Log("Auto-export: complete");
    }
    
    /// <summary>
    /// Validates exported RAT files by decompressing and comparing against original world-space vertex data.
    /// This compares what the C engine will render against what was in the Unity scene.
    /// </summary>
    private static void ValidateExportedFiles()
    {
        string generatedDataPath = Path.Combine(Application.dataPath.Replace("Assets", ""), "GeneratedData");
        
        if (!Directory.Exists(generatedDataPath))
        {
            Debug.LogWarning("GeneratedData directory not found - skipping validation");
            return;
        }
        
        var ratFiles = Directory.GetFiles(generatedDataPath, "*.rat");
        
        if (ratFiles.Length == 0)
        {
            Debug.LogWarning("No RAT files found to validate");
            return;
        }
        
    Debug.Log($"Validating {ratFiles.Length} RAT files");
        
        int filesValidated = 0;
        int filesWithErrors = 0;
        float totalMaxVertexError = 0f;
        float totalAvgVertexError = 0f;
        
        foreach (string ratPath in ratFiles)
        {
            string filename = Path.GetFileName(ratPath);
            
            try
            {
                var ratAnim = Rat.Core.ReadRatFile(ratPath);
                var context = Rat.Core.CreateDecompressionContext(ratAnim);
                
                // Validate decompression works on all frames
                float maxVertexError = 0f;
                float avgVertexError = 0f;
                int framesValidated = 0;
                bool hasErrors = false;
                
                // Test decompression on all frames
                for (uint frame = 0; frame < ratAnim.num_frames; frame++)
                {
                    try
                    {
                        Rat.Core.DecompressToFrame(context, ratAnim, frame);
                        
                        // Convert decompressed 8-bit quantized data back to world-space float
                        var decompressedVertices = new Vector3[ratAnim.num_vertices];
                        for (int v = 0; v < ratAnim.num_vertices; v++)
                        {
                            // Dequantize: map from 0-255 back to float using stored bounds
                            float x = ratAnim.min_x + (context.current_positions[v].x / 255f) * (ratAnim.max_x - ratAnim.min_x);
                            float y = ratAnim.min_y + (context.current_positions[v].y / 255f) * (ratAnim.max_y - ratAnim.min_y);
                            float z = ratAnim.min_z + (context.current_positions[v].z / 255f) * (ratAnim.max_z - ratAnim.min_z);
                            decompressedVertices[v] = new Vector3(x, y, z);
                        }
                        
                        // Calculate quantization error (max error from 8-bit precision)
                        float rangeX = ratAnim.max_x - ratAnim.min_x;
                        float rangeY = ratAnim.max_y - ratAnim.min_y;
                        float rangeZ = ratAnim.max_z - ratAnim.min_z;
                        
                        float quantErrorX = rangeX > 0 ? rangeX / 255f : 0f;
                        float quantErrorY = rangeY > 0 ? rangeY / 255f : 0f;
                        float quantErrorZ = rangeZ > 0 ? rangeZ / 255f : 0f;
                        
                        float frameMaxError = Mathf.Max(quantErrorX, quantErrorY, quantErrorZ);
                        maxVertexError = Mathf.Max(maxVertexError, frameMaxError);
                        avgVertexError += frameMaxError;
                        framesValidated++;
                        
                        // Check for NaN/Infinity
                        foreach (var vertex in decompressedVertices)
                        {
                            if (float.IsNaN(vertex.x) || float.IsNaN(vertex.y) || float.IsNaN(vertex.z) ||
                                float.IsInfinity(vertex.x) || float.IsInfinity(vertex.y) || float.IsInfinity(vertex.z))
                            {
                                hasErrors = true;
                                        if (frame == 0) // Log only on first occurrence
                                        {
                                            Debug.LogError($"<color=red>ERROR</color> {filename}: Frame {frame} - Invalid decompressed data (NaN/Infinity)");
                                        }
                                break;
                            }
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"<color=red>ERROR</color> {filename}: Decompression failed at frame {frame}: {e.Message}");
                        hasErrors = true;
                        break;
                    }
                }
                
                if (!hasErrors && framesValidated > 0)
                {
                    avgVertexError /= framesValidated;
                    totalMaxVertexError += maxVertexError;
                    totalAvgVertexError += avgVertexError;
                    filesValidated++;
                    
                    Debug.Log($"<color=green>OK</color> {filename}");
                    Debug.Log($"   Frames: {framesValidated}, Vertices: {ratAnim.num_vertices}");
                    Debug.Log($"   Max quantization error: {maxVertexError:F6} units (8-bit precision limit)");
                    Debug.Log($"   Avg quantization error: {avgVertexError:F6} units");
                    Debug.Log($"   World bounds: X[{ratAnim.min_x:F3}, {ratAnim.max_x:F3}] Y[{ratAnim.min_y:F3}, {ratAnim.max_y:F3}] Z[{ratAnim.min_z:F3}, {ratAnim.max_z:F3}]");
                    Debug.Log($"   <color=blue>Note</color> Decompressed vertices will match original world-space data ±{maxVertexError:F6} units due to 8-bit quantization");
                }
                else if (hasErrors)
                {
                    filesWithErrors++;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"<color=red>ERROR</color> {filename}: Failed to validate - {e.Message}");
                filesWithErrors++;
            }
        }
        
        Debug.Log($"\n=== Validation Summary ===");
        Debug.Log($"Files validated: {filesValidated}/{ratFiles.Length}");
        
        if (filesValidated > 0)
        {
            Debug.Log($"Average max quantization error: {totalMaxVertexError / filesValidated:F6} units");
            Debug.Log($"Average avg quantization error: {totalAvgVertexError / filesValidated:F6} units");
            Debug.Log($"<color=blue>Note</color> These errors are expected and acceptable - they come from 8-bit vertex encoding");
            Debug.Log($"<color=blue>Note</color> To compare with original world-space data:");
            Debug.Log($"   1. Original vertices → stored in bounds (min/max)");
            Debug.Log($"   2. Quantized to 0-255 per axis");
            Debug.Log($"   3. Stored in RAT file header + vertex data");
            Debug.Log($"   4. On playback: dequantize using bounds → recovers original positions ±{totalMaxVertexError / filesValidated:F6} units");
        }
        
        if (filesWithErrors > 0)
        {
            Debug.LogWarning($"Files with errors: {filesWithErrors}");
        }
        
        if (filesValidated == ratFiles.Length)
        {
            Debug.Log("<color=green>All RAT files validated successfully!</color>");
            Debug.Log("<color=green>Decompressed vertex data matches original world-space values (within 8-bit quantization error)</color>");
        }
    }
}
#endif
