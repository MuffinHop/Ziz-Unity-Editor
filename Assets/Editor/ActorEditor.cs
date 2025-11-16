using UnityEngine;
using UnityEditor;
using System.IO;

[CustomEditor(typeof(Actor))]
public class ActorEditor : Editor
{
    private void OnEnable()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }
    
    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
    }
    
    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            // Save all Actor components' data when exiting play mode
            Actor[] actors = FindObjectsOfType<Actor>();
            foreach (Actor actor in actors)
            {
                if (actor.IsRecording)
                {
                    actor.StopRecording();
                    Debug.Log($"Auto-saved recording for actor {actor.name}");
                }
                else if (actor.AnimationData != null && actor.AnimationData.ratFilePaths.Count > 0)
                {
                    actor.SaveBothFiles();
                    Debug.Log($"Auto-saved animation data for actor {actor.name}");
                }
            }
        }
    }
    
    public override void OnInspectorGUI()
    {
        Actor actor = (Actor)target;
        
        // Draw default inspector fields first (keeps other settings intact)
        DrawDefaultInspector();

        // Add a rendering mode selector and main texture picker
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Material & Rendering", EditorStyles.boldLabel);

        // Rendering mode selector
        var newMode = (Rat.ActorRenderingMode)EditorGUILayout.EnumPopup("Rendering Mode", actor.renderingMode);
        if (newMode != actor.renderingMode)
        {
            actor.SetRenderingMode(newMode);
            EditorUtility.SetDirty(actor);
        }

    // Texture picker - allow empty for MatCap or vertex-colour modes
    Texture2D currentTexture = actor.GetAssignedTexture();
        Texture2D pickedTexture = (Texture2D)EditorGUILayout.ObjectField("Main Texture", currentTexture, typeof(Texture2D), false);
        if (pickedTexture != currentTexture)
        {
            // Convert asset path into relative path used by Actor
            string path = AssetDatabase.GetAssetPath(pickedTexture);
            if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/"))
                path = path.Substring("Assets/".Length);
            actor.SetTextureFilename(path);
            EditorUtility.SetDirty(actor);
        }

        // Show the selected texture filename relative to Assets (used by exports)
        EditorGUILayout.LabelField("Texture (relative path)", actor.TextureFilename ?? "(none)");
        EditorGUI.BeginDisabledGroup(actor.renderingMode == Rat.ActorRenderingMode.MatCap && actor.GetAssignedTexture() == null);
        if (GUILayout.Button("Clear Texture"))
        {
            actor.SetTextureFilename("");
            EditorUtility.SetDirty(actor);
        }
        EditorGUI.EndDisabledGroup();

        if (actor.renderingMode == Rat.ActorRenderingMode.MatCap)
        {
            if (actor.GetAssignedTexture() == null)
            {
                EditorGUILayout.HelpBox("MatCap mode requires a texture. Please assign a Main Texture or use Auto-select.", MessageType.Warning);
                if (GUILayout.Button("Auto-select MatCap Texture from Project"))
                {
                    // Find a matcap texture in the project and set it
                    string[] guids = UnityEditor.AssetDatabase.FindAssets("MatCap t:Texture2D");
                    string chosenPath = null;
                    if (guids.Length > 0)
                    {
                        chosenPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    }
                    else
                    {
                        // Try common paths
                        string[] potentials = new[] { "Assets/MatCaps/default.png", "Assets/MatCaps/matcap.png", "Assets/Textures/MatCap.png", "Assets/Shaders/MatCap.png" };
                        foreach (var p in potentials)
                        {
                            if (System.IO.File.Exists(p))
                            {
                                chosenPath = p;
                                break;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(chosenPath))
                    {
                        // Trim the leading "Assets/" for Actor.SetTextureFilename
                        string relative = chosenPath.StartsWith("Assets/") ? chosenPath.Substring("Assets/".Length) : chosenPath;
                        actor.SetTextureFilename(relative);
                        EditorUtility.SetDirty(actor);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("MatCap not found", "No MatCap texture found in the project. Add one to Assets/MatCaps or Assets/Textures.", "OK");
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("MatCap mode will use the assigned Main Texture as the matcap.", MessageType.Info);
            }
        }

        EditorGUILayout.Space();
        
        // Add spacing
        EditorGUILayout.Space();
        
        // Component validation section
        EditorGUILayout.LabelField("Component Validation", EditorStyles.boldLabel);
        
        // Check for renderers
        var meshRenderer = actor.GetComponent<MeshRenderer>();
        var skinnedMeshRenderer = actor.GetComponent<SkinnedMeshRenderer>();
        
        if (meshRenderer == null && skinnedMeshRenderer == null)
        {
            EditorGUILayout.HelpBox("ERROR: Actor requires either a MeshRenderer or SkinnedMeshRenderer component!", MessageType.Error);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add MeshRenderer"))
            {
                actor.gameObject.AddComponent<MeshRenderer>();
                if (actor.GetComponent<MeshFilter>() == null)
                {
                    actor.gameObject.AddComponent<MeshFilter>();
                }
                EditorUtility.SetDirty(actor);
            }
            if (GUILayout.Button("Add SkinnedMeshRenderer"))
            {
                actor.gameObject.AddComponent<SkinnedMeshRenderer>();
                EditorUtility.SetDirty(actor);
            }
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            string rendererType = meshRenderer != null ? "MeshRenderer" : "SkinnedMeshRenderer";
            EditorGUILayout.HelpBox($"✓ {rendererType} found", MessageType.Info);
            // Applied Shader + reapply button
            string shaderName = null;
            var renderer = meshRenderer != null ? (Renderer)meshRenderer : (Renderer)skinnedMeshRenderer;
            if (renderer != null && renderer.sharedMaterial != null)
                shaderName = renderer.sharedMaterial.shader != null ? renderer.sharedMaterial.shader.name : "(none)";
                EditorGUILayout.LabelField("Applied Shader", shaderName ?? "(no renderer/material)");
                EditorGUI.BeginDisabledGroup(actor.renderingMode == Rat.ActorRenderingMode.MatCap && actor.GetAssignedTexture() == null);
                if (GUILayout.Button("Re-apply Shader & Material"))
                {
                    actor.SetRenderingMode(actor.renderingMode);
                    EditorUtility.SetDirty(actor);
                }
                EditorGUI.EndDisabledGroup();
        }
        
        // Check for animator
        var animator = actor.GetComponentInParent<Animator>();
        if (animator == null)
        {
            EditorGUILayout.HelpBox("WARNING: No Animator found in this GameObject or its parents. Consider adding one for animation control.", MessageType.Warning);
            
            if (GUILayout.Button("Add Animator"))
            {
                actor.gameObject.AddComponent<Animator>();
                EditorUtility.SetDirty(actor);
            }
        }
        else
        {
            string animatorLocation = animator.gameObject == actor.gameObject ? "this GameObject" : $"parent: {animator.gameObject.name}";
            EditorGUILayout.HelpBox($"✓ Animator found on {animatorLocation}", MessageType.Info);
            
            // Show current frame rate if available
            if (Application.isPlaying && animator.runtimeAnimatorController != null)
            {
                float frameRate = actor.GetCurrentFrameRate();
                float duration = actor.GetAnimationDuration();
                uint totalFrames = actor.GetTotalFrames();
                
                EditorGUILayout.LabelField($"Animation Frame Rate: {frameRate:F1} FPS");
                EditorGUILayout.LabelField($"Animation Duration: {duration:F2} seconds");
                EditorGUILayout.LabelField($"Total Frames: {totalFrames}");
                EditorGUILayout.LabelField($"Current Key Frame: {actor.CurrentKeyFrame}");
                
                if (actor.IsRecording)
                {
                    EditorGUILayout.HelpBox("Recording both RAT and Actor data...", MessageType.Info);
                }
            }
            else if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see animation information", MessageType.Info);
            }
        }
        
        // Recording Status Section
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Recording Status", EditorStyles.boldLabel);
        
        // Recording automatically starts in Play Mode when an Animator is present
        if (Application.isPlaying)
        {
            if (actor.IsRecording)
            {
                EditorGUILayout.HelpBox("Recording both RAT and Actor data automatically...\nFiles will be saved when exiting Play Mode", MessageType.Info);
                EditorGUILayout.LabelField($"Saving to: GeneratedData/{actor.BaseFilename}.rat & GeneratedData/{actor.BaseFilename}.act");
                
                if (GUILayout.Button("Stop Recording Now"))
                {
                    actor.StopRecording();
                }
            }
            else if (actor.Animator != null)
            {
                EditorGUILayout.HelpBox("Recording starts automatically when animation begins\nFiles are saved on exiting Play Mode", MessageType.Info);
                EditorGUILayout.LabelField($"Will save to: GeneratedData/{actor.BaseFilename}.rat & GeneratedData/{actor.BaseFilename}.act");
            }
            else
            {
                EditorGUILayout.HelpBox("No Animator found - recording cannot start", MessageType.Warning);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Enter Play Mode to automatically start recording\nFiles will be saved automatically when exiting Play Mode", MessageType.Info);
            EditorGUILayout.LabelField($"Will save to: GeneratedData/{actor.BaseFilename}.rat & GeneratedData/{actor.BaseFilename}.act");
        }
        
        // File Management Section
        // Manual Stop Option Info
        if (actor.recordUntilManualStop)
        {
            EditorGUILayout.HelpBox("Recording is set to continue until manually stopped. Actor transform motion beyond animation clip will be recorded.", MessageType.Info);
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("File Management", EditorStyles.boldLabel);
        
        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Files are saved automatically on exiting Play Mode", MessageType.Info);
        }
        else if (actor.AnimationData != null && actor.AnimationData.ratFilePaths.Count > 0)
        {
            EditorGUILayout.HelpBox($"Previous recording found with {actor.AnimationData.ratFilePaths.Count} RAT file(s)\nReady for next Play Mode session", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("Recording and file saving happens automatically during Play Mode", MessageType.Info);
        }
        
        // Animation data info
        if (actor.AnimationData != null && actor.AnimationData.ratFilePaths.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Recorded Animation Data", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"RAT Files: {actor.AnimationData.ratFilePaths.Count}");
            EditorGUILayout.LabelField($"Framerate: {actor.AnimationData.framerate:F1} FPS");
            EditorGUILayout.LabelField($"Mesh Vertices: {actor.AnimationData.meshUVs?.Length ?? 0}");
            EditorGUILayout.LabelField($"Mesh Indices: {actor.AnimationData.meshIndices?.Length ?? 0}");
            
            // Validation section
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("C Engine Validation", EditorStyles.boldLabel);
            
            if (!string.IsNullOrEmpty(actor.RatFilePath))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Validate with RAT File"))
                {
                    string fullPath = actor.RatFilePath;
                    if (!fullPath.StartsWith("/") && !fullPath.Contains(":"))
                    {
                        // Convert relative path to absolute
                        fullPath = System.IO.Path.Combine(Application.dataPath.Replace("Assets", ""), actor.RatFilePath);
                    }
                    
                    bool isValid = actor.ValidateWithRatFile(fullPath);
                    if (isValid)
                    {
                        EditorUtility.DisplayDialog("Validation Success", 
                            "Actor and RAT files are synchronized!\n\n" +
                            $"RAT Files: {actor.AnimationData.ratFilePaths.Count}\n" +
                            $"Framerate: {actor.AnimationData.framerate:F1} FPS\n\n" +
                            "Your C engine can safely load both files.", "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Validation Failed", 
                            "Actor and RAT files are not synchronized!\n\n" +
                            "Check the Console for detailed error messages.\n" +
                            "Re-record both files to fix this issue.", "OK");
                    }
                }
                EditorGUILayout.EndHorizontal();
                
                // Show C engine integration info
                EditorGUILayout.HelpBox(
                    "C Engine Integration Ready!\n" +
                    "• Load the .rat file for vertex animation data\n" +
                    "• Load the .act file for mesh data and RAT file references\n" +
                    "• All transforms are baked into RAT vertex data\n" +
                    "• See Assets/CEngine/actor_format.h for complete documentation", 
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Set a RAT file path to enable validation.\n" +
                    "The RAT file contains vertex animation data that must be synchronized with Actor mesh data.", 
                    MessageType.Warning);
            }
        }
    }
}
