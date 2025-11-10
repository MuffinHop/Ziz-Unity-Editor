using UnityEngine;
using UnityEditor;
using System.IO;

// Editor window to bake a PBR MatCap using the Hidden/MatCapGenerator/PBR shader
// Produces an 8-bit PNG file.
// Note: Alpha is thresholded to 1-bit (0 or 255) as requested.

public class MatCapBaker : EditorWindow {
    private Material mat;
    private string outputName = "MatCap";
    private string outputFolder = "Assets/Textures/";
    private Color baseColor = Color.gray;
    private float metallic = 0.0f;
    private float roughness = 0.25f;
    private Color emissive = Color.black;
    private float transparency = 0.0f;
    private float ior = 1.5f;
    private Color skyColorTop = new Color(0.7f, 0.8f, 1.0f);
    private Color skyColorHorizon = new Color(0.35f, 0.4f, 0.5f);
    private Vector3 lightDir = new Vector3(0.577f, 0.577f, 0.577f);
    private Color lightColor = Color.white;
    private float directionalIntensity = 1.0f;
    private Vector3 pointLightPos = new Vector3(2f, 2f, 2f);
    private Color pointLightColor = Color.white;
    private float pointIntensity = 0.5f;
    private bool useACES = true;

    private const int SIZE = 32;
    // Preview RT size (larger for window preview)
    private const int PREVIEW_SIZE = 256;
    private RenderTexture previewRT;
    private bool autoUpdate = true;
    private double updateInterval = 1.0f/16.0f; // seconds
    private double lastUpdateTime = 0.0;

    [MenuItem("Tools/MatCap/Bake MatCap...")]
    public static void ShowWindow() {
        var w = GetWindow<MatCapBaker>("MatCap Baker");
        w.minSize = new Vector2(360, 260);
    }

    void EnsureMaterial() {
        if (mat == null) {
            Shader shader = Shader.Find("MatCapGenerator/PBR");
            if (shader != null) {
                mat = new Material(shader);
                mat.name = "MatCapGenerator_Material";
            } else {
                Debug.LogError("MatCapBaker: Could not find shader 'MatCapGenerator/PBR'");
            }
        }
    }

    void OnGUI() {
        // Preview pane
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        Rect previewRect = GUILayoutUtility.GetRect(160, 160, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
        if (previewRT != null) {
            // Draw the preview render texture
            EditorGUI.DrawPreviewTexture(previewRect, previewRT);
        } else {
            EditorGUI.DrawRect(previewRect, Color.black * 0.2f);
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        // Controls to update preview
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Quick Bake & Save", GUILayout.Width(140))) {
            EnsureMaterial();
            if (mat == null) {
                EditorUtility.DisplayDialog("MatCap Baker", "Shader 'MatCapGenerator/PBR' not found!", "OK");
            } else {
                BakeAndSave();
                UpdatePreview();
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.Label("PBR MatCap Baker", EditorStyles.boldLabel);

        outputName = EditorGUILayout.TextField("Output base name", outputName);
        outputFolder = EditorGUILayout.TextField("Output folder (Assets/...)", outputFolder);

        EditorGUILayout.Space();
        baseColor = EditorGUILayout.ColorField("Base Color", baseColor);
        metallic = EditorGUILayout.Slider("Metallic", metallic, 0f, 1f);
        roughness = EditorGUILayout.Slider("Roughness", roughness, 0f, 1f);
        emissive = EditorGUILayout.ColorField("Emissive", emissive);
        transparency = EditorGUILayout.Slider("Transparency", transparency, 0f, 1f);
        ior = EditorGUILayout.Slider("IOR", ior, 1f, 2f);
        skyColorTop = EditorGUILayout.ColorField("Sky Color Top", skyColorTop);
        skyColorHorizon = EditorGUILayout.ColorField("Sky Color Horizon", skyColorHorizon);
        EditorGUILayout.Space();
        GUILayout.Label("Directional Light", EditorStyles.boldLabel);
        lightDir = EditorGUILayout.Vector3Field("Light Direction", lightDir);
        lightColor = EditorGUILayout.ColorField("Light Color", lightColor);
        directionalIntensity = EditorGUILayout.FloatField("Light Intensity", directionalIntensity);
        EditorGUILayout.Space();
        GUILayout.Label("Point Light (optional)", EditorStyles.boldLabel);
        pointLightPos = EditorGUILayout.Vector3Field("Point Light Position", pointLightPos);
        pointLightColor = EditorGUILayout.ColorField("Point Light Color", pointLightColor);
        pointIntensity = EditorGUILayout.FloatField("Point Light Intensity", pointIntensity);
        useACES = EditorGUILayout.Toggle("Use ACES Tonemap", useACES);
        EditorGUILayout.Space();
        

        EditorGUILayout.Space();
        if (GUILayout.Button("Bake MatCap (PNG)")) {
            EnsureMaterial();
            if (mat == null) {
                EditorUtility.DisplayDialog("MatCap Baker", "Shader 'MatCapGenerator/PBR' not found!", "OK");
            } else {
                BakeAndSave();
            }
        }
    }

    void OnEnable() {
        CreatePreviewRT();
        EnsureMaterial();
        EditorApplication.update += OnEditorUpdate;
    }

    void OnDisable() {
        ReleasePreviewRT();
        EditorApplication.update -= OnEditorUpdate;
    }

    void OnEditorUpdate() {
        if (!autoUpdate) return;
        double t = EditorApplication.timeSinceStartup;
        if (t - lastUpdateTime >= updateInterval) {
            lastUpdateTime = t;
            UpdatePreview();
        }
    }

    void CreatePreviewRT() {
        if (previewRT == null) {
            previewRT = new RenderTexture(PREVIEW_SIZE, PREVIEW_SIZE, 0, RenderTextureFormat.ARGB32);
            previewRT.Create();
        }
    }

    void ReleasePreviewRT() {
        if (previewRT != null) {
            previewRT.Release();
            DestroyImmediate(previewRT);
            previewRT = null;
        }
    }

    void UpdatePreview() {
    if (mat == null) return;
        CreatePreviewRT();

        // Update material parameters
        mat.SetColor("_BaseColor", baseColor);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Roughness", roughness);
        mat.SetColor("_Emissive", emissive);
        mat.SetFloat("_Transparency", transparency);
        mat.SetFloat("_IOR", ior);
        mat.SetColor("_SkyColorTop", skyColorTop);
        mat.SetColor("_SkyColorHorizon", skyColorHorizon);
        mat.SetVector("_LightDir", new Vector4(lightDir.x, lightDir.y, lightDir.z, 0));
        mat.SetColor("_LightColor", lightColor);
        mat.SetFloat("_DirectionalIntensity", directionalIntensity);
        mat.SetVector("_PointLightPos", new Vector4(pointLightPos.x, pointLightPos.y, pointLightPos.z, 0));
        mat.SetColor("_PointLightColor", pointLightColor);
        mat.SetFloat("_PointIntensity", pointIntensity);
        mat.SetFloat("_UseACES", useACES ? 1f : 0f);

        RenderTexture prev = RenderTexture.active;
        Graphics.Blit(null, previewRT, mat);
        RenderTexture.active = prev;
        Repaint();
    }

    void BakeAndSave() {
        // Ensure output folder exists
        string absFolder = Path.Combine(Application.dataPath, "..", outputFolder);
        if (!Directory.Exists(absFolder)) {
            Directory.CreateDirectory(absFolder);
        }

        // Ensure GeneratedData folder exists
        string generatedDataFolder = Path.Combine(Application.dataPath, "..", "GeneratedData");
        if (!Directory.Exists(generatedDataFolder)) {
            Directory.CreateDirectory(generatedDataFolder);
        }

        // Create a temporary RenderTexture with half precision (RGBAHalf)
        RenderTexture rt = new RenderTexture(SIZE, SIZE, 0, RenderTextureFormat.ARGBHalf);
        rt.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;
        rt.useMipMap = false;
        rt.Create();

        // Prepare material parameters
        mat.SetColor("_BaseColor", baseColor);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Roughness", roughness);
        mat.SetColor("_Emissive", emissive);
        mat.SetFloat("_Transparency", transparency);
        mat.SetFloat("_IOR", ior);
        mat.SetColor("_SkyColorTop", skyColorTop);
        mat.SetColor("_SkyColorHorizon", skyColorHorizon);
        mat.SetVector("_LightDir", new Vector4(lightDir.x, lightDir.y, lightDir.z, 0));
        mat.SetColor("_LightColor", lightColor);
        mat.SetFloat("_DirectionalIntensity", directionalIntensity);
        mat.SetVector("_PointLightPos", new Vector4(pointLightPos.x, pointLightPos.y, pointLightPos.z, 0));
        mat.SetColor("_PointLightColor", pointLightColor);
        mat.SetFloat("_PointIntensity", pointIntensity);
        mat.SetFloat("_UseACES", useACES ? 1f : 0f);

        // Render onto RT
        RenderTexture prev = RenderTexture.active;
        Graphics.Blit(null, rt, mat);
        RenderTexture.active = rt;

        // Render the material into an 8-bit sRGB RT for PNG export
        Texture2D tex8 = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false, false); // not linear (sRGB)
        tex8.ReadPixels(new Rect(0, 0, SIZE, SIZE), 0, 0);
        tex8.Apply();

        string pngPath = Path.Combine(absFolder, outputName + ".png");
        string generatedDataPngPath = Path.Combine(generatedDataFolder, outputName + ".png");
        
        byte[] pngBytes = tex8.EncodeToPNG();
        File.WriteAllBytes(pngPath, pngBytes);
        File.WriteAllBytes(generatedDataPngPath, pngBytes);

        // Cleanup
        RenderTexture.active = prev;
        rt.Release();
        DestroyImmediate(rt);
        DestroyImmediate(tex8);

        // Import the saved files so they appear in the Editor
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("MatCap Baker", "Baked and saved:\n" + pngPath + "\n\nAlso saved to:\n" + generatedDataPngPath, "OK");
    }
}
