using UnityEditor;
using UnityEngine;

// Editor-only custom inspector for SkyBox component and a menu item to create the skybox
[CustomEditor(typeof(ZizSceneEditor.Assets.Scripts.Shapes.SkyBox))]
public class SkyBoxEditor : Editor
{
    SerializedProperty topColorProp;
    SerializedProperty bottomColorProp;
    SerializedProperty horizonColorProp;
    SerializedProperty lonProp;
    SerializedProperty latProp;
    SerializedProperty radiusProp;

    const string AutoApplyKey = "ZizSkybox_AutoApply";

    void OnEnable()
    {
        var so = serializedObject;
    topColorProp = so.FindProperty("topColor");
    horizonColorProp = so.FindProperty("horizonColor");
    bottomColorProp = so.FindProperty("bottomColor");
        lonProp = so.FindProperty("longitudeSegments");
        latProp = so.FindProperty("latitudeSegments");
        radiusProp = so.FindProperty("radius");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

    EditorGUILayout.PropertyField(topColorProp);
    EditorGUILayout.PropertyField(horizonColorProp);
    EditorGUILayout.PropertyField(bottomColorProp);

        EditorGUILayout.IntSlider(lonProp, 3, 256, new GUIContent("Longitude Segments"));
        EditorGUILayout.IntSlider(latProp, 2, 128, new GUIContent("Latitude Segments"));
        EditorGUILayout.PropertyField(radiusProp);

        bool autoApply = EditorPrefs.GetBool(AutoApplyKey, true);
        autoApply = EditorGUILayout.Toggle("Auto Apply", autoApply);
        EditorPrefs.SetBool(AutoApplyKey, autoApply);

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Apply"))
        {
            ApplyNow();
        }
        if (GUILayout.Button("Generate Named Ziz_SkyBox"))
        {
            ZizSceneEditor.Assets.Scripts.Shapes.SkyBox.GenerateSkybox(
                lon: lonProp.intValue,
                lat: latProp.intValue,
                radius: radiusProp.floatValue,
                top: topColorProp.colorValue,
                horizon: horizonColorProp.colorValue,
                bottom: bottomColorProp.colorValue
            );
        }
        EditorGUILayout.EndHorizontal();

        serializedObject.ApplyModifiedProperties();

        if (autoApply && GUI.changed)
        {
            ApplyNow();
        }
    }

    void ApplyNow()
    {
        var sb = target as ZizSceneEditor.Assets.Scripts.Shapes.SkyBox;
        if (sb == null) return;
        // ensure serialized values are applied
        serializedObject.ApplyModifiedProperties();
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                ZizSceneEditor.Assets.Scripts.Shapes.SkyBox.GenerateOnGameObject(sb.gameObject, sb.longitudeSegments, sb.latitudeSegments, sb.radius, sb.topColor, sb.bottomColor, sb.horizonColor);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(sb.gameObject.scene);
            };
        }
        else
        {
            ZizSceneEditor.Assets.Scripts.Shapes.SkyBox.GenerateOnGameObject(sb.gameObject, sb.longitudeSegments, sb.latitudeSegments, sb.radius, sb.topColor, sb.bottomColor, sb.horizonColor);
        }
#else
        ZizSceneEditor.Assets.Scripts.Shapes.SkyBox.GenerateOnGameObject(sb.gameObject, sb.longitudeSegments, sb.latitudeSegments, sb.radius, sb.topColor, sb.bottomColor);
#endif
    }

    [MenuItem("Tools/Ziz/Generate Ziz_SkyBox")]
    static void MenuCreateSkybox()
    {
        GameObject existing = GameObject.Find("Ziz_SkyBox");
        if (existing != null)
        {
            Selection.activeGameObject = existing;
            EditorGUIUtility.PingObject(existing);
            return;
        }

        GameObject go = new GameObject("Ziz_SkyBox");
    var sb = go.AddComponent<ZizSceneEditor.Assets.Scripts.Shapes.SkyBox>();
    ZizSceneEditor.Assets.Scripts.Shapes.SkyBox.GenerateOnGameObject(go, sb.longitudeSegments, sb.latitudeSegments, sb.radius, sb.topColor, sb.bottomColor, sb.horizonColor);
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
    }
}
