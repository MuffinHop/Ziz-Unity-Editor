using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using Newtonsoft.Json.Linq;
#endif

public enum SDFEmulatedResolution 
{
    None,
    Tex512x512,
    Tex256x256,
    Tex128x64
}

public enum SDFShapeType 
{
    Circle,
    Box,
    Triangle,
    Capsule,
    Star,
    CircleRing,
    Cross,
    Plus,
    Arrow
}

[ExecuteInEditMode]
[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class SDFShape : MonoBehaviour 
{
    public SDFEmulatedResolution emulatedResolution = SDFEmulatedResolution.None;
    public SDFShapeType shapeType = SDFShapeType.Circle;
    public Color color = Color.white;
    public float radius = 0.4f;
    public float roundness = 0.0f;
    public float smooth = 0.01f;
    public float thickness = 0.1f;
    public float headSize = 0.25f;
    public float shaftThickness = 0.08f;
    public float starInner = 0.3f;
    public float starOuter = 0.5f;
    public float starPoints = 5f;

    private MeshRenderer _renderer;
    private MeshFilter _filter;
    private Material _unlitMaterial;    // Main mesh material (unlit texture)
    private Material _sdfMaterial;      // Material for rendering SDF to texture
    private RenderTexture _renderTexture;
    private bool _needsRender = false;
    private bool _isSetupComplete = false;

    private void Awake() 
    {
        _renderer = GetComponent<MeshRenderer>();
        _filter = GetComponent<MeshFilter>();
        EnsureQuadMesh();
    }

    private void OnEnable() 
    {
        _isSetupComplete = false;
        _needsRender = true;
    }

    private void OnValidate() 
    {
        _isSetupComplete = false;
        EnsureQuadMesh();
        UpdateMaterial();
        _needsRender = true;
    }

    private void OnDisable() 
    {
        CleanupRenderResources();
    }

    private void OnDestroy() 
    {
        CleanupRenderResources();
    }

    private void ClearRenderTexture()
    {
        if (_renderTexture != null && _renderTexture.IsCreated())
        {
            // Create a temporary black texture to clear with
            RenderTexture tempRT = RenderTexture.GetTemporary(_renderTexture.width, _renderTexture.height, 0, _renderTexture.format);
            RenderTexture.active = tempRT;
            GL.Clear(true, true, Color.clear);
            
            // Copy the cleared texture to our render texture
            Graphics.Blit(tempRT, _renderTexture);
            RenderTexture.ReleaseTemporary(tempRT);
        }
    }

    private void Update() 
    {
        if (!_isSetupComplete) 
        {
            UpdateMaterial();
            _isSetupComplete = true;
        }

        if (_renderTexture != null && _renderTexture.IsCreated() && _sdfMaterial != null) 
        {
            // Update material properties
            UpdateSDFMaterialProperties(_sdfMaterial);
            
            // Clear the texture before rendering new content
            ClearRenderTexture();
            
            // Render directly to texture using Graphics.Blit
            Graphics.Blit(null, _renderTexture, _sdfMaterial);
        }

        // Accumulate shapes per frame during play mode
        if (Application.isPlaying)
        {
            int frame = Time.frameCount;
            if (!frameTransforms.ContainsKey(frame))
                frameTransforms[frame] = new Dictionary<SDFShape, TransformData>();
            frameTransforms[frame][this] = new TransformData {
                position = transform.position,
                rotation = transform.rotation,
                scale = transform.localScale
            };
        }
    }

    private void CleanupRenderResources() 
    {
        if (_renderTexture != null) 
        {
            _renderTexture.Release();
            DestroyImmediate(_renderTexture);
            _renderTexture = null;
        }
        if (_sdfMaterial != null) 
        {
            DestroyImmediate(_sdfMaterial);
            _sdfMaterial = null;
        }
        if (_unlitMaterial != null) 
        {
            DestroyImmediate(_unlitMaterial);
            _unlitMaterial = null;
        }
        _isSetupComplete = false;
    }
    public bool recyclePreviousShapes = true; // Reuse previously-generated PNGs when parameters match
    
    /// <summary>
    /// Builds a deterministic output filename for the SDF shape PNG.
    /// The file is stored under the project's GeneratedData folder and is derived from:
    /// - shape type and resolution (width x height)
    /// - a human-readable parameter string (radius, roundness, smooth, thickness)
    /// - an extra suffix for shape-specific parameters (e.g. Arrow or Star)
    /// - a short hash computed from the concatenation of the above to ensure compact, collision-resistant identifiers
    /// 
    /// The resulting filename format is:
    /// Shape_{shape}_{width}x{height}_{paramString}{extra}_{hash}.png
    ///
    /// Important: no timestamps are used — filenames are fully deterministic based on parameters.
    /// This enables reusing previously-generated PNGs when recyclePreviousShapes is enabled.
    /// </summary>
    private string BuildOutputFilename(int width, int height)
    {
        string projectRoot = Application.dataPath.Replace("/Assets", "");
        string generatedDataPath = System.IO.Path.Combine(projectRoot, "GeneratedData");
        if (!System.IO.Directory.Exists(generatedDataPath))
            System.IO.Directory.CreateDirectory(generatedDataPath);

        string shapeName = shapeType.ToString();
        // Use the quantized parameter string (includes shape-specific extras) so filenames align with exported params
        string paramString = QuantizeParamString(width, height);

        // Stable filename: include a short hash of the parameters so identical configs reuse the file
        string hash = ComputeParamHash(shapeName + "|" + width + "x" + height + "|" + paramString);
        string fileName = string.Format("Shape_{0}_{1}x{2}_{3}_{4}.png", shapeName, width, height, paramString, hash);
        return System.IO.Path.Combine(generatedDataPath, fileName);
    }
    
    /// <summary>
    /// Computes a short parameter hash used in filenames.
    /// It uses MD5 over the provided input string and returns the first 8 hex characters
    /// (derived from the first 4 bytes of the MD5 digest) to keep filenames concise.
    /// </summary>
    private string ComputeParamHash(string input)
    {
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes(input);
            byte[] hash = md5.ComputeHash(data);
            // use first 8 hex chars for short id
            return BitConverter.ToString(hash, 0, 4).Replace("-", "");
        }
    }

    private void RenderToPNG() {
        // Simplified, robust exporter supporting multiple sizes.
        switch (emulatedResolution)
        {
            case SDFEmulatedResolution.Tex512x512:
                SaveRenderTextureToPNG(512, 512, recyclePreviousShapes);
                break;
            case SDFEmulatedResolution.Tex256x256:
                SaveRenderTextureToPNG(256, 256, recyclePreviousShapes);
                break;
            case SDFEmulatedResolution.Tex128x64:
                SaveRenderTextureToPNG(128, 64, recyclePreviousShapes);
                break;
            case SDFEmulatedResolution.None:
            default:
                // nothing to export
                break;
        }
    }
    // Render the SDF material into a temporary RenderTexture at the requested size and save as PNG
    private void SaveRenderTextureToPNG(int width, int height, bool stable = false)
    {
        if (_sdfMaterial == null)
        {
            Debug.LogError("SDF material is missing, cannot render to PNG");
            return;
        }

        string outPath = BuildOutputFilename(width, height);

        if (stable && System.IO.File.Exists(outPath))
        {
            Debug.Log($"Reusing existing SDF PNG: {outPath}");
            return; // reuse existing file
        }

        RenderTexture buffer = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        try
        {
            // Render the SDF into the buffer using the SDF material
            Graphics.Blit(null, buffer, _sdfMaterial);

            // Read back to CPU texture
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = buffer;
            Texture2D tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            tex.Apply(false, false);
            
            // Force the rendered shape to white (preserve alpha) to produce consistent white PNGs
            Color[] pixels = tex.GetPixels();
            for (int pi = 0; pi < pixels.Length; pi++)
            {
                if (pixels[pi].a > 0.01f)
                {
                    // Set RGB to white, keep alpha
                    pixels[pi].r = 1f;
                    pixels[pi].g = 1f;
                    pixels[pi].b = 1f;
                }
                else
                {
                    // Ensure fully transparent pixels are cleared
                    pixels[pi] = new Color(0f, 0f, 0f, 0f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(false, false);

            // Encode and write file (filename is stable and parameter-driven)
            byte[] bytes = tex.EncodeToPNG();
            System.IO.File.WriteAllBytes(outPath, bytes);
            Debug.Log($"Saved SDF PNG: {outPath}");

            DestroyImmediate(tex);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save SDF PNG: {e.Message}");
        }
        finally
        {
            RenderTexture.ReleaseTemporary(buffer);
        }
    }

    private void UpdateSDFMaterialProperties(Material material) 
    {
        if (material == null) return;
        
        material.SetColor("_Color", color);
        material.SetFloat("_Smooth", smooth);
        
        switch (shapeType) 
        {
            case SDFShapeType.Circle:
                material.SetFloat("_Radius", radius);
                break;
            case SDFShapeType.Box:
                material.SetFloat("_Roundness", roundness);
                break;
            case SDFShapeType.Triangle:
                break;
            case SDFShapeType.Capsule:
                material.SetFloat("_Radius", radius);
                break;
            case SDFShapeType.Star:
                material.SetFloat("_Points", starPoints);
                material.SetFloat("_Inner", starInner);
                material.SetFloat("_Outer", starOuter);
                break;
            case SDFShapeType.CircleRing:
                material.SetFloat("_Radius", radius);
                material.SetFloat("_Thickness", thickness);
                break;
            case SDFShapeType.Cross:
            case SDFShapeType.Plus:
                material.SetFloat("_Thickness", thickness);
                break;
            case SDFShapeType.Arrow:
                material.SetFloat("_HeadSize", headSize);
                material.SetFloat("_Thickness", shaftThickness);
                break;
        }
    }

    private void EnsureQuadMesh() 
    {
        if (_filter == null) _filter = GetComponent<MeshFilter>();
        if (_filter.sharedMesh == null || _filter.sharedMesh.vertexCount != 4) 
        {
            Mesh quad = new Mesh();
            quad.name = "SDFQuad";
            quad.vertices = new Vector3[] {
                new Vector3(-0.5f, -0.5f, 0),
                new Vector3( 0.5f, -0.5f, 0),
                new Vector3(-0.5f,  0.5f, 0),
                new Vector3( 0.5f,  0.5f, 0)
            };
            quad.uv = new Vector2[] {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(0, 1),
                new Vector2(1, 1)
            };
            quad.triangles = new int[] { 0, 1, 2, 2, 1, 3 };
            quad.normals = new Vector3[] {
                Vector3.forward,
                Vector3.forward,
                Vector3.forward,
                Vector3.forward
            };
            quad.bounds = new Bounds(Vector3.zero, new Vector3(1, 1, 0.01f));
            _filter.sharedMesh = quad;
        }
    }

    public void UpdateMaterial() 
    {
        if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
        
        // Setup the unlit material for the visible mesh
        if (_unlitMaterial == null) 
        {
            Shader unlitShader = Shader.Find("Unlit/Transparent");
            if (unlitShader == null) 
            {
                Debug.LogError($"[SDFShape] Could not find Unlit/Transparent shader!");
                return;
            }
            
            _unlitMaterial = new Material(unlitShader);
            if (_unlitMaterial == null) 
            {
                Debug.LogError($"[SDFShape] Failed to create material from shader!");
                return;
            }
            _unlitMaterial.name = "SDF_Display_Unlit";
            
            // Set up transparency
            _unlitMaterial.renderQueue = 3000;
            _unlitMaterial.enableInstancing = true;
        }
        
        // Setup the SDF material for rendering to texture
        string shaderName = GetShaderName(shapeType);
        Shader sdfShader = Shader.Find(shaderName);
        if (sdfShader == null) return;
        
        if (_sdfMaterial == null || _sdfMaterial.shader != sdfShader) 
        {
            if (_sdfMaterial != null) DestroyImmediate(_sdfMaterial);
            _sdfMaterial = new Material(sdfShader);
            _sdfMaterial.name = $"SDFShape_{shapeType}_Mat";
        }
        
        // Ensure the mesh uses the unlit material
        if (_unlitMaterial != null && _renderer != null) 
        {
            _renderer.sharedMaterial = _unlitMaterial;
        }

        // Handle render texture setup
        if (emulatedResolution != SDFEmulatedResolution.None) 
        {
            int width, height;
            switch (emulatedResolution) 
            {
                case SDFEmulatedResolution.Tex512x512:
                    width = height = 512;
                    break;
                case SDFEmulatedResolution.Tex256x256:
                    width = height = 256;
                    break;
                case SDFEmulatedResolution.Tex128x64:
                    width = 128;
                    height = 64;
                    break;
                default:
                    width = height = 512;
                    break;
            }
            
            // Create or update render texture
            if (_renderTexture == null || _renderTexture.width != width || _renderTexture.height != height) 
            {
                if (_renderTexture != null) 
                {
                    _renderTexture.Release();
                    DestroyImmediate(_renderTexture);
                }
                _renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
                _renderTexture.filterMode = FilterMode.Bilinear;
                _renderTexture.Create();
                
                // Clear the new texture immediately
                ClearRenderTexture();
            }
            
            // Update materials
            _unlitMaterial.mainTexture = _renderTexture;
            _sdfMaterial.SetVector("_TexelSize", new Vector4(1.0f/width, 1.0f/height, width, height));
            
            // Update SDF parameters
            UpdateSDFMaterialProperties(_sdfMaterial);
        } 
        else 
        {
            // Clean up render texture
            if (_renderTexture != null) 
            {
                _renderTexture.Release();
                DestroyImmediate(_renderTexture);
                _renderTexture = null;
            }
            
            // Use SDF material directly
            _renderer.sharedMaterial = _sdfMaterial;
            _sdfMaterial.SetVector("_TexelSize", Vector4.zero);
            UpdateSDFMaterialProperties(_sdfMaterial);
        }
    }

    private string GetShaderName(SDFShapeType type) 
    {
        switch (type) 
        {
            case SDFShapeType.Circle: return "Custom/SDF_Circle";
            case SDFShapeType.Box: return "Custom/SDF_Box";
            case SDFShapeType.Triangle: return "Custom/SDF_Triangle";
            case SDFShapeType.Capsule: return "Custom/SDF_Capsule";
            case SDFShapeType.Star: return "Custom/SDF_Star";
            case SDFShapeType.CircleRing: return "Custom/SDF_CircleRing";
            case SDFShapeType.Cross: return "Custom/SDF_Cross";
            case SDFShapeType.Plus: return "Custom/SDF_Plus";
            case SDFShapeType.Arrow: return "Custom/SDF_Arrow";
            default: return "Custom/SDF_Circle";
        }
    }

    // Quantize parameters based on output resolution to reduce file churn for low-res outputs
    private string QuantizeParamString(int width, int height)
    {
        // Base quantization (0.01 steps)
        float radiusQ = (float)Math.Round(radius * 100f) / 100f;
        float roundnessQ = (float)Math.Round(roundness * 100f) / 100f;
        float smoothQ = (float)Math.Round(smooth * 100f) / 100f;
        float thicknessQ = (float)Math.Round(thickness * 100f) / 100f;

        string baseStr = string.Format("r{0:0.##}_rd{1:0.##}_s{2:0.###}_t{3:0.##}", radiusQ, roundnessQ, smoothQ, thicknessQ);

        // Add shape-specific quantized extras so matching works reliably
        string extra = "";
        if (shapeType == SDFShapeType.Arrow)
        {
            float headQ = (float)Math.Round(headSize * 100f) / 100f;
            float shaftQ = (float)Math.Round(shaftThickness * 100f) / 100f;
            extra = string.Format("_h{0:0.##}_st{1:0.##}", headQ, shaftQ);
        }
        else if (shapeType == SDFShapeType.Star)
        {
            float inQ = (float)Math.Round(starInner * 100f) / 100f;
            float outQ = (float)Math.Round(starOuter * 100f) / 100f;
            int pts = Mathf.RoundToInt(starPoints);
            extra = string.Format("_in{0:0.##}_ou{1:0.##}_p{2:0}", inQ, outQ, pts);
        }

        return baseStr + extra;
    }

#if UNITY_EDITOR
    // Struct to store transform data per frame
    private struct TransformData
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
    }

    // Static storage for per-frame transforms during play mode
    private static Dictionary<int, Dictionary<SDFShape, TransformData>> frameTransforms = new Dictionary<int, Dictionary<SDFShape, TransformData>>();

    [InitializeOnLoad]
    private static void Initialize()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            // Save all accumulated frames into a single BSON
            ExportAllFramesToBSON(frameTransforms);
            frameTransforms.Clear();
        }
    }

    [UnityEditor.MenuItem("Tools/Export SDF Shapes BSON")]
    private static void ExportAllSDFShapesToBSON()
    {
        var shapes = FindObjectsOfType<SDFShape>();
        ExportAllSDFShapesToBSON(-1, shapes, null);
    }

    public static void ExportAllFramesToBSON(Dictionary<int, Dictionary<SDFShape, TransformData>> allFrames)
    {
        if (allFrames == null || allFrames.Count == 0)
        {
            Debug.LogWarning("No frame data to export.");
            return;
        }

        string generatedDataPath = GetGeneratedDataPath();
        string texturesBsonPath = System.IO.Path.Combine(generatedDataPath, "textures.bson");
        string framesBsonPath = System.IO.Path.Combine(generatedDataPath, "frames.bson");

        // Collect all unique shapes across frames
        var allShapes = new HashSet<SDFShape>();
        foreach (var frame in allFrames.Values)
        {
            foreach (var shape in frame.Keys)
            {
                allShapes.Add(shape);
            }
        }

        // Ensure textures exist and build list of unique texture files
        List<string> uniqueTexturePaths = new List<string>();
        Dictionary<string, int> textureIndex = new Dictionary<string, int>();
        long totalTextureBytes = 0;

        foreach (var s in allShapes)
        {
            // Ensure texture exists
            s.RenderToPNG();
            // Determine stable path for current emulated resolution
            int w = 0, h = 0;
            switch (s.emulatedResolution)
            {
                case SDFEmulatedResolution.Tex512x512: w = h = 512; break;
                case SDFEmulatedResolution.Tex256x256: w = h = 256; break;
                case SDFEmulatedResolution.Tex128x64: w = 128; h = 64; break;
                default: continue; // skip shapes with no texture
            }

            string texPath = s.BuildOutputFilename(w, h);
            if (!System.IO.File.Exists(texPath))
            {
                Debug.LogWarning($"Texture not found for shape {s.name}: {texPath}");
                continue;
            }

            if (!textureIndex.ContainsKey(texPath))
            {
                textureIndex[texPath] = uniqueTexturePaths.Count;
                uniqueTexturePaths.Add(texPath);
                totalTextureBytes += new System.IO.FileInfo(texPath).Length;
            }
        }

        // Write textures.bson using Newtonsoft BSON
        {
            using (var fs = System.IO.File.Create(texturesBsonPath))
            using (var writer = new BsonWriter(fs))
            {
                var serializer = new JsonSerializer();
                var root = new JObject();
                var texturesArray = new JArray();
                foreach (var path in uniqueTexturePaths)
                {
                    var jo = new JObject();
                    jo["name"] = System.IO.Path.GetFileName(path);
                    byte[] data = System.IO.File.ReadAllBytes(path);
                    jo["data"] = JToken.FromObject(data);
                    texturesArray.Add(jo);
                }
                root["textures"] = texturesArray;
                serializer.Serialize(writer, root);
            }
        }

        // Write frames.bson using Newtonsoft BSON
        {
            using (var fs = System.IO.File.Create(framesBsonPath))
            using (var writer = new BsonWriter(fs))
            {
                var serializer = new JsonSerializer();
                var root = new JObject();
                var framesArray = new JArray();

                foreach (var kvp in allFrames.OrderBy(k => k.Key)) // Order by frame number
                {
                    int frameNumber = kvp.Key;
                    var frameShapes = kvp.Value;

                    var frameObj = new JObject();
                    frameObj["frameNumber"] = frameNumber;
                    var shapesArray = new JArray();

                    foreach (var shapeKvp in frameShapes)
                    {
                        var s = shapeKvp.Key;
                        var td = shapeKvp.Value;

                        var jo = new JObject();

                        // Transform
                        var transObj = new JObject();
                        transObj["position"] = new JArray(td.position.x, td.position.y, td.position.z);
                        transObj["rotation"] = new JArray(td.rotation.x, td.rotation.y, td.rotation.z, td.rotation.w);
                        transObj["scale"] = new JArray(td.scale.x, td.scale.y, td.scale.z);
                        jo["transform"] = transObj;

                        // Color
                        var c = s.color;
                        jo["color"] = new JArray(c.r, c.g, c.b, c.a);

                        // Shape metadata
                        jo["shapeType"] = s.shapeType.ToString();

                        // Texture index
                        int w = 0, h = 0;
                        switch (s.emulatedResolution)
                        {
                            case SDFEmulatedResolution.Tex512x512: w = h = 512; break;
                            case SDFEmulatedResolution.Tex256x256: w = h = 256; break;
                            case SDFEmulatedResolution.Tex128x64: w = 128; h = 64; break;
                        }
                        string texPath = (w > 0 && h > 0) ? s.BuildOutputFilename(w, h) : "";
                        int tidx = -1;
                        if (!string.IsNullOrEmpty(texPath) && textureIndex.TryGetValue(texPath, out int idx))
                            tidx = idx;
                        jo["textureIndex"] = tidx;

                        shapesArray.Add(jo);
                    }

                    frameObj["shapes"] = shapesArray;
                    framesArray.Add(frameObj);
                }

                root["frames"] = framesArray;
                root["totalTextureBytes"] = totalTextureBytes;
                serializer.Serialize(writer, root);
            }
        }

        Debug.Log($"Exported {uniqueTexturePaths.Count} textures ({totalTextureBytes} bytes) and {allFrames.Count} frames to {generatedDataPath}");
    }

    private static string GetGeneratedDataPath()
    {
        string projectRoot = Application.dataPath.Replace("/Assets", "");
        string generatedDataPath = System.IO.Path.Combine(projectRoot, "GeneratedData");
        if (!System.IO.Directory.Exists(generatedDataPath)) System.IO.Directory.CreateDirectory(generatedDataPath);
        return generatedDataPath;
    }
#endif
}
