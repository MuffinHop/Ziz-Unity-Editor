using UnityEngine;
using UnityEditor;
using System.IO;

// Editor window to bake a PBR MatCap using the Hidden/MatCapGenerator/PBR shader
// Produces a 32x32 RGBAHalf EXR (preserves 16-bit half floats) and an 8-bit PNG preview.
// Note: Unity does not provide a built-in way to write 16-bit-per-channel PNGs. EXR (half) is the
// practical file format to preserve 16-bit (half) channels. The PNG written here is 8-bit and
// alpha is thresholded to 1-bit (0 or 255) as requested.

public class MatCapBaker : EditorWindow {
    private Material mat;
    private string outputName = "MatCap";
    private string outputFolder = "Assets/MatCaps";
    private Color baseColor = Color.gray;
    private float metallic = 0.0f;
    private float roughness = 0.25f;
    private Vector3 lightDir = new Vector3(0.577f, 0.577f, 0.577f);
    private Color lightColor = Color.white;
    private Cubemap envCube = null;
    private bool useIBL = true;
    private float exposure = 1.0f;
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
        if (GUILayout.Button("Update Preview", GUILayout.Width(140))) {
            UpdatePreview();
        }
        if (GUILayout.Button("Quick Bake & Save", GUILayout.Width(140))) {
            if (mat == null) EditorUtility.DisplayDialog("MatCap Baker", "Please assign a MatCap material first.", "OK"); else { BakeAndSave(); UpdatePreview(); }
        }
        GUILayout.EndHorizontal();

        GUILayout.Label("PBR MatCap Baker", EditorStyles.boldLabel);

        mat = (Material)EditorGUILayout.ObjectField("MatCap Material", mat, typeof(Material), false);
        outputName = EditorGUILayout.TextField("Output base name", outputName);
        outputFolder = EditorGUILayout.TextField("Output folder (Assets/...)", outputFolder);

        EditorGUILayout.Space();
        baseColor = EditorGUILayout.ColorField("Base Color", baseColor);
        metallic = EditorGUILayout.Slider("Metallic", metallic, 0f, 1f);
        roughness = EditorGUILayout.Slider("Roughness", roughness, 0f, 1f);
        EditorGUILayout.Space();
        lightDir = EditorGUILayout.Vector3Field("Light Direction", lightDir);
        lightColor = EditorGUILayout.ColorField("Light Color", lightColor);
    envCube = (Cubemap)EditorGUILayout.ObjectField("Environment Cubemap", envCube, typeof(Cubemap), false);
    useIBL = EditorGUILayout.Toggle("Use IBL", useIBL);
    useACES = EditorGUILayout.Toggle("Use ACES Tonemap", useACES);
    exposure = EditorGUILayout.FloatField("Exposure", exposure);

        EditorGUILayout.Space();
        if (GUILayout.Button("Bake MatCap (32x32 RGBA16/EXR + PNG preview)")) {
            if (mat == null) {
                EditorUtility.DisplayDialog("MatCap Baker", "Please assign a MatCap material first.", "OK");
            } else {
                BakeAndSave();
            }
        }
    }

    void OnEnable() {
        CreatePreviewRT();
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
            previewRT = new RenderTexture(PREVIEW_SIZE, PREVIEW_SIZE, 0, RenderTextureFormat.ARGBHalf);
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
        mat.SetVector("_LightDir", new Vector4(lightDir.x, lightDir.y, lightDir.z, 0));
        mat.SetColor("_LightColor", lightColor);
        if (envCube != null) mat.SetTexture("_EnvCube", envCube);
        mat.SetFloat("_UseIBL", useIBL ? 1f : 0f);
    mat.SetFloat("_UseACES", useACES ? 1f : 0f);
        mat.SetFloat("_Exposure", exposure);

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

        // Create a temporary RenderTexture with half precision (RGBAHalf)
        RenderTexture rt = new RenderTexture(SIZE, SIZE, 0, RenderTextureFormat.ARGBHalf);
        rt.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;
        rt.useMipMap = false;
        rt.Create();

        // Prepare material parameters
    mat.SetColor("_BaseColor", baseColor);
    mat.SetFloat("_Metallic", metallic);
    mat.SetFloat("_Roughness", roughness);
    mat.SetVector("_LightDir", new Vector4(lightDir.x, lightDir.y, lightDir.z, 0));
    mat.SetColor("_LightColor", lightColor);
    if (envCube != null) mat.SetTexture("_EnvCube", envCube);
    mat.SetFloat("_UseIBL", useIBL ? 1f : 0f);
    mat.SetFloat("_UseACES", useACES ? 1f : 0f);
    mat.SetFloat("_Exposure", exposure);

        // Render onto RT
        RenderTexture prev = RenderTexture.active;
        Graphics.Blit(null, rt, mat);
        RenderTexture.active = rt;

        // Read pixels into a half float Texture2D
        Texture2D texHalf = new Texture2D(SIZE, SIZE, TextureFormat.RGBAHalf, false, true);
        texHalf.ReadPixels(new Rect(0, 0, SIZE, SIZE), 0, 0);
        texHalf.Apply();

        // Threshold alpha to 1-bit (0 or 1)
        Color[] px = texHalf.GetPixels();
        for (int i = 0; i < px.Length; ++i) {
            px[i].a = px[i].a > 0.5f ? 1.0f : 0.0f;
        }
        texHalf.SetPixels(px);
        texHalf.Apply();

        // Save EXR (preserves half floats, closest to RGBA16 half precision)
        string exrPath = Path.Combine(absFolder, outputName + ".exr");
        byte[] exrBytes = texHalf.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
        File.WriteAllBytes(exrPath, exrBytes);

        // Also produce an 8-bit PNG preview with alpha thresholded
        Texture2D tex8 = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
        Color[] px8 = new Color[px.Length];
        for (int i = 0; i < px.Length; ++i) {
            // Convert from linear half to gamma 8-bit
            Color c = px[i];
            Color g = new Color(Mathf.Pow(c.r, 1.0f/2.2f), Mathf.Pow(c.g, 1.0f/2.2f), Mathf.Pow(c.b, 1.0f/2.2f), c.a);
            px8[i] = g;
        }
        tex8.SetPixels(px8);
        tex8.Apply();
        string pngPath = Path.Combine(absFolder, outputName + ".png");
        File.WriteAllBytes(pngPath, tex8.EncodeToPNG());

        // Cleanup
        RenderTexture.active = prev;
        rt.Release();
        DestroyImmediate(rt);

        // Import the saved files so they appear in the Editor
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("MatCap Baker", "Baked and saved:\n" + exrPath + "\n" + pngPath, "OK");
    }
}
