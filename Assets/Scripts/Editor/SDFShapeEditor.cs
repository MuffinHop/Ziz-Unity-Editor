using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SDFShape))]
public class SDFShapeEditor : Editor {
    public override void OnInspectorGUI() {
        SDFShape shape = (SDFShape)target;

        EditorGUI.BeginChangeCheck();
        
        EditorGUILayout.LabelField("Rendering Settings", EditorStyles.boldLabel);
        shape.shapeType = (SDFShapeType)EditorGUILayout.EnumPopup("Shape Type", shape.shapeType);
        
        EditorGUI.BeginChangeCheck();
        shape.emulatedResolution = (SDFEmulatedResolution)EditorGUILayout.EnumPopup("Resolution", shape.emulatedResolution);
        if (EditorGUI.EndChangeCheck())
        {
            // This will trigger a re-setup of the texture renderer
            shape.UpdateMaterial();
        }
        
        if (shape.emulatedResolution != SDFEmulatedResolution.None)
        {
            EditorGUILayout.HelpBox($"Rendering to {(int)shape.emulatedResolution}x{(int)shape.emulatedResolution} texture", MessageType.Info);
        }
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Appearance", EditorStyles.boldLabel);
        shape.color = EditorGUILayout.ColorField("Color", shape.color);
        shape.smooth = EditorGUILayout.Slider("Smooth", shape.smooth, 0.0001f, 0.1f);

        switch (shape.shapeType) {
            case SDFShapeType.Circle:
                shape.radius = EditorGUILayout.Slider("Radius", shape.radius, 0.01f, 1f);
                break;
            case SDFShapeType.Box:
                shape.roundness = EditorGUILayout.Slider("Roundness", shape.roundness, 0f, 0.5f);
                break;
            case SDFShapeType.Triangle:
                // No extra params
                break;
            case SDFShapeType.Capsule:
                shape.radius = EditorGUILayout.Slider("Radius", shape.radius, 0.01f, 1f);
                break;
            case SDFShapeType.Star:
                shape.starPoints = EditorGUILayout.Slider("Points", shape.starPoints, 3, 10);
                shape.starInner = EditorGUILayout.Slider("Inner Radius", shape.starInner, 0.01f, 1f);
                shape.starOuter = EditorGUILayout.Slider("Outer Radius", shape.starOuter, 0.01f, 1f);
                break;
            case SDFShapeType.CircleRing:
                shape.radius = EditorGUILayout.Slider("Radius", shape.radius, 0.01f, 1f);
                shape.thickness = EditorGUILayout.Slider("Thickness", shape.thickness, 0.001f, 1f);
                break;
            case SDFShapeType.Cross:
            case SDFShapeType.Plus:
                shape.thickness = EditorGUILayout.Slider("Thickness", shape.thickness, 0.001f, 1f);
                break;
            case SDFShapeType.Arrow:
                shape.headSize = EditorGUILayout.Slider("Head Size", shape.headSize, 0.01f, 1f);
                shape.shaftThickness = EditorGUILayout.Slider("Shaft Thickness", shape.shaftThickness, 0.001f, 1f);
                break;
        }

        if (EditorGUI.EndChangeCheck()) {
            EditorUtility.SetDirty(shape);
            shape.UpdateMaterial();
        }
    }
}