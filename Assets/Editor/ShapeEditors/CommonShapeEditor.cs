using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(Shape), true)]
public class CommonShapeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        Shape shape = (Shape)target;
        if(GUILayout.Button("Rebuild Mesh"))
        {
            shape.Rebuild();
        }
        if (GUI.changed && shape.autoRebuild)
        {
            shape.Rebuild();
        }
    }

    private void OnSceneGUI()
    {
        Shape shape = (Shape)target;
        // Generic handle for color maybe later; specific shapes can have derived editors.
        // Example for CircleShape radius handle.
        if (shape is CircleShape circle)
        {
            EditorGUI.BeginChangeCheck();
            Vector3 worldCenter = circle.transform.position;
            Vector3 handlePos = worldCenter + Vector3.right * circle.radius;
            float size = HandleUtility.GetHandleSize(handlePos)*0.1f;
            Vector3 newWorld = Handles.Slider(handlePos, (handlePos-worldCenter).normalized, size, Handles.SphereHandleCap, 0f);
            float newRadius = (newWorld - worldCenter).magnitude;
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(circle, "Change Circle Radius");
                circle.radius = Mathf.Max(0,newRadius);
                circle.Rebuild();
                EditorUtility.SetDirty(circle);
            }
        }
    }
}
