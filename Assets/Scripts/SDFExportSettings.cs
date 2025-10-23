using UnityEngine;
using UnityEditor;

public class SDFExportSettings : EditorWindow
{
    public enum TargetPlatform
    {
        N64,
        Dreamcast,
        PSP,
        Wii
    }

    private TargetPlatform selectedPlatform = TargetPlatform.Wii;
    private int selectedFrameRate = 30;

    [MenuItem("Tools/Platform Export Settings")]
    static void Init()
    {
        SDFExportSettings window = (SDFExportSettings)EditorWindow.GetWindow(typeof(SDFExportSettings));
        window.titleContent = new GUIContent("Platform Export Settings");
        window.Show();
    }

    void OnGUI()
    {
        GUILayout.Label("Platform Export Settings", EditorStyles.boldLabel);
        GUILayout.Space(10);

        // Platform selection
        GUILayout.Label("Target Platform", EditorStyles.boldLabel);
        selectedPlatform = (TargetPlatform)EditorGUILayout.EnumPopup("Platform:", selectedPlatform);

        // Display platform info
        string platformInfo = GetPlatformInfo(selectedPlatform);
        GUILayout.Label(platformInfo, EditorStyles.helpBox);

        GUILayout.Space(10);

        // Frame rate selection
        GUILayout.Label("Export Frame Rate", EditorStyles.boldLabel);
        int[] frameRateOptions = new int[] { 25, 30 };
        selectedFrameRate = EditorGUILayout.IntPopup("Frame Rate:", selectedFrameRate, new string[] { "25 FPS", "30 FPS" }, frameRateOptions);

        GUILayout.Space(10);

        // Apply settings
        if (GUILayout.Button("Apply Settings", GUILayout.Height(40)))
        {
            ApplySettings();
        }

        GUILayout.Space(10);

        // Display current settings
        GUILayout.Label("Current Settings", EditorStyles.boldLabel);
        GUILayout.Label($"Platform: {selectedPlatform}", EditorStyles.helpBox);
        GUILayout.Label($"Resolution: {GetResolutionForPlatform(selectedPlatform)}", EditorStyles.helpBox);
        GUILayout.Label($"Frame Rate: {SDFShape.exportFrameRate} FPS", EditorStyles.helpBox);

        GUILayout.Space(10);

        // Performance estimator
        GUILayout.Label("Performance Estimator", EditorStyles.boldLabel);
        DisplayPerformanceInfo();
    }

    private void DisplayPerformanceInfo()
    {
        const float msPerVertex = 0.0577f;
        
        // Count actual vertices from RAT (Actor) and SDF shapes in the scene
        uint totalVertices = CountVerticesInScene();
        
        float totalMs = totalVertices * msPerVertex;
        float hypotheticalFps = totalMs > 0 ? 1000f / totalMs : 60f;
        hypotheticalFps = Mathf.Min(hypotheticalFps, 60f);

        // Update window title with performance info
        string performanceInfo = $"Vertices: {totalVertices} | Render: {totalMs:F2}ms | FPS: {hypotheticalFps:F1}";
        titleContent = new GUIContent($"Target Platform Export Settings - {performanceInfo}");
    }

    private uint CountVerticesInScene()
    {
        uint totalVertices = 0;

        // Count vertices from RAT objects (Actor.cs)
        var ratActors = UnityEngine.Object.FindObjectsOfType<Actor>();
        foreach (var actor in ratActors)
        {
            // RatRecorder component handles vertex animation
            var ratRecorder = actor.GetComponent<RatRecorder>();
            if (ratRecorder != null)
            {
                // Estimate vertices from RatRecorder
                // Each RatRecorder has a target mesh (SkinnedMeshRenderer or MeshFilter)
                uint ratVertexCount = GetVertexCountFromRecorder(ratRecorder);
                totalVertices += ratVertexCount;
            }
        }

        // Count vertices from SDF shapes (each quad = 4 vertices)
        var sdfShapes = UnityEngine.Object.FindObjectsOfType<SDFShape>();
        totalVertices += (uint)(sdfShapes.Length * 4);

        return totalVertices;
    }

    private uint GetVertexCountFromRecorder(RatRecorder recorder)
    {
        // Try to get vertex count from the target mesh
        if (recorder.targetSkinnedMeshRenderer != null)
        {
            Mesh sharedMesh = recorder.targetSkinnedMeshRenderer.sharedMesh;
            if (sharedMesh != null)
            {
                return (uint)sharedMesh.vertexCount;
            }
        }

        if (recorder.targetMeshFilter != null)
        {
            Mesh sharedMesh = recorder.targetMeshFilter.sharedMesh;
            if (sharedMesh != null)
            {
                return (uint)sharedMesh.vertexCount;
            }
        }

        // Fallback: estimate based on typical mesh sizes
        return 1000;
    }

    private string GetPlatformInfo(TargetPlatform platform)
    {
        switch (platform)
        {
            case TargetPlatform.N64:
                return "Nintendo 64\nResolution: 128x64\nOptimized for retro handheld performance.";
            case TargetPlatform.Dreamcast:
                return "Sega Dreamcast\nResolution: 128x128\nOptimized for early 2000s console.";
            case TargetPlatform.PSP:
                return "PlayStation Portable\nResolution: 256x256\nOptimized for portable gaming.";
            case TargetPlatform.Wii:
                return "Nintendo Wii\nResolution: 512x512\nOptimized for modern console.";
            default:
                return "Unknown platform.";
        }
    }

    private string GetResolutionForPlatform(TargetPlatform platform)
    {
        switch (platform)
        {
            case TargetPlatform.N64:
                return "128x64";
            case TargetPlatform.Dreamcast:
                return "128x128";
            case TargetPlatform.PSP:
                return "256x256";
            case TargetPlatform.Wii:
                return "512x512";
            default:
                return "Unknown";
        }
    }

    private SDFEmulatedResolution GetEmulatedResolutionForPlatform(TargetPlatform platform)
    {
        switch (platform)
        {
            case TargetPlatform.N64:
                return SDFEmulatedResolution.Tex128x64;
            case TargetPlatform.Dreamcast:
                return SDFEmulatedResolution.Tex128x64; // Use closest available
            case TargetPlatform.PSP:
                return SDFEmulatedResolution.Tex256x256;
            case TargetPlatform.Wii:
                return SDFEmulatedResolution.Tex512x512;
            default:
                return SDFEmulatedResolution.None;
        }
    }

    private void ApplySettings()
    {
        SDFShape.exportFrameRate = selectedFrameRate;
        SDFShape.targetPlatform = selectedPlatform.ToString();
        SDFShape.targetResolution = GetEmulatedResolutionForPlatform(selectedPlatform);
        
        Debug.Log($"Applied Platform Export Settings:\nPlatform: {selectedPlatform}\nResolution: {GetResolutionForPlatform(selectedPlatform)}\nFrame Rate: {selectedFrameRate} FPS");
    }
}