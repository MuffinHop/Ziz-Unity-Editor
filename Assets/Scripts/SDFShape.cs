using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using System.Collections.Generic;
using System.Text;
using System.Linq;
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

    // New: choose export framerate (30 or 25)
    // Set this to 30 (default) or 25 before exporting.
    public static int exportFrameRate = 30;

    // New: platform and resolution settings
    public static string targetPlatform = "Wii";
    public static SDFEmulatedResolution targetResolution = SDFEmulatedResolution.Tex512x512;

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
                scale = transform.localScale,
                time = Time.time // record timestamp for resampling
            };
        }

        // Periodically update title bar info (once per second)
        if (Time.frameCount % 60 == 0)
        {
            UpdateTitleBarWithFrameRateInfo();
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
    public struct TransformData
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public float time; // new: record Time.time for resampling
    }

    // Static storage for per-frame transforms during play mode
    private static Dictionary<int, Dictionary<SDFShape, TransformData>> frameTransforms = new Dictionary<int, Dictionary<SDFShape, TransformData>>();

    [InitializeOnLoad]
    private static class SDFShapeInitializer
    {
        static SDFShapeInitializer()
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
    }

    [UnityEditor.MenuItem("Tools/Export SDF Shapes BSON")]
    private static void ExportAllSDFShapesToBSON()
    {
        var shapes = FindObjectsOfType<SDFShape>();
        var allFrames = new Dictionary<int, Dictionary<SDFShape, TransformData>>();
        ExportAllFramesToBSON(allFrames);
    }

    // Shape definition: unique combination of type and parameters
    public struct ShapeDefinition
    {
        public SDFShapeType shapeType;
        public Color color;
        public float radius;
        public float roundness;
        public float smooth;
        public float thickness;
        public float headSize;
        public float shaftThickness;
        public float starInner;
        public float starOuter;
        public float starPoints;
        public int textureIndex; // index into textures array

        public override bool Equals(object obj)
        {
            if (!(obj is ShapeDefinition)) return false;
            var other = (ShapeDefinition)obj;
            return shapeType == other.shapeType &&
                   color == other.color &&
                   radius == other.radius &&
                   roundness == other.roundness &&
                   smooth == other.smooth &&
                   thickness == other.thickness &&
                   headSize == other.headSize &&
                   shaftThickness == other.shaftThickness &&
                   starInner == other.starInner &&
                   starOuter == other.starOuter &&
                   starPoints == other.starPoints;
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(shapeType);
            hash.Add(color);
            hash.Add(radius);
            hash.Add(roundness);
            hash.Add(smooth);
            hash.Add(thickness);
            hash.Add(headSize);
            hash.Add(shaftThickness);
            hash.Add(starInner);
            hash.Add(starOuter);
            hash.Add(starPoints);
            return hash.ToHashCode();
        }
    }

    // Compressed transform: position deltas (new), rotation byte, scale byte
    public struct CompressedTransform
    {
        // Positions now handled separately with deltas
        public byte rotX, rotY, rotZ, rotW;
        public byte scaleX, scaleY, scaleZ;

        public static CompressedTransform FromTransformData(TransformData td, Vector3 posMin, Vector3 posMax, Vector3 scaleMin, Vector3 scaleMax)
        {
            // Compress position: now handled externally with deltas
            var ct = new CompressedTransform();
            
            // Compress rotation: map [-1, 1] to [0, 255]
            ct.rotX = (byte)Mathf.Clamp01((td.rotation.x + 1f) * 127.5f);
            ct.rotY = (byte)Mathf.Clamp01((td.rotation.y + 1f) * 127.5f);
            ct.rotZ = (byte)Mathf.Clamp01((td.rotation.z + 1f) * 127.5f);
            ct.rotW = (byte)Mathf.Clamp01((td.rotation.w + 1f) * 127.5f);

            // Compress scale: map to [0, 255]
            ct.scaleX = (byte)Mathf.Clamp01(Mathf.InverseLerp(scaleMin.x, scaleMax.x, td.scale.x) * 255f);
            ct.scaleY = (byte)Mathf.Clamp01(Mathf.InverseLerp(scaleMin.y, scaleMax.y, td.scale.y) * 255f);
            ct.scaleZ = (byte)Mathf.Clamp01(Mathf.InverseLerp(scaleMin.z, scaleMax.z, td.scale.z) * 255f);

            return ct;
        }
    }

    // New: Compressed positions with deltas (per texture group) - removed visibilityStream
    public struct CompressedPositions
    {
        public byte[] firstFramePositions; // x,y,z per shape (normalized 0-255)
        public byte[] bitWidthsX, bitWidthsY, bitWidthsZ; // Per-shape bit widths for deltas
        public uint[] deltaStream; // Compressed deltas for subsequent frames
    }

    // New: Bitstream writer for visibility (1 bit per shape)
    public class VisibilityBitstreamWriter
    {
        private readonly List<uint> _stream = new List<uint>();
        private uint _currentWord = 0;
        private int _bitsUsed = 0;

        public void WriteBit(bool visible)
        {
            uint bit = visible ? 1U : 0U;
            int bitsRemainingInWord = 32 - _bitsUsed;
            _currentWord |= bit << (bitsRemainingInWord - 1);
            _bitsUsed++;
            if (_bitsUsed == 32)
            {
                _stream.Add(_currentWord);
                _currentWord = 0;
                _bitsUsed = 0;
            }
        }

        public void Flush()
        {
            if (_bitsUsed > 0) _stream.Add(_currentWord);
            _currentWord = 0;
            _bitsUsed = 0;
        }

        public uint[] ToArray() => _stream.ToArray();
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
        string animationBsonPath = System.IO.Path.Combine(generatedDataPath, "animation.bson");

        // Collect all unique shapes across frames
        var allShapes = new HashSet<SDFShape>();
        foreach (var frame in allFrames.Values)
        {
            foreach (var shape in frame.Keys)
            {
                allShapes.Add(shape);
            }
        }

        // Build unique shape definitions and texture index map
        var shapeDefinitions = new List<ShapeDefinition>();
        var shapeDefDict = new Dictionary<ShapeDefinition, int>();
        List<string> uniqueTexturePaths = new List<string>();
        Dictionary<string, int> textureIndex = new Dictionary<string, int>();
        long totalTextureBytes = 0;

        foreach (var s in allShapes)
        {
            s.RenderToPNG();
            int w = 0, h = 0;
            switch (s.emulatedResolution)
            {
                case SDFEmulatedResolution.Tex512x512: w = h = 512; break;
                case SDFEmulatedResolution.Tex256x256: w = h = 256; break;
                case SDFEmulatedResolution.Tex128x64: w = 128; h = 64; break;
                default: continue;
            }

            string texPath = s.BuildOutputFilename(w, h);
            if (!System.IO.File.Exists(texPath)) continue;

            if (!textureIndex.ContainsKey(texPath))
            {
                textureIndex[texPath] = uniqueTexturePaths.Count;
                uniqueTexturePaths.Add(texPath);
                totalTextureBytes += new System.IO.FileInfo(texPath).Length;
            }

            // Create shape definition
            var shapeDef = new ShapeDefinition
            {
                shapeType = s.shapeType,
                color = s.color,
                radius = s.radius,
                roundness = s.roundness,
                smooth = s.smooth,
                thickness = s.thickness,
                headSize = s.headSize,
                shaftThickness = s.shaftThickness,
                starInner = s.starInner,
                starOuter = s.starOuter,
                starPoints = s.starPoints,
                textureIndex = textureIndex[texPath]
            };

            if (!shapeDefDict.ContainsKey(shapeDef))
            {
                shapeDefDict[shapeDef] = shapeDefinitions.Count;
                shapeDefinitions.Add(shapeDef);
            }
        }

        // New: Simplify shape definitions to strings
        var shapeStrings = new List<string>();
        var shapeStringDict = new Dictionary<string, int>();
        foreach (var s in allShapes)
        {
            string shapeStr = $"{s.shapeType}_r{Math.Round(s.radius, 2)}_rd{Math.Round(s.roundness, 2)}_s{Math.Round(s.smooth, 3)}_t{Math.Round(s.thickness, 2)}";
            if (!shapeStringDict.ContainsKey(shapeStr))
            {
                shapeStringDict[shapeStr] = shapeStrings.Count;
                shapeStrings.Add(shapeStr);
            }
        }

        // Write textures.bson (only index and name)
        {
            using (var fs = System.IO.File.Create(texturesBsonPath))
            using (var writer = new BsonWriter(fs))
            {
                var serializer = new JsonSerializer();
                var root = new JObject();
                var texturesArray = new JArray();
                foreach (var kvp in textureIndex)
                {
                    var jo = new JObject();
                    jo["index"] = kvp.Value;
                    jo["name"] = System.IO.Path.GetFileName(kvp.Key);
                    texturesArray.Add(jo);
                }
                root["textures"] = texturesArray;
                serializer.Serialize(writer, root);
            }
        }

        // Calculate bounds for compression
        Vector3 posMin = Vector3.zero, posMax = Vector3.zero;
        bool first = true;
        foreach (var frame in allFrames.Values)
        {
            foreach (var td in frame.Values)
            {
                if (first) { posMin = posMax = td.position; first = false; }
                else
                {
                    posMin = Vector3.Min(posMin, td.position);
                    posMax = Vector3.Max(posMax, td.position);
                }
            }
        }
        Vector3 posRange = posMax - posMin;

        // New: Group shapes by textureIndex
        var shapesByTexture = new Dictionary<int, List<SDFShape>>();
        var shapeStringsByTexture = new Dictionary<int, List<string>>();
        foreach (var s in allShapes)
        {
            int w = 0, h = 0;
            switch (s.emulatedResolution)
            {
                case SDFEmulatedResolution.Tex512x512: w = h = 512; break;
                case SDFEmulatedResolution.Tex256x256: w = h = 256; break;
                case SDFEmulatedResolution.Tex128x64: w = 128; h = 64; break;
                default: continue;
            }
            string texPath = s.BuildOutputFilename(w, h);
            if (!textureIndex.TryGetValue(texPath, out int texIdx)) continue;
            if (!shapesByTexture.ContainsKey(texIdx)) shapesByTexture[texIdx] = new List<SDFShape>();
            shapesByTexture[texIdx].Add(s);
            if (!shapeStringsByTexture.ContainsKey(texIdx)) shapeStringsByTexture[texIdx] = new List<string>();
            string shapeStr = $"{s.shapeType}_r{Math.Round(s.radius, 2)}_rd{Math.Round(s.roundness, 2)}_s{Math.Round(s.smooth, 3)}_t{Math.Round(s.thickness, 2)}";
            if (!shapeStringsByTexture[texIdx].Contains(shapeStr)) shapeStringsByTexture[texIdx].Add(shapeStr);
        }

        // New: Per-texture compression - removed visibility
        var compressedPositionsByTexture = new Dictionary<int, CompressedPositions>();
        var orderedFrames = allFrames.OrderBy(k => k.Key).ToList();

        // Build ordered frames list (by frame key) and corresponding frame times
        var frameTimes = orderedFrames.Select(kv =>
        {
            var dict = kv.Value;
            if (dict != null && dict.Count > 0)
                return dict.Values.First().time; // all entries in a recorded frame share same time
            return 0f;
        }).ToList();

        // Resample frames to requested exportFrameRate (default 30 fps). Optionally set exportFrameRate = 25.
        float startTime = frameTimes.First();
        float endTime = frameTimes.Last();
        float dt = 1.0f / Math.Max(1, exportFrameRate);

        var sampledOrderedFrames = new List<KeyValuePair<int, Dictionary<SDFShape, TransformData>>>();
        int lastChosenIndex = -1;
        for (float t = startTime; t <= endTime + 1e-6f; t += dt)
        {
            // find nearest recorded frame index to target time t
            int bestIdx = 0;
            float bestDist = Math.Abs(frameTimes[0] - t);
            for (int i = 1; i < frameTimes.Count; i++)
            {
                float d = Math.Abs(frameTimes[i] - t);
                if (d < bestDist) { bestDist = d; bestIdx = i; }
            }
            // avoid consecutive duplicates
            if (bestIdx != lastChosenIndex)
            {
                sampledOrderedFrames.Add(orderedFrames[bestIdx]);
                lastChosenIndex = bestIdx;
            }
        }
        // ensure the final recorded frame is included
        if (sampledOrderedFrames.Count == 0 || !sampledOrderedFrames.Last().Equals(orderedFrames.Last()))
            sampledOrderedFrames.Add(orderedFrames.Last());

        // Use sampledOrderedFrames instead of orderedFrames for compression and writing
        foreach (var kvp in shapesByTexture)
        {
            int texIdx = kvp.Key;
            var shapesInGroup = kvp.Value.OrderBy(s => s.GetInstanceID()).ToList();
            var compressed = new CompressedPositions
            {
                firstFramePositions = new byte[shapesInGroup.Count * 3],
                bitWidthsX = new byte[shapesInGroup.Count],
                bitWidthsY = new byte[shapesInGroup.Count],
                bitWidthsZ = new byte[shapesInGroup.Count]
            };

            // First sampled frame positions
            var firstSampledFrame = sampledOrderedFrames.First().Value;
            for (int i = 0; i < shapesInGroup.Count; i++)
            {
                var shape = shapesInGroup[i];
                if (firstSampledFrame.TryGetValue(shape, out var td))
                {
                    compressed.firstFramePositions[i * 3] = (byte)((td.position.x - posMin.x) / posRange.x * 255f);
                    compressed.firstFramePositions[i * 3 + 1] = (byte)((td.position.y - posMin.y) / posRange.y * 255f);
                    compressed.firstFramePositions[i * 3 + 2] = (byte)((td.position.z - posMin.z) / posRange.z * 255f);
                }
            }

            // Bit widths for deltas based on sampled frames
            for (int i = 0; i < shapesInGroup.Count; i++)
            {
                var shape = shapesInGroup[i];
                int maxDx = 0, maxDy = 0, maxDz = 0;
                for (int f = 1; f < sampledOrderedFrames.Count; f++)
                {
                    var prevFrame = sampledOrderedFrames[f - 1].Value;
                    var currFrame = sampledOrderedFrames[f].Value;
                    if (prevFrame.TryGetValue(shape, out var prevTd) && currFrame.TryGetValue(shape, out var currTd))
                    {
                        int dx = (int)((currTd.position.x - posMin.x) / posRange.x * 255f) - (int)((prevTd.position.x - posMin.x) / posRange.x * 255f);
                        int dy = (int)((currTd.position.y - posMin.y) / posRange.y * 255f) - (int)((prevTd.position.y - posMin.y) / posRange.y * 255f);
                        int dz = (int)((currTd.position.z - posMin.z) / posRange.z * 255f) - (int)((prevTd.position.z - posMin.z) / posRange.z * 255f);
                        maxDx = Math.Max(maxDx, Math.Abs(dx));
                        maxDy = Math.Max(maxDy, Math.Abs(dy));
                        maxDz = Math.Max(maxDz, Math.Abs(dz));
                    }
                }
                compressed.bitWidthsX[i] = BitsForDelta(maxDx);
                compressed.bitWidthsY[i] = BitsForDelta(maxDy);
                compressed.bitWidthsZ[i] = BitsForDelta(maxDz);
            }

            // Compress deltas across sampled frames
            var deltaWriter = new BitstreamWriter();
            for (int f = 1; f < sampledOrderedFrames.Count; f++)
            {
                var prevFrame = sampledOrderedFrames[f - 1].Value;
                var currFrame = sampledOrderedFrames[f].Value;
                for (int i = 0; i < shapesInGroup.Count; i++)
                {
                    var shape = shapesInGroup[i];
                    if (prevFrame.TryGetValue(shape, out var prevTd) && currFrame.TryGetValue(shape, out var currTd))
                    {
                        int dx = (int)((currTd.position.x - posMin.x) / posRange.x * 255f) - (int)((prevTd.position.x - posMin.x) / posRange.x * 255f);
                        int dy = (int)((currTd.position.y - posMin.y) / posRange.y * 255f) - (int)((prevTd.position.y - posMin.y) / posRange.y * 255f);
                        int dz = (int)((currTd.position.z - posMin.z) / posRange.z * 255f) - (int)((prevTd.position.z - posMin.z) / posRange.z * 255f);
                        deltaWriter.Write((uint)dx, compressed.bitWidthsX[i]);
                        deltaWriter.Write((uint)dy, compressed.bitWidthsY[i]);
                        deltaWriter.Write((uint)dz, compressed.bitWidthsZ[i]);
                    }
                }
            }
            deltaWriter.Flush();
            compressed.deltaStream = deltaWriter.ToArray();

            compressedPositionsByTexture[texIdx] = compressed;
        }

        // Update posMin/posMax/posRange using sampled frames for accurate bounds
        posMin = Vector3.zero;
        posMax = Vector3.zero;
        bool firstPosSampled = true;
        foreach (var kv in sampledOrderedFrames)
        {
            foreach (var td in kv.Value.Values)
            {
                if (firstPosSampled) { posMin = posMax = td.position; firstPosSampled = false; }
                else { posMin = Vector3.Min(posMin, td.position); posMax = Vector3.Max(posMax, td.position); }
            }
        }
        posRange = posMax - posMin;

        // Write animation.bson - restructured for draw calls
        {
            using (var fs = System.IO.File.Create(animationBsonPath))
            using (var writerBson = new BsonWriter(fs))
            {
                var serializer = new JsonSerializer();
                var root = new JObject();
                var drawCallsArray = new JArray();

                foreach (var texKvp in shapesByTexture)
                {
                    int texIdx = texKvp.Key;
                    var drawCallObj = new JObject();
                    drawCallObj["textureIndex"] = texIdx;

                    // Shapes for this texture
                    var shapesArray = new JArray();
                    foreach (var shapeStr in shapeStringsByTexture[texIdx])
                    {
                        shapesArray.Add(shapeStr);
                    }
                    drawCallObj["shapes"] = shapesArray;

                    // Compressed positions for this texture
                    var compressed = compressedPositionsByTexture[texIdx];
                    var compressedObj = new JObject();
                    compressedObj["firstFramePositions"] = JToken.FromObject(compressed.firstFramePositions);
                    compressedObj["bitWidthsX"] = JToken.FromObject(compressed.bitWidthsX);
                    compressedObj["bitWidthsY"] = JToken.FromObject(compressed.bitWidthsY);
                    compressedObj["bitWidthsZ"] = JToken.FromObject(compressed.bitWidthsZ);
                    compressedObj["deltaStream"] = JToken.FromObject(compressed.deltaStream);
                    drawCallObj["compressedPositions"] = compressedObj;

                    drawCallsArray.Add(drawCallObj);
                }

                root["drawCalls"] = drawCallsArray;

                // Frames with drawCallIndex and shapeIndexInDrawCall
                var framesArray = new JArray();
                foreach (var kvp in sampledOrderedFrames)
                {
                    int frameNumber = kvp.Key;
                    var frameShapes = kvp.Value;

                    var frameObj = new JObject();
                    frameObj["frameNumber"] = frameNumber;
                    
                    var instancesArray = new JArray();
                    foreach (var texKvp in shapesByTexture)
                    {
                        int texIdx = texKvp.Key;
                        var shapesInGroup = texKvp.Value.OrderBy(s => s.GetInstanceID()).ToList();
                        var shapeStringsInGroup = shapeStringsByTexture[texIdx];
                        for (int i = 0; i < shapesInGroup.Count; i++)
                        {
                            var shape = shapesInGroup[i];
                            if (frameShapes.TryGetValue(shape, out var td))
                            {
                                string shapeStr = $"{shape.shapeType}_r{Math.Round(shape.radius, 2)}_rd{Math.Round(shape.roundness, 2)}_s{Math.Round(shape.smooth, 3)}_t{Math.Round(shape.thickness, 2)}";
                                int shapeIndexInDrawCall = shapeStringsInGroup.IndexOf(shapeStr);

                                var instanceObj = new JObject();
                                instanceObj["drawCallIndex"] = texIdx; // Assuming texIdx as drawCallIndex for simplicity
                                instanceObj["shapeIndexInDrawCall"] = shapeIndexInDrawCall;

                                instancesArray.Add(instanceObj);
                            }
                        }
                    }

                    frameObj["instances"] = instancesArray;
                    framesArray.Add(frameObj);
                }

                root["frames"] = framesArray;
                root["posMin"] = new JArray(posMin.x, posMin.y, posMin.z);
                root["posMax"] = new JArray(posMax.x, posMax.y, posMax.z);
                serializer.Serialize(writerBson, root);
            }
        }

        Debug.Log($"Exported {uniqueTexturePaths.Count} textures, {shapeDefinitions.Count} unique shapes, and {allFrames.Count} frames to {generatedDataPath}");
    }

    // New: Calculate hypothetical framerate based on vertex count
    private static float CalculateHypotheticalFrameRate(uint vertexCount)
    {
        const float msPerVertex = 0.0577f; // milliseconds per vertex
        float totalMs = vertexCount * msPerVertex;
        float ms16 = 16.67f; // ~60 FPS
        if (totalMs > ms16)
        {
            return 1000f / totalMs;
        }
        return 60f; // Cap at 60 FPS
    }

    // New: Update Unity title bar with framerate info
    private static void UpdateTitleBarWithFrameRateInfo()
    {
        try
        {
            // Count total vertices from all SDF shapes and RAT files
            uint totalSdfVertices = 0;
            var sdfShapes = FindObjectsOfType<SDFShape>();
            foreach (var shape in sdfShapes)
            {
                // Each SDF shape uses a quad (4 vertices)
                totalSdfVertices += 4;
            }

            // Calculate hypothetical framerates
            float sdfFrameRate = CalculateHypotheticalFrameRate(totalSdfVertices);
            string titleInfo = $"SDF FPS: {sdfFrameRate:F1} | Vertices: {totalSdfVertices}";

            // Update window title (Unity doesn't directly expose title bar, but we can log it)
            Debug.Log($"[Performance] {titleInfo}");
        }
        catch
        {
            // Silently fail if called outside editor context
        }
    }

    // New: Helper to compute bits needed for delta
    private static byte BitsForDelta(int delta)
    {
        int d = Math.Abs(delta);
        if (d == 0) return 1;
        int bits = 1;
        while ((1 << (bits - 1)) <= d) bits++;
        return (byte)bits;
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
