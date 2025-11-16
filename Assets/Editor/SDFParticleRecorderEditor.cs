using UnityEngine;
using UnityEditor;

/// <summary>
/// Custom inspector for SDFParticleRecorder component
/// Provides visual feedback and controls for particle system recording
/// </summary>
[CustomEditor(typeof(SDFParticleRecorder))]
public class SDFParticleRecorderEditor : Editor
{
    private SerializedProperty targetParticleSystemProp;
    private SerializedProperty particleShapeTypeProp;
    private SerializedProperty shapeResolutionProp;
    private SerializedProperty particleColorProp;
    private SerializedProperty useParticleSystemColorsProp;
    private SerializedProperty roundnessProp;
    private SerializedProperty smoothProp;
    private SerializedProperty thicknessProp;
    private SerializedProperty arrowHeadSizeProp;
    private SerializedProperty arrowShaftThicknessProp;
    private SerializedProperty starInnerProp;
    private SerializedProperty starOuterProp;
    private SerializedProperty starPointsProp;
    private SerializedProperty captureFramerateProp;
    private SerializedProperty autoStartRecordingProp;
    private SerializedProperty onlyRecordWhenVisibleProp;
    private SerializedProperty baseFilenameProp;
    private SerializedProperty maxFileSizeKBProp;
    private SerializedProperty autoExportOnPlayModeExitProp;

    private bool showShapePreview = false;
    private Texture2D previewTexture;

    void OnEnable()
    {
        targetParticleSystemProp = serializedObject.FindProperty("targetParticleSystem");
        particleShapeTypeProp = serializedObject.FindProperty("particleShapeType");
        shapeResolutionProp = serializedObject.FindProperty("shapeResolution");
        particleColorProp = serializedObject.FindProperty("particleColor");
        useParticleSystemColorsProp = serializedObject.FindProperty("useParticleSystemColors");
        roundnessProp = serializedObject.FindProperty("roundness");
        smoothProp = serializedObject.FindProperty("smooth");
        thicknessProp = serializedObject.FindProperty("thickness");
        arrowHeadSizeProp = serializedObject.FindProperty("arrowHeadSize");
        arrowShaftThicknessProp = serializedObject.FindProperty("arrowShaftThickness");
        starInnerProp = serializedObject.FindProperty("starInner");
        starOuterProp = serializedObject.FindProperty("starOuter");
        starPointsProp = serializedObject.FindProperty("starPoints");
        captureFramerateProp = serializedObject.FindProperty("captureFramerate");
        autoStartRecordingProp = serializedObject.FindProperty("autoStartRecording");
        onlyRecordWhenVisibleProp = serializedObject.FindProperty("onlyRecordWhenVisible");
        baseFilenameProp = serializedObject.FindProperty("baseFilename");
        maxFileSizeKBProp = serializedObject.FindProperty("maxFileSizeKB");
        autoExportOnPlayModeExitProp = serializedObject.FindProperty("autoExportOnPlayModeExit");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SDFParticleRecorder recorder = (SDFParticleRecorder)target;

        // Header
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("SDF Particle System Recorder", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Records Unity ParticleSystem animations and exports them as SDF shape-based .act/.rat files for retro hardware.",
            MessageType.Info
        );

        EditorGUILayout.Space();

        // Particle System Target
        EditorGUILayout.LabelField("Particle System Target", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(targetParticleSystemProp);

        if (targetParticleSystemProp.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox(
                "No ParticleSystem assigned. The component will auto-detect the ParticleSystem on this GameObject.",
                MessageType.Warning
            );
        }

        EditorGUILayout.Space();

        // SDF Shape Settings
        EditorGUILayout.LabelField("SDF Shape Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(particleShapeTypeProp);
        EditorGUILayout.PropertyField(shapeResolutionProp);
        EditorGUILayout.PropertyField(particleColorProp);
        EditorGUILayout.PropertyField(useParticleSystemColorsProp);
        
        // Common shape parameters
        EditorGUILayout.PropertyField(smoothProp);
        
        // Shape-specific parameters
        SDFShapeType currentShape = (SDFShapeType)particleShapeTypeProp.enumValueIndex;
        
        // Roundness (for Box, Triangle)
        if (currentShape == SDFShapeType.Box || currentShape == SDFShapeType.Triangle)
        {
            EditorGUILayout.PropertyField(roundnessProp);
        }
        
        // Thickness (for CircleRing, Cross, Plus)
        if (currentShape == SDFShapeType.CircleRing || currentShape == SDFShapeType.Cross || currentShape == SDFShapeType.Plus)
        {
            EditorGUILayout.PropertyField(thicknessProp);
        }
        
        // Arrow parameters
        if (currentShape == SDFShapeType.Arrow)
        {
            EditorGUILayout.PropertyField(arrowHeadSizeProp);
            EditorGUILayout.PropertyField(arrowShaftThicknessProp);
        }
        
        // Star parameters
        if (currentShape == SDFShapeType.Star)
        {
            EditorGUILayout.PropertyField(starInnerProp);
            EditorGUILayout.PropertyField(starOuterProp);
            EditorGUILayout.PropertyField(starPointsProp);
        }

        // Button to update SDF settings (works in both edit and play mode)
        if (GUILayout.Button("Update SDF Shape & Material"))
        {
            recorder.UpdateSDFSettings();
        }
        
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "You can preview the SDF material in Edit Mode. The material will be applied but recording only works in Play Mode.",
                MessageType.Info
            );
        }

        // Shape preview toggle
        showShapePreview = EditorGUILayout.Foldout(showShapePreview, "Preview SDF Shape");
        if (showShapePreview)
        {
            DrawShapePreview(recorder);
        }

        EditorGUILayout.Space();

        // Recording Settings
        EditorGUILayout.LabelField("Recording Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(captureFramerateProp);
        EditorGUILayout.PropertyField(autoStartRecordingProp);
        EditorGUILayout.PropertyField(onlyRecordWhenVisibleProp);

        EditorGUILayout.HelpBox(
            "Recording continues while in Play Mode. Only records when particle system is playing and visible (if enabled).",
            MessageType.Info
        );

        EditorGUILayout.Space();

        // Export Settings
        EditorGUILayout.LabelField("Export Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(baseFilenameProp);
        EditorGUILayout.PropertyField(maxFileSizeKBProp);
        EditorGUILayout.PropertyField(autoExportOnPlayModeExitProp, new GUIContent("Auto-Export on Exit Play Mode"));

        if (autoExportOnPlayModeExitProp.boolValue)
        {
            EditorGUILayout.HelpBox(
                "Particle animation will be automatically exported when exiting Play Mode if any frames were recorded.",
                MessageType.Info
            );
        }

        EditorGUILayout.Space();

        // Runtime Controls
        EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode to record particle system animations.",
                MessageType.Info
            );
        }
        else
        {
            // Show recording controls in play mode
            EditorGUILayout.BeginHorizontal();
            
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Start Recording", GUILayout.Height(30)))
            {
                recorder.ManualStartRecording();
            }
            
            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("Stop & Export", GUILayout.Height(30)))
            {
                recorder.ManualStopRecording();
            }
            
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space();

        // Additional Info
        if (targetParticleSystemProp.objectReferenceValue != null)
        {
            ParticleSystem ps = targetParticleSystemProp.objectReferenceValue as ParticleSystem;
            if (ps != null)
            {
                EditorGUILayout.LabelField("Particle System Info", EditorStyles.boldLabel);
                var main = ps.main;
                EditorGUILayout.LabelField("Max Particles:", main.maxParticles.ToString());
                EditorGUILayout.LabelField("Duration:", main.duration.ToString("F2") + "s");
                EditorGUILayout.LabelField("Looping:", main.loop.ToString());
                
                // Estimate mesh complexity
                int maxParticles = main.maxParticles;
                int verticesPerParticle = 4;
                int totalVertices = maxParticles * verticesPerParticle;
                
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Export Estimation", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Max Vertices:", totalVertices.ToString());
                EditorGUILayout.LabelField("Max Triangles:", (maxParticles * 2).ToString());
                
                // Performance warning
                if (totalVertices > 10000)
                {
                    EditorGUILayout.HelpBox(
                        $"Warning: High vertex count ({totalVertices} vertices). Consider reducing max particles for better retro hardware performance.",
                        MessageType.Warning
                    );
                }
            }
        }

        EditorGUILayout.Space();

        // Quick Actions
        EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);
                
        if (GUILayout.Button("Open Output Folder"))
        {
            string outputPath = System.IO.Path.Combine(Application.dataPath.Replace("/Assets", ""), "GeneratedData");
            EditorUtility.RevealInFinder(outputPath);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawShapePreview(SDFParticleRecorder recorder)
    {
        EditorGUILayout.HelpBox(
            "Shape preview shows the SDF texture that will be used for particles.",
            MessageType.Info
        );

        string shapeType = particleShapeTypeProp.enumNames[particleShapeTypeProp.enumValueIndex];
        string resolution = shapeResolutionProp.enumNames[shapeResolutionProp.enumValueIndex];
        
        EditorGUILayout.LabelField("Shape:", $"{shapeType} ({resolution})");
        
        // Note: Actual texture preview would require rendering the SDF shape
        // For now, just show a description
        GUILayout.Box("SDF Shape Preview\n(Texture generated at runtime)", GUILayout.Height(100), GUILayout.ExpandWidth(true));
    }

    void OnDisable()
    {
        if (previewTexture != null)
        {
            DestroyImmediate(previewTexture);
        }
    }
}
