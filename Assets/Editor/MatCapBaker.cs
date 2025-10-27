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
    private float iblIntensity = 1.0f;
    private float specularIBLIntensity = 1.0f;
    private float diffuseIBLIntensity = 1.0f;
    private float envMipScale = 8.0f;
    private float specularRoughnessFalloff = 0.5f;
    private Color sssColor = new Color(1.0f, 0.8f, 0.7f);
    private float sssStrength = 0.0f;
    private float sssScale = 0.5f;
    private bool useFresnel = true;
    private float fresnelPower = 1.0f;
    private Color fresnelTint = Color.white;
    private bool fresnelR = true;
    private bool fresnelG = true;
    private bool fresnelB = true;
    private float fresnelTintStrength = 0.5f;
    private float fresnelTintStart = 0.0f;
    private float fresnelTintEnd = 1.0f;

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
    iblIntensity = EditorGUILayout.Slider("IBL Intensity", iblIntensity, 0f, 4f);
    specularRoughnessFalloff = EditorGUILayout.Slider("Specular Roughness Falloff", specularRoughnessFalloff, 0.1f, 2.0f);

    EditorGUILayout.Space();
    GUILayout.Label("Subsurface Scattering", EditorStyles.boldLabel);
    sssColor = EditorGUILayout.ColorField("SSS Color", sssColor);
    sssStrength = EditorGUILayout.Slider("SSS Strength", sssStrength, 0f, 1f);
    sssScale = EditorGUILayout.Slider("SSS Scale", sssScale, 0f, 1f);

        EditorGUILayout.Space();
        GUILayout.Label("Fresnel", EditorStyles.boldLabel);
        useFresnel = EditorGUILayout.Toggle("Use Fresnel Diffuse Mod", useFresnel);
        fresnelPower = EditorGUILayout.Slider("Fresnel Power", fresnelPower, 0.1f, 5.0f);
    fresnelTint = EditorGUILayout.ColorField("Fresnel Tint", fresnelTint);
    fresnelTintStrength = EditorGUILayout.Slider("Fresnel Tint Strength", fresnelTintStrength, 0f, 1f);
    GUILayout.BeginHorizontal();
    GUILayout.Label("Fresnel Tint Range", GUILayout.Width(120));
    fresnelTintStart = EditorGUILayout.Slider(fresnelTintStart, 0f, 1f);
    fresnelTintEnd = EditorGUILayout.Slider(fresnelTintEnd, 0f, 1f);
    GUILayout.EndHorizontal();

    GUILayout.BeginHorizontal();
    GUILayout.Label("Channel Mask", GUILayout.Width(90));
    fresnelR = EditorGUILayout.ToggleLeft("R", fresnelR, GUILayout.Width(40));
    fresnelG = EditorGUILayout.ToggleLeft("G", fresnelG, GUILayout.Width(40));
    fresnelB = EditorGUILayout.ToggleLeft("B", fresnelB, GUILayout.Width(40));
    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();

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
    mat.SetColor("_SSSColor", sssColor);
    mat.SetFloat("_SSSStrength", sssStrength);
    mat.SetFloat("_SSSScale", sssScale);
    mat.SetFloat("_UseFresnel", useFresnel ? 1f : 0f);
    mat.SetFloat("_FresnelPower", fresnelPower);
        mat.SetColor("_FresnelTint", fresnelTint);
        Vector4 mask = new Vector4(fresnelR ? 1f : 0f, fresnelG ? 1f : 0f, fresnelB ? 1f : 0f, 0f);
        mat.SetVector("_FresnelChannelMask", mask);
        mat.SetFloat("_SpecularRoughnessFalloff", specularRoughnessFalloff);
        mat.SetFloat("_FresnelTintStrength", fresnelTintStrength);
    mat.SetFloat("_FresnelTintStart", fresnelTintStart);
    mat.SetFloat("_FresnelTintEnd", fresnelTintEnd);
    mat.SetFloat("_Exposure", exposure);
    mat.SetFloat("_IBLIntensity", iblIntensity);
    mat.SetFloat("_SpecularIBLIntensity", specularIBLIntensity);
    mat.SetFloat("_DiffuseIBLIntensity", diffuseIBLIntensity);
    mat.SetFloat("_EnvMipScale", envMipScale);
    mat.SetFloat("_SpecularRoughnessFalloff", specularRoughnessFalloff);
    mat.SetFloat("_FresnelTintStrength", fresnelTintStrength);
    mat.SetFloat("_FresnelTintStart", fresnelTintStart);
    mat.SetFloat("_FresnelTintEnd", fresnelTintEnd);
    mat.SetFloat("_FresnelTintStart", fresnelTintStart);
    mat.SetFloat("_FresnelTintEnd", fresnelTintEnd);

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
    mat.SetColor("_SSSColor", sssColor);
    mat.SetFloat("_SSSStrength", sssStrength);
    mat.SetFloat("_SSSScale", sssScale);
    mat.SetFloat("_UseFresnel", useFresnel ? 1f : 0f);
    mat.SetFloat("_FresnelPower", fresnelPower);
    mat.SetColor("_FresnelTint", fresnelTint);
    Vector4 mask = new Vector4(fresnelR ? 1f : 0f, fresnelG ? 1f : 0f, fresnelB ? 1f : 0f, 0f);
    mat.SetVector("_FresnelChannelMask", mask);
    mat.SetFloat("_Exposure", exposure);
    mat.SetFloat("_IBLIntensity", iblIntensity);

        // Render onto RT
        RenderTexture prev = RenderTexture.active;
        Graphics.Blit(null, rt, mat);
        RenderTexture.active = rt;

        // Read pixels into a half float Texture2D for EXR (preserves half floats)
        Texture2D texHalf = new Texture2D(SIZE, SIZE, TextureFormat.RGBAHalf, false, true);
        texHalf.ReadPixels(new Rect(0, 0, SIZE, SIZE), 0, 0);
        texHalf.Apply();

        // Threshold alpha to 1-bit (0 or 1) on the half-float texture's pixel data
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

        // Produce an 8-bit PNG preview by rendering the same material into an 8-bit sRGB RT
        RenderTexture rt8 = new RenderTexture(SIZE, SIZE, 0, RenderTextureFormat.ARGB32);
        rt8.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;
        rt8.useMipMap = false;
        rt8.Create();

        // Render the material into the 8-bit sRGB RT so what we read matches the editor preview
        Graphics.Blit(null, rt8, mat);
        RenderTexture.active = rt8;

        Texture2D tex8 = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false, false); // not linear (sRGB)
        tex8.ReadPixels(new Rect(0, 0, SIZE, SIZE), 0, 0);
        tex8.Apply();

        // Threshold alpha to 1-bit on the 8-bit preview as well
        Color[] px8 = tex8.GetPixels();
        for (int i = 0; i < px8.Length; ++i) {
            px8[i].a = px8[i].a > 0.5f ? 1.0f : 0.0f;
        }
        tex8.SetPixels(px8);
        tex8.Apply();

        string pngPath = Path.Combine(absFolder, outputName + ".png");
        File.WriteAllBytes(pngPath, tex8.EncodeToPNG());

        // Cleanup 8-bit RT
        RenderTexture.active = rt;
        rt8.Release();
        DestroyImmediate(rt8);

        // Cleanup
        RenderTexture.active = prev;
        rt.Release();
        DestroyImmediate(rt);

        // Import the saved files so they appear in the Editor
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("MatCap Baker", "Baked and saved:\n" + exrPath + "\n" + pngPath, "OK");
    }
}
