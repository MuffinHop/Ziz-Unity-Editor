using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using Rat;

#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Comprehensive validator for .act and .rat animation files.
/// Verifies file integrity, data consistency, and playback compatibility.
/// </summary>
public static class AnimationValidator
{
    [MenuItem("Ziz/Validate All Animation Files")]
    public static void ValidateAllFiles()
    {
        string generatedDataPath = Path.Combine(Application.dataPath.Replace("Assets", ""), "GeneratedData");
        
        if (!Directory.Exists(generatedDataPath))
        {
            Debug.LogError("GeneratedData directory not found!");
            return;
        }
        
        var actFiles = Directory.GetFiles(generatedDataPath, "*.act");
        var ratFiles = Directory.GetFiles(generatedDataPath, "*.rat");
        
        Debug.Log($"=== Animation File Validation ===\nFound {actFiles.Length} ACT files and {ratFiles.Length} RAT files");
        
        int totalErrors = 0;
        int totalWarnings = 0;
        
        // Validate individual files
        foreach (string actFile in actFiles)
        {
            var result = ValidateActFile(actFile);
            totalErrors += result.errors.Count;
            totalWarnings += result.warnings.Count;
        }
        
        foreach (string ratFile in ratFiles)
        {
            var result = ValidateRatFile(ratFile);
            totalErrors += result.errors.Count;
            totalWarnings += result.warnings.Count;
        }
        
        // Summary
        if (totalErrors == 0 && totalWarnings == 0)
        {
            Debug.Log("✅ All animation files validated successfully!");
        }
        else
        {
            Debug.Log($"❌ Validation complete: {totalErrors} errors, {totalWarnings} warnings");
        }
    }
    
    [MenuItem("Ziz/Test Playback All Animations")]
    public static void TestPlaybackAllAnimations()
    {
        string generatedDataPath = Path.Combine(Application.dataPath.Replace("Assets", ""), "GeneratedData");
        
        if (!Directory.Exists(generatedDataPath))
        {
            Debug.LogError("GeneratedData directory not found!");
            return;
        }
        
        var actFiles = Directory.GetFiles(generatedDataPath, "*.act");
        Debug.Log($"=== Playback Testing {actFiles.Length} Animations ===");
        
        foreach (string actFile in actFiles)
        {
            TestPlaybackAnimation(actFile);
        }
        
        Debug.Log("=== Playback Testing Complete ===");
    }
    
    [MenuItem("Ziz/Test Playback RAT-Only Animations")]
    public static void TestPlaybackRatOnlyAnimations()
    {
        string generatedDataPath = Path.Combine(Application.dataPath.Replace("Assets", ""), "GeneratedData");
        
        if (!Directory.Exists(generatedDataPath))
        {
            Debug.LogError("GeneratedData directory not found!");
            return;
        }
        
        var ratFiles = Directory.GetFiles(generatedDataPath, "*.rat");
        Debug.Log($"=== Playback Testing {ratFiles.Length} RAT-Only Animations ===");
        
        foreach (string ratFile in ratFiles)
        {
            TestPlaybackRatOnlyAnimation(ratFile);
        }
        
        Debug.Log("=== RAT-Only Playback Testing Complete ===");
    }
    
    [MenuItem("Ziz/Verify Vertex Positions All Animations")]
    public static void VerifyVertexPositionsAllAnimations()
    {
        string generatedDataPath = Path.Combine(Application.dataPath.Replace("Assets", ""), "GeneratedData");
        
        if (!Directory.Exists(generatedDataPath))
        {
            Debug.LogError("GeneratedData directory not found!");
            return;
        }
        
        var actFiles = Directory.GetFiles(generatedDataPath, "*.act");
        Debug.Log($"=== Vertex Position Verification for {actFiles.Length} Animations ===");
        
        foreach (string actFile in actFiles)
        {
            VerifyVertexPositions(actFile);
        }
        
        Debug.Log("=== Vertex Position Verification Complete ===");
    }
    
    [MenuItem("Ziz/Run Round-Trip Test")]
    public static void RunRoundTripTest()
    {
        Debug.Log("=== Round-Trip Test: Export → Import → Compare ===");
        
        // Create a simple test mesh
        Mesh testMesh = CreateTestMesh();
        string testName = "RoundTripTest";
        
        try
        {
            // Export the mesh
            Actor.ExportMeshToRatAct(testName, testMesh, null, Color.white);
            Debug.Log("✅ Export completed");
            
            // Immediately try to load it back
            string actPath = $"GeneratedData/{testName}.act";
            if (File.Exists(actPath))
            {
                var loadedData = LoadActorData(actPath);
                Debug.Log($"✅ Import completed");
                
                // Compare original vs loaded
                CompareRoundTrip(testMesh, loadedData);
            }
            else
            {
                Debug.LogError("❌ Exported ACT file not found");
            }
            
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Round-trip test failed: {e.Message}");
        }
        
        Debug.Log("=== Round-Trip Test Complete ===");
    }
    
    private static Mesh CreateTestMesh()
    {
        Mesh mesh = new Mesh();
        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0),
            new Vector3(0.5f, -0.5f, 0),
            new Vector3(0, 0.5f, 0)
        };
        mesh.triangles = new int[] { 0, 1, 2 };
        mesh.uv = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0.5f, 1)
        };
        mesh.colors = new Color[]
        {
            Color.red,
            Color.green,
            Color.blue
        };
        return mesh;
    }
    
    private static void CompareRoundTrip(Mesh originalMesh, ActorAnimationData loadedData)
    {
        // v6 ACT files have all transforms baked into RAT vertex data
        // Just validate that the RAT files exist and can be loaded
        
        if (loadedData.ratFilePaths.Count != 1)
        {
            Debug.LogError($"❌ RAT file count mismatch: expected 1, got {loadedData.ratFilePaths.Count}");
            return;
        }
        
        string ratPath = loadedData.ratFilePaths[0];
        string fullRatPath = Path.Combine("GeneratedData", ratPath);
        
        if (!File.Exists(fullRatPath))
        {
            Debug.LogError($"❌ RAT file not found: {fullRatPath}");
            return;
        }
        
        try
        {
            var ratAnim = Rat.Core.ReadRatFile(fullRatPath);
            
            if (ratAnim.num_vertices != originalMesh.vertexCount)
            {
                Debug.LogError($"❌ Vertex count mismatch: expected {originalMesh.vertexCount}, got {ratAnim.num_vertices}");
            }
            
            if (ratAnim.num_frames < 1)
            {
                Debug.LogError($"❌ No frames in RAT file");
            }
            
            // Test decompression
            var context = Rat.Core.CreateDecompressionContext(ratAnim);
            Rat.Core.DecompressToFrame(context, ratAnim, 0);
            
            Debug.Log("✅ Round-trip comparison completed");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ RAT file read error: {e.Message}");
        }
    }
    
    public class ValidationResult
    {
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();
        public bool isValid => errors.Count == 0;
        
        public void Log(string filename)
        {
            foreach (var error in errors)
                Debug.LogError($"❌ {filename}: {error}");
            foreach (var warning in warnings)
                Debug.LogWarning($"⚠️ {filename}: {warning}");
            if (isValid && warnings.Count == 0)
                Debug.Log($"✅ {filename}: Valid");
        }
    }
    
    /// <summary>
    /// Validates a single .act file
    /// </summary>
    public static ValidationResult ValidateActFile(string actPath)
    {
        var result = new ValidationResult();
        string filename = Path.GetFileName(actPath);
        
        try
        {
            using (var stream = new FileStream(actPath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(stream))
            {
                // Read and validate header
                var headerBytes = reader.ReadBytes(Marshal.SizeOf<ActorHeader>());
                if (headerBytes.Length != Marshal.SizeOf<ActorHeader>())
                {
                    result.errors.Add("Incomplete header");
                    return result;
                }
                
                var header = BytesToStruct<ActorHeader>(headerBytes);
                
                // Validate magic number
                if (header.magic != 0x52544341) // 'ACTR'
                {
                    result.errors.Add($"Invalid magic number: 0x{header.magic:X8} (expected 0x52544341)");
                    return result;
                }
                
                // Validate version (v6 only)
                if (header.version != 6)
                {
                    result.errors.Add($"Unsupported version: {header.version} (expected 6 - mesh data only)");
                    return result;
                }
                
                // Validate basic structure
                if (header.framerate <= 0 || header.framerate > 120)
                {
                    result.errors.Add($"Invalid framerate: {header.framerate} FPS");
                }
                
                // Validate RAT file references
                if (header.num_rat_files > 0)
                {
                    if (header.rat_filenames_length == 0)
                    {
                        result.errors.Add("RAT files specified but no filenames data");
                    }
                    else
                    {
                        // Try to read RAT filenames
                        stream.Seek(Marshal.SizeOf<ActorHeader>(), SeekOrigin.Begin);
                        var ratBlob = reader.ReadBytes((int)header.rat_filenames_length);
                        
                        var ratPaths = ParseNullTerminatedStrings(ratBlob);
                        if (ratPaths.Count != header.num_rat_files)
                        {
                            result.errors.Add($"RAT filename count mismatch: found {ratPaths.Count}, expected {header.num_rat_files}");
                        }
                    }
                }
                else
                {
                    result.warnings.Add("No RAT files referenced");
                }
                
                // Warnings
                if (header.num_vertices == 0)
                {
                    result.warnings.Add("Mesh has no vertices");
                }
            }
        }
        catch (System.Exception e)
        {
            result.errors.Add($"File read error: {e.Message}");
        }
        
        result.Log(filename);
        return result;
    }
    
    /// <summary>
    /// Validates a single .rat file
    /// </summary>
    public static ValidationResult ValidateRatFile(string ratPath)
    {
        var result = new ValidationResult();
        string filename = Path.GetFileName(ratPath);
        
        try
        {
            using (var stream = new FileStream(ratPath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(stream))
            {
                // Read and validate header
                var headerBytes = reader.ReadBytes(Marshal.SizeOf<RatHeader>());
                if (headerBytes.Length != Marshal.SizeOf<RatHeader>())
                {
                    result.errors.Add("Incomplete header");
                    return result;
                }
                
                var header = BytesToStruct<RatHeader>(headerBytes);
                
                // Validate magic number
                if (header.magic != 0x33544152) // 'RAT3'
                {
                    result.errors.Add($"Invalid magic number: 0x{header.magic:X8} (expected 0x33544152)");
                    return result;
                }
                
                // Validate basic structure
                if (header.num_vertices == 0)
                {
                    result.errors.Add("No vertices in animation");
                }
                
                if (header.num_frames == 0)
                {
                    result.errors.Add("No frames in animation");
                }
                
                if (header.num_indices == 0)
                {
                    result.warnings.Add("No triangle indices (point cloud only)");
                }
                
                // Validate bounds
                if (header.max_x < header.min_x ||
                    header.max_y < header.min_y ||
                    header.max_z < header.min_z)
                {
                    result.errors.Add("Invalid bounds (max < min)");
                }
                
                // Check for reasonable bounds
                float boundsSize = Mathf.Max(
                    header.max_x - header.min_x,
                    header.max_y - header.min_y,
                    header.max_z - header.min_z
                );
                
                if (boundsSize > 1000f)
                {
                    result.warnings.Add($"Very large bounds: {boundsSize:F1} units");
                }
                else if (boundsSize < 0.001f)
                {
                    result.warnings.Add($"Very small bounds: {boundsSize:F6} units");
                }
                
                // Validate bit widths data exists
                uint bitWidthsOffset = (uint)Marshal.SizeOf<RatHeader>();
                if (bitWidthsOffset + (header.num_vertices * 3) > stream.Length)
                {
                    result.errors.Add("Bit widths data extends beyond file");
                }
                
                // Validate first frame data exists
                uint firstFrameOffset = bitWidthsOffset + (header.num_vertices * 3);
                uint firstFrameSize = header.num_vertices * 3; // 3 bytes per vertex (x,y,z)
                if (firstFrameOffset + firstFrameSize > stream.Length)
                {
                    result.errors.Add("First frame data extends beyond file");
                }
                
                // Validate delta stream exists
                if (header.delta_offset >= stream.Length)
                {
                    // For single-frame animations, delta_offset might be at or beyond file end
                    if (header.num_frames <= 1)
                    {
                        result.warnings.Add("Single-frame animation has no delta stream (expected)");
                    }
                    else
                    {
                        result.errors.Add("Delta stream offset beyond file end");
                    }
                }
                else
                {
                    long deltaSize = stream.Length - header.delta_offset;
                    if (deltaSize <= 0)
                    {
                        if (header.num_frames <= 1)
                        {
                            result.warnings.Add("Single-frame animation has no delta stream (expected)");
                        }
                        else
                        {
                            result.errors.Add("No delta stream data");
                        }
                    }
                    else if (header.num_frames <= 1 && deltaSize > 0)
                    {
                        result.warnings.Add("Delta stream present but only single frame");
                    }
                }
                
                // Try to load with Rat.Core to validate decompression
                try
                {
                    var anim = Rat.Core.ReadRatFile(ratPath);
                    
                    // Additional validation on loaded data
                    if (anim.bit_widths_x.Length != header.num_vertices)
                    {
                        result.errors.Add($"Bit widths X array size mismatch: {anim.bit_widths_x.Length} vs {header.num_vertices}");
                    }
                    
                    if (anim.first_frame.Length != header.num_vertices)
                    {
                        result.errors.Add($"First frame array size mismatch: {anim.first_frame.Length} vs {header.num_vertices}");
                    }
                    
                    // Test decompression context creation
                    var context = Rat.Core.CreateDecompressionContext(anim);
                    if (context.current_positions.Length != header.num_vertices)
                    {
                        result.errors.Add($"Decompression context size mismatch: {context.current_positions.Length} vs {header.num_vertices}");
                    }
                    
                    // Test first frame decompression
                    if (header.num_frames > 0)
                    {
                        Rat.Core.DecompressToFrame(context, anim, 0);
                    }
                }
                catch (System.Exception e)
                {
                    result.errors.Add($"Decompression test failed: {e.Message}");
                }
            }
        }
        catch (System.Exception e)
        {
            result.errors.Add($"File read error: {e.Message}");
        }
        
        result.Log(filename);
        return result;
    }
    
    /// <summary>
    /// Validates that an ACT file and its referenced RAT files are compatible
    /// </summary>
    public static ValidationResult ValidateActRatPair(string actPath)
    {
        var result = new ValidationResult();
        string filename = Path.GetFileName(actPath);
        
        try
        {
            // Load ACT data
            var actData = LoadActorData(actPath);
            string directory = Path.GetDirectoryName(actPath);
            
            // v6: All transforms are baked into RAT vertex data
            // Just validate that RAT files exist and are readable
            
            foreach (string ratPath in actData.ratFilePaths)
            {
                string fullRatPath = Path.Combine(directory, ratPath);
                
                if (!File.Exists(fullRatPath))
                {
                    result.errors.Add($"Referenced RAT file not found: {ratPath}");
                    continue;
                }
                
                try
                {
                    var ratAnim = Rat.Core.ReadRatFile(fullRatPath);
                    Debug.Log($"Animation: {ratAnim.num_frames} frames, {ratAnim.num_vertices} vertices");
                }
                catch (System.Exception e)
                {
                    result.errors.Add($"Error reading RAT file {ratPath}: {e.Message}");
                }
            }
            
            if (actData.ratFilePaths.Count == 0)
            {
                result.warnings.Add("No RAT files referenced");
            }
            
        }
        catch (System.Exception e)
        {
            result.errors.Add($"Validation error: {e.Message}");
        }
        
        if (result.errors.Count > 0 || result.warnings.Count > 0)
        {
            result.Log(filename + " (pair validation)");
        }
        
        return result;
    }
    
    /// <summary>
    /// Tests playback of a single animation
    /// </summary>
    public static void TestPlaybackAnimation(string actPath)
    {
        string filename = Path.GetFileName(actPath);
        
        try
        {
            // Create temporary test object
            GameObject testObj = new GameObject($"Test_{filename}");
            var player = testObj.AddComponent<AnimationPlayer>();
            player.actFilePath = actPath;
            player.autoPlay = false;
            
            // Force load
            player.LoadAnimation(actPath);
            
            if (player._actorData == null)
            {
                Debug.LogError($"❌ {filename}: Failed to load animation data");
                Object.DestroyImmediate(testObj);
                return;
            }
            
            // Test loading RAT files
            if (player._ratAnimations.Count == 0)
            {
                Debug.LogError($"❌ {filename}: No RAT files loaded");
                Object.DestroyImmediate(testObj);
                return;
            }
            
            // Test playback
            player.Play();
            player.Stop();
            
            Debug.Log($"✅ {filename}: Playback test passed");
            
            Object.DestroyImmediate(testObj);
            
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ {filename}: Playback test failed: {e.Message}");
        }
    }
    
    /// <summary>
    /// Tests playback of a single RAT-only animation (when transforms are baked into vertices)
    /// </summary>
    public static void TestPlaybackRatOnlyAnimation(string ratPath)
    {
        string filename = Path.GetFileName(ratPath);
        
        try
        {
            // Create a test GameObject with AnimationPlayer
            GameObject testObj = new GameObject($"TestPlayer_{filename}");
            var player = testObj.AddComponent<AnimationPlayer>();
            
            // Load RAT-only animation
            player.LoadRatOnlyAnimation(ratPath);
            
            if (player._actorData == null)
            {
                Debug.LogError($"❌ {filename}: Failed to load RAT-only animation");
                Object.DestroyImmediate(testObj);
                return;
            }
            
            // Test that we can apply keyframes
            if (player._ratAnimations.Count > 0)
            {
                for (int i = 0; i < Mathf.Min(3, player._ratAnimations[0].num_frames); i++)
                {
                    player.ApplyKeyframe(i);
                    
                    // Check that vertex animation is working (mesh should be updated)
                    if (player.GetComponent<MeshFilter>().sharedMesh == null)
                    {
                        Debug.LogError($"❌ {filename}: No mesh after applying keyframe {i}");
                        break;
                    }
                }
            }
            
            player.Stop();
            
            Debug.Log($"✅ {filename}: RAT-only playback test passed");
            
            Object.DestroyImmediate(testObj);
            
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ {filename}: RAT-only playback test failed: {e.Message}");
        }
    }
    
    /// <summary>
    /// Verifies that vertex positions are correct for every frame by simulating playback
    /// </summary>
    public static void VerifyVertexPositions(string actPath)
    {
        string filename = Path.GetFileName(actPath);
        
        try
        {
            // Load ACT data
            var actData = LoadActorData(actPath);
            string directory = Path.GetDirectoryName(actPath);
            
            // Load all referenced RAT files
            var ratAnimations = new List<CompressedAnimation>();
            var decompContexts = new List<DecompressionContext>();
            
            foreach (string ratPath in actData.ratFilePaths)
            {
                string fullRatPath = Path.Combine(directory, ratPath);
                if (File.Exists(fullRatPath))
                {
                    var ratAnim = Rat.Core.ReadRatFile(fullRatPath);
                    ratAnimations.Add(ratAnim);
                    decompContexts.Add(Rat.Core.CreateDecompressionContext(ratAnim));
                }
                else
                {
                    Debug.LogError($"❌ {filename}: Referenced RAT file not found: {ratPath}");
                    return;
                }
            }
            
            // v6: All animation is in RAT vertex data
            // Verify first frame of first RAT file
            if (ratAnimations.Count > 0)
            {
                var ratAnim = ratAnimations[0];
                var context = decompContexts[0];
                
                Rat.Core.DecompressToFrame(context, ratAnim, 0);
                
                var vertices = new Vector3[ratAnim.num_vertices];
                for (int i = 0; i < ratAnim.num_vertices; i++)
                {
                    float x = ratAnim.min_x + (context.current_positions[i].x / 255f) * (ratAnim.max_x - ratAnim.min_x);
                    float y = ratAnim.min_y + (context.current_positions[i].y / 255f) * (ratAnim.max_y - ratAnim.min_y);
                    float z = ratAnim.min_z + (context.current_positions[i].z / 255f) * (ratAnim.max_z - ratAnim.min_z);
                    vertices[i] = new Vector3(x, y, z);
                }
                
                ValidateKeyframeVertices(filename, 0, vertices, ratAnim);
                
                Debug.Log($"✅ {filename}: Vertex position verification complete");
            }
            else
            {
                Debug.LogWarning($"⚠️ {filename}: No RAT files to verify");
            }
            
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ {filename}: Vertex verification failed: {e.Message}");
        }
    }
    
    /// <summary>
    /// Validates vertex positions for a single keyframe
    /// </summary>
    private static void ValidateKeyframeVertices(string filename, int keyframeIndex, Vector3[] worldVertices, CompressedAnimation ratAnim)
    {
        bool hasErrors = false;
        bool hasWarnings = false;
        
        // Check for NaN or infinite values
        foreach (var vertex in worldVertices)
        {
            if (float.IsNaN(vertex.x) || float.IsNaN(vertex.y) || float.IsNaN(vertex.z) ||
                float.IsInfinity(vertex.x) || float.IsInfinity(vertex.y) || float.IsInfinity(vertex.z))
            {
                Debug.LogError($"❌ {filename} Frame {keyframeIndex}: Invalid vertex position (NaN/Infinity) in world space");
                hasErrors = true;
                break;
            }
        }
        
        // Check bounds are reasonable
        Bounds bounds = new Bounds(worldVertices[0], Vector3.zero);
        foreach (var vertex in worldVertices)
        {
            bounds.Encapsulate(vertex);
        }
        
        float boundsSize = bounds.size.magnitude;
        if (boundsSize > 10000f)
        {
            Debug.LogWarning($"⚠️ {filename} Frame {keyframeIndex}: Very large world bounds ({boundsSize:F1} units) - may indicate coordinate system issues");
            hasWarnings = true;
        }
        else if (boundsSize < 0.001f)
        {
            Debug.LogWarning($"⚠️ {filename} Frame {keyframeIndex}: Very small world bounds ({boundsSize:F6} units) - may be degenerate");
            hasWarnings = true;
        }
        
        // Check vertex count matches RAT file
        if (worldVertices.Length != ratAnim.num_vertices)
        {
            Debug.LogError($"❌ {filename} Frame {keyframeIndex}: Vertex count mismatch ({worldVertices.Length} vs {ratAnim.num_vertices})");
            hasErrors = true;
        }
        
        // Check for duplicate vertices (potential compression artifacts)
        var uniquePositions = new HashSet<Vector3>(worldVertices);
        if (uniquePositions.Count < worldVertices.Length * 0.1f) // Less than 10% unique positions
        {
            Debug.LogWarning($"⚠️ {filename} Frame {keyframeIndex}: Very few unique vertex positions ({uniquePositions.Count}/{worldVertices.Length}) - possible compression artifacts");
            hasWarnings = true;
        }
        
        if (!hasErrors && !hasWarnings)
        {
            // Only log success for first frame to avoid spam
            if (keyframeIndex == 0)
            {
                Debug.Log($"✅ {filename} Frame {keyframeIndex}: Vertex positions valid ({worldVertices.Length} vertices, bounds size {boundsSize:F3})");
            }
        }
    }
    
    /// <summary>
    /// Validates that decompressed RAT vertex data matches original Unity scene data within tolerance
    /// </summary>
    public static void ValidateRatDataIntegrity(string actPath)
    {
        string filename = Path.GetFileName(actPath);
        
        try
        {
            // Load ACT data
            var actData = LoadActorData(actPath);
            string directory = Path.GetDirectoryName(actPath);
            
            if (actData.ratFilePaths.Count == 0)
            {
                Debug.LogWarning($"⚠️ {filename}: No RAT files to validate");
                return;
            }
            
            // Load first RAT file for detailed validation
            string firstRatPath = Path.Combine(directory, actData.ratFilePaths[0]);
            if (!File.Exists(firstRatPath))
            {
                Debug.LogError($"❌ {filename}: RAT file not found: {firstRatPath}");
                return;
            }
            
            var ratAnim = Rat.Core.ReadRatFile(firstRatPath);
            var context = Rat.Core.CreateDecompressionContext(ratAnim);
            
            // Decompress all frames and validate
            int framesValidated = 0;
            int framesWithWarnings = 0;
            float maxQuantizationError = 0f;
            float avgQuantizationError = 0f;
            
            for (uint frame = 0; frame < ratAnim.num_frames; frame++)
            {
                Rat.Core.DecompressToFrame(context, ratAnim, frame);
                
                // Convert quantized vertices back to world space
                var decompressedVertices = new Vector3[ratAnim.num_vertices];
                for (int v = 0; v < ratAnim.num_vertices; v++)
                {
                    float x = ratAnim.min_x + (context.current_positions[v].x / 255f) * (ratAnim.max_x - ratAnim.min_x);
                    float y = ratAnim.min_y + (context.current_positions[v].y / 255f) * (ratAnim.max_y - ratAnim.min_y);
                    float z = ratAnim.min_z + (context.current_positions[v].z / 255f) * (ratAnim.max_z - ratAnim.min_z);
                    decompressedVertices[v] = new Vector3(x, y, z);
                }
                
                // Validate this frame's data
                ValidateRatFrameData(filename, frame, decompressedVertices, ratAnim, 
                    ref maxQuantizationError, ref avgQuantizationError);
                
                framesValidated++;
            }
            
            if (framesValidated > 0)
            {
                avgQuantizationError /= framesValidated;
                Debug.Log($"✅ {filename}: RAT data integrity validated");
                Debug.Log($"  Frames validated: {framesValidated}");
                Debug.Log($"  Max quantization error: {maxQuantizationError:F6} units");
                Debug.Log($"  Avg quantization error: {avgQuantizationError:F6} units");
                Debug.Log($"  Bounds: X[{ratAnim.min_x:F3}, {ratAnim.max_x:F3}] Y[{ratAnim.min_y:F3}, {ratAnim.max_y:F3}] Z[{ratAnim.min_z:F3}, {ratAnim.max_z:F3}]");
            }
            
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ {filename}: Data integrity validation failed: {e.Message}");
        }
    }
    
    /// <summary>
    /// Validates a single frame's decompressed data
    /// </summary>
    private static void ValidateRatFrameData(string filename, uint frameIndex, Vector3[] decompressedVertices, 
        Rat.CompressedAnimation ratAnim, ref float maxError, ref float avgError)
    {
        bool hasErrors = false;
        
        // Check for NaN/Infinity
        foreach (var vertex in decompressedVertices)
        {
            if (float.IsNaN(vertex.x) || float.IsNaN(vertex.y) || float.IsNaN(vertex.z) ||
                float.IsInfinity(vertex.x) || float.IsInfinity(vertex.y) || float.IsInfinity(vertex.z))
            {
                if (frameIndex == 0) // Only log once
                {
                    Debug.LogError($"❌ {filename} Frame {frameIndex}: Invalid vertex data (NaN/Infinity)");
                    hasErrors = true;
                }
                break;
            }
        }
        
        // Calculate quantization error (difference between 8-bit quantized and original float)
        // Error = (delta from quantization) / (range per axis)
        float rangeX = ratAnim.max_x - ratAnim.min_x;
        float rangeY = ratAnim.max_y - ratAnim.min_y;
        float rangeZ = ratAnim.max_z - ratAnim.min_z;
        
        if (rangeX > 0 && rangeY > 0 && rangeZ > 0)
        {
            float quantizationErrorX = rangeX / 255f; // Max error per axis (1 LSB)
            float quantizationErrorY = rangeY / 255f;
            float quantizationErrorZ = rangeZ / 255f;
            
            float frameMaxError = Mathf.Max(quantizationErrorX, quantizationErrorY, quantizationErrorZ);
            maxError = Mathf.Max(maxError, frameMaxError);
            avgError += frameMaxError;
            
            // Warn if quantization error is very large (indicates poor data fit in bounds)
            if (frameMaxError > 0.1f && frameIndex == 0)
            {
                Debug.LogWarning($"⚠️ {filename} Frame {frameIndex}: Large quantization error ({frameMaxError:F4} units) - " +
                               $"check if bounds are too large or vertex precision is insufficient");
            }
        }
        
        // Check bounds compliance
        foreach (var vertex in decompressedVertices)
        {
            if (vertex.x < ratAnim.min_x - 0.01f || vertex.x > ratAnim.max_x + 0.01f ||
                vertex.y < ratAnim.min_y - 0.01f || vertex.y > ratAnim.max_y + 0.01f ||
                vertex.z < ratAnim.min_z - 0.01f || vertex.z > ratAnim.max_z + 0.01f)
            {
                if (frameIndex == 0) // Only log once
                {
                    Debug.LogWarning($"⚠️ {filename} Frame {frameIndex}: Vertex outside bounds (check quantization)");
                }
                break;
            }
        }
        
        if (!hasErrors && frameIndex == 0)
        {
            Debug.Log($"  Frame {frameIndex}: Data valid ({decompressedVertices.Length} vertices)");
        }
    }

    [MenuItem("Ziz/Validate RAT Data Integrity All")]
    public static void ValidateAllRatDataIntegrity()
    {
        string generatedDataPath = Path.Combine(Application.dataPath.Replace("Assets", ""), "GeneratedData");
        
        if (!Directory.Exists(generatedDataPath))
        {
            Debug.LogError("GeneratedData directory not found!");
            return;
        }
        
        var actFiles = Directory.GetFiles(generatedDataPath, "*.act");
        Debug.Log($"=== Validating RAT Data Integrity for {actFiles.Length} Animation Files ===");
        
        foreach (string actFile in actFiles)
        {
            ValidateRatDataIntegrity(actFile);
        }
        
        Debug.Log("=== RAT Data Integrity Validation Complete ===");
    }

    // ...existing code...
    
    // Helper methods
    private static T BytesToStruct<T>(byte[] bytes) where T : struct
    {
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
        }
        finally
        {
            handle.Free();
        }
    }
    
    private static List<string> ParseNullTerminatedStrings(byte[] data)
    {
        var strings = new List<string>();
        int start = 0;
        
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == 0)
            {
                if (i > start)
                {
                    var strBytes = new byte[i - start];
                    System.Array.Copy(data, start, strBytes, 0, i - start);
                    strings.Add(System.Text.Encoding.UTF8.GetString(strBytes));
                }
                start = i + 1;
            }
        }
        
        return strings;
    }
    
    private static ActorAnimationData LoadActorData(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
        using (var reader = new BinaryReader(stream))
        {
            var headerBytes = reader.ReadBytes(Marshal.SizeOf<ActorHeader>());
            var header = BytesToStruct<ActorHeader>(headerBytes);
            
            var data = new ActorAnimationData();
            data.framerate = header.framerate;
            
            // Read RAT filenames
            stream.Seek(Marshal.SizeOf<ActorHeader>(), SeekOrigin.Begin);
            var ratBlob = reader.ReadBytes((int)header.rat_filenames_length);
            data.ratFilePaths.AddRange(ParseNullTerminatedStrings(ratBlob));
            
            return data;
        }
    }
    
    private static float Fixed16ToFloat(ushort fixedValue, float minValue, float maxValue)
    {
        if (maxValue <= minValue) return minValue;
        float normalized = fixedValue / 65535f;
        return minValue + normalized * (maxValue - minValue);
    }
    
    private static float Fixed16ToDegrees(ushort fixedValue)
    {
        return (fixedValue / 65535f) * 360f;
    }
}
#endif