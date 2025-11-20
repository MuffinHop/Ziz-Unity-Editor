using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System.IO;
using System.Linq;

namespace ZizSceneEditor.Assets.Scripts.Shapes
{
    /// <summary>
    /// Generates a simple inverted-sphere skybox mesh that uses vertex colours.
    /// Call GenerateSkybox() from the context menu or call SkyBox.GenerateSkybox(...) programmatically.
    /// The created GameObject will be named "Ziz_SkyBox" and will use the shader "Hidden/UnlitVertexColor" if available.
    /// This implementation avoids SRP-specific APIs and uses a basic vertex-colour unlit shader so it works under URP.
    /// </summary>
    [ExecuteAlways]
    public class SkyBox : MonoBehaviour
    {
        public Color topColor = new Color(0.2f, 0.45f, 0.9f, 1f);
        public Color bottomColor = new Color(0.95f, 0.95f, 1f, 1f);
    // Horizon/mid color between top and bottom (appears at normalized v=0.5)
    public Color horizonColor = new Color(0.6f, 0.75f, 0.95f, 1f);
        public int longitudeSegments = 48;
        public int latitudeSegments = 24;
        public float radius = 500f;

        [HideInInspector, SerializeField] private bool _hasBeenInitialized = false;
        [HideInInspector, SerializeField] private Color _lastTopColor;
        [HideInInspector, SerializeField] private Color _lastBottomColor;
        [HideInInspector, SerializeField] private Color _lastHorizonColor;
        [HideInInspector, SerializeField] private int _lastLongitudeSegments;
        [HideInInspector, SerializeField] private int _lastLatitudeSegments;
        [HideInInspector, SerializeField] private float _lastRadius;

        // Animation recording data
        private bool isRecordingAnimation = false;
        private float recordingStartTime;
        private List<Mesh> animationFrames = new List<Mesh>();
        private List<TransformData> frameTransforms = new List<TransformData>();
        public float animationFramerate = 30f;

        /// <summary>
        /// Stores transform data for a single frame
        /// </summary>
        [System.Serializable]
        private struct TransformData
        {
            public Vector3 position;
            public Vector3 rotation; // Euler angles
            // Effective world-space scale recorded as lossyscale
            public Vector3 scale;
        }

        /// <summary>
        /// Context menu hook so you can right-click the component and choose Generate Skybox in the inspector.
        /// </summary>
        [ContextMenu("Generate Skybox")]
        public void GenerateSkyboxContext()
        {
            _hasBeenInitialized = true;
            SaveCurrentParameters();
            // Generate on this GameObject (so inspector changes update the component's GameObject)
            GenerateOnGameObject(this.gameObject, longitudeSegments, latitudeSegments, radius, topColor, bottomColor, horizonColor); 
        }

        private void SaveCurrentParameters()
        {
            _lastTopColor = topColor;
            _lastBottomColor = bottomColor;
            _lastHorizonColor = horizonColor;
            _lastLongitudeSegments = longitudeSegments;
            _lastLatitudeSegments = latitudeSegments;
            _lastRadius = radius;
        }

        private bool ParametersChanged()
        {
            return topColor != _lastTopColor ||
                   bottomColor != _lastBottomColor ||
                   horizonColor != _lastHorizonColor ||
                   longitudeSegments != _lastLongitudeSegments ||
                   latitudeSegments != _lastLatitudeSegments ||
                   !Mathf.Approximately(radius, _lastRadius);
        }

        void Awake()
        {
            // Only generate skybox on first initialization to avoid resetting custom parameters after compilation
            if (!_hasBeenInitialized)
            {
                _hasBeenInitialized = true;
                SaveCurrentParameters();
                GenerateOnGameObject(this.gameObject, longitudeSegments, latitudeSegments, radius, topColor, bottomColor, horizonColor);
            }
        }

        void OnValidate()
        {
            // Only regenerate if parameters have actually changed (not just during deserialization/compilation)
            if (_hasBeenInitialized && ParametersChanged())
            {
                SaveCurrentParameters();
            }
            
            // Regenerate when inspector values change in the editor
            #if UNITY_EDITOR
            // Avoid doing heavy work if the editor is compiling or application is playing
            if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode && _hasBeenInitialized && ParametersChanged())
            {
                // Try to update the existing mesh in-place immediately (safe during OnValidate).
                // This avoids adding/removing components or assigning a new sharedMesh which can trigger SendMessage.
                UpdateExistingMeshInPlace(this.gameObject, longitudeSegments, latitudeSegments, radius, topColor, horizonColor, bottomColor);
                // If components or mesh are missing, schedule the full creation for after the editor event loop.
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode && this != null && ParametersChanged())
                    {
                        SaveCurrentParameters();
                        GenerateOnGameObject(this.gameObject, longitudeSegments, latitudeSegments, radius, topColor, bottomColor, horizonColor);
                    }
                };
            }
            #endif
        }

        static void UpdateExistingMeshInPlace(GameObject go, int lon, int lat, float radius, Color topColor, Color horizonColor, Color bottomColor)
        {
            if (go == null) return;
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null) return; // can't update if there is no mesh filter
            var mesh = mf.sharedMesh;
            if (mesh == null) return; // avoid assigning sharedMesh during OnValidate

            // Update the mesh vertices/colors/triangles in-place
            UpdateMeshData(mesh, lon, lat, radius, topColor, horizonColor, bottomColor);
        }

        static void UpdateMeshData(Mesh m, int lon, int lat, float radius, Color topColor, Color horizonColor, Color bottomColor)
        {
            lon = Mathf.Max(3, lon);
            lat = Mathf.Max(2, lat);

            var verts = new List<Vector3>( (lon+1)*(lat+1) );
            var tris = new List<int>( lon * lat * 6 );
            var cols = new List<Color>( (lon+1)*(lat+1) );

            for (int y = 0; y <= lat; y++)
            {
                float v = (float)y / lat; // 0..1 from bottom to top
                // Map v to phi so that v=0 is bottom (phi = PI) and v=1 is top (phi = 0).
                float phi = (1f - v) * Mathf.PI;
                for (int x = 0; x <= lon; x++)
                {
                    float u = (float)x / lon; // 0..1
                    float theta = u * Mathf.PI * 2f;
                    float sinPhi = Mathf.Sin(phi);
                    Vector3 p = new Vector3(
                        sinPhi * Mathf.Cos(theta),
                        Mathf.Cos(phi),
                        sinPhi * Mathf.Sin(theta)
                    );
                    Vector3 pos = p * radius;
                    verts.Add(pos);

                    // three-way interpolation: bottom -> horizon -> top with horizon at v=0.5
                    Color c;
                    if (v < 0.5f)
                    {
                        float t = v * 2f; // 0..1 from bottom to horizon
                        c = Color.Lerp(bottomColor, horizonColor, t);
                    }
                    else
                    {
                        float t = (v - 0.5f) * 2f; // 0..1 from horizon to top
                        c = Color.Lerp(horizonColor, topColor, t);
                    }
                    cols.Add(c);
                }
            }

            for (int y = 0; y < lat; y++)
            {
                for (int x = 0; x < lon; x++)
                {
                    int i0 = y * (lon + 1) + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + (lon + 1);
                    int i3 = i2 + 1;

                    // triangles - note we invert winding so the inside faces are visible
                    tris.Add(i0);
                    tris.Add(i2);
                    tris.Add(i1);

                    tris.Add(i1);
                    tris.Add(i2);
                    tris.Add(i3);
                }
            }

            m.Clear();
            m.name = "Ziz_Skybox_Mesh";
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.SetColors(cols);
            m.RecalculateBounds();
            Vector3[] normals = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++) normals[i] = -verts[i].normalized;
            m.normals = normals;
        }

        /// <summary>
        /// Generate a skybox on the provided GameObject (attach MeshFilter/MeshRenderer and reuse materials if possible).
        /// This does not set HideFlags so it can be used on scene GameObjects.
        /// </summary>
        public static GameObject GenerateOnGameObject(GameObject go, int lon, int lat, float radius, Color topColor, Color bottomColor, Color? horizon = null)
        {
            if (go == null) return GenerateSkybox(lon, lat, radius, topColor, horizon, bottomColor);

            // Try to reuse existing components when possible. DestroyImmediate is not allowed during OnValidate
            // so we avoid removing components and instead reuse or create new ones.
            var oldFilter = go.GetComponent<MeshFilter>();
            var oldRenderer = go.GetComponent<MeshRenderer>();

            Color hColor = horizon ?? new Color(0.6f, 0.75f, 0.95f, 1f);
            Mesh mesh = BuildInvertedSphereMesh(lon, lat, radius, topColor, hColor, bottomColor);

            MeshFilter mf = oldFilter != null ? oldFilter : go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = oldRenderer != null ? oldRenderer : go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            // Try to reuse existing material if compatible
            Material mat = null;
            if (oldRenderer != null && oldRenderer.sharedMaterial != null)
            {
                var existingMat = oldRenderer.sharedMaterial;
                if (existingMat.shader != null && (existingMat.shader.name == "Hidden/UnlitVertexColor" || existingMat.shader.name.Contains("Unlit") || existingMat.shader.name == "Hidden/UnlitVertexColorURP"))
                {
                    mat = existingMat;
                }
            }

            if (mat == null)
            {
                // Prefer a URP-compatible shader when using SRP (Universal RP). Fall back to the simple built-in shader.
                Shader sh = null;
                if (GraphicsSettings.currentRenderPipeline != null)
                {
                    sh = Shader.Find("Hidden/UnlitVertexColorURP");
                }
                if (sh == null)
                    sh = Shader.Find("Hidden/UnlitVertexColor");

                if (sh != null)
                {
                    mat = new Material(sh) { name = "Ziz_Skybox_VCol_Mat" };
                }
                else
                {
                    // Fall back to URP Unlit or built-in Unlit/Color so at least something is visible.
                    Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
                    if (urpUnlit != null)
                    {
                        mat = new Material(urpUnlit) { name = "Ziz_Skybox_URPUnlit_Fallback" };
                        // URP unlit uses _BaseColor
                        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", topColor);
                        else if (mat.HasProperty("_Color")) mat.SetColor("_Color", topColor);
                    }
                    else
                    {
                        mat = new Material(Shader.Find("Unlit/Color")) { name = "Ziz_Skybox_Fallback_Mat" };
                        if (mat.HasProperty("_Color")) mat.color = topColor;
                    }
                }
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Background;
            }

            mr.sharedMaterial = mat;
            // Editor debug log to help diagnose invisible material issues
            #if UNITY_EDITOR
            Debug.Log($"[SkyBox] Assigned material '{mr.sharedMaterial?.name}' using shader '{mr.sharedMaterial?.shader?.name}' to GameObject '{go.name}'");
            #endif
            return go;
        }

        /// <summary>
        /// Create or replace a GameObject named "Ziz_SkyBox" in the scene containing an inverted sphere mesh colored by vertex colour.
        /// </summary>
        public static GameObject GenerateSkybox(int lon = 48, int lat = 24, float radius = 500f, Color? top = null, Color? horizon = null, Color? bottom = null)
        {
            Color topColor = top ?? new Color(0.2f, 0.45f, 0.9f, 1f);
            Color horizonColor = horizon ?? new Color(0.6f, 0.75f, 0.95f, 1f);
            Color bottomColor = bottom ?? new Color(0.95f, 0.95f, 1f, 1f);

            var existing = GameObject.Find("Ziz_SkyBox");
            GameObject go = existing ?? new GameObject("Ziz_SkyBox");
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            Mesh mesh = BuildInvertedSphereMesh(lon, lat, radius, topColor, horizonColor, bottomColor);

            // Reuse components if they exist (avoid DestroyImmediate during editor callbacks)
            var mf = go.GetComponent<MeshFilter>() ?? go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.GetComponent<MeshRenderer>() ?? go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            // Find shader and create material
            Shader sh = Shader.Find("Hidden/UnlitVertexColor");
            Material mat = null;
            if (sh != null)
            {
                mat = new Material(sh) { name = "Ziz_Skybox_VCol_Mat" };
            }
            else
            {
                // fallback simple unlit material (may not use vertex colours)
                mat = new Material(Shader.Find("Unlit/Color")) { name = "Ziz_Skybox_Fallback_Mat" };
                mat.color = topColor;
            }

            // Render in background queue so it's always behind scene geometry
            if (mat != null)
            {
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Background;
                mr.sharedMaterial = mat;
            }

            // make sure it doesn't get saved into builds accidentally
            #if UNITY_EDITOR
            go.hideFlags |= HideFlags.DontSaveInBuild;
            #endif

            return go;
        }

        static Mesh BuildInvertedSphereMesh(int lon, int lat, float radius, Color topColor, Color horizonColor, Color bottomColor)
        {
            lon = Mathf.Max(3, lon);
            lat = Mathf.Max(2, lat);

            List<Vector3> verts = new List<Vector3>();
            List<int> tris = new List<int>();
            List<Color> cols = new List<Color>();

            for (int y = 0; y <= lat; y++)
            {
                    float v = (float)y / lat; // 0..1 from bottom to top
                    // Map v to phi so that v=0 is bottom (phi = PI) and v=1 is top (phi = 0).
                    float phi = (1f - v) * Mathf.PI;
                for (int x = 0; x <= lon; x++)
                {
                    float u = (float)x / lon; // 0..1
                    float theta = u * Mathf.PI * 2f;
                    float sinPhi = Mathf.Sin(phi);
                    Vector3 p = new Vector3(
                        sinPhi * Mathf.Cos(theta),
                        Mathf.Cos(phi),
                        sinPhi * Mathf.Sin(theta)
                    );
                    Vector3 pos = p * radius;
                    verts.Add(pos);

                    // three-way interpolation: bottom -> horizon -> top with horizon at v=0.5
                    Color c;
                    if (v < 0.5f)
                    {
                        float t = v * 2f; // 0..1 from bottom to horizon
                        c = Color.Lerp(bottomColor, horizonColor, t);
                    }
                    else
                    {
                        float t = (v - 0.5f) * 2f; // 0..1 from horizon to top
                        c = Color.Lerp(horizonColor, topColor, t);
                    }
                    cols.Add(c);
                }
            }

            for (int y = 0; y < lat; y++)
            {
                for (int x = 0; x < lon; x++)
                {
                    int i0 = y * (lon + 1) + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + (lon + 1);
                    int i3 = i2 + 1;

                    // triangles - note we invert winding so the inside faces are visible
                    tris.Add(i0);
                    tris.Add(i2);
                    tris.Add(i1);

                    tris.Add(i1);
                    tris.Add(i2);
                    tris.Add(i3);
                }
            }

            Mesh m = new Mesh();
            m.name = "Ziz_Skybox_Mesh";  
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.SetColors(cols);
            m.RecalculateBounds();
            // normals are not important for unlit vertex-colour shader, but set inward normals in case
            Vector3[] normals = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++) normals[i] = -verts[i].normalized;
            m.normals = normals;
            return m;
        }

        /// <summary>
        /// Context menu hook to export the SkyBox mesh as a .rat file
        /// </summary>
        [ContextMenu("Export SkyBox to RAT")]
        public void ExportToRAT()
        {
            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogError("SkyBox: No mesh found to export!");
                return;
            }

            var mesh = meshFilter.sharedMesh;
            
            // Use mesh colors and generate default UVs if none exist
            var colors = mesh.colors.Length > 0 ? mesh.colors : null;
            var uvs = mesh.uv.Length > 0 ? mesh.uv : GenerateDefaultUVs(mesh.vertices.Length);
            
            // Use unified ExportAnimation which handles transform baking, Z-flip, RAT+ACT export
            var frames = new List<UnityEngine.Vector3[]> { mesh.vertices };
            var actorTransforms = new List<Rat.ActorTransformFloat>() {
                new Rat.ActorTransformFloat {
                    position = transform.position,
                    rotation = transform.eulerAngles,
                    scale = transform.lossyScale,
                    rat_file_index = 0,
                    rat_local_frame = 0
                }
            };

            Rat.Tool.ExportAnimation(gameObject.name, frames, mesh, uvs, colors, 30f, "", 64, Rat.ActorRenderingMode.VertexColoursOnly, actorTransforms, true);
            Debug.Log($"SkyBox: Exported to RAT+ACT via unified ExportAnimation API");
        }
        
        /// <summary>
        /// Generates default UV coordinates for a mesh (spherical mapping)
        /// </summary>
        private UnityEngine.Vector2[] GenerateDefaultUVs(int vertexCount)
        {
            var uvs = new UnityEngine.Vector2[vertexCount];
            var vertices = GetComponent<MeshFilter>().sharedMesh.vertices;
            
            for (int i = 0; i < vertexCount; i++)
            {
                var v = vertices[i].normalized;
                uvs[i] = new UnityEngine.Vector2(
                    Mathf.Atan2(v.z, v.x) / (2 * Mathf.PI) + 0.5f,
                    v.y * 0.5f + 0.5f
                );
            }
            
            return uvs;
        }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
    private static class SkyBoxInitializer
    {
        static SkyBoxInitializer()
        {
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                // Auto-export all SkyBox instances to RAT files when exiting play mode
                var skyBoxes = UnityEngine.Object.FindObjectsOfType<SkyBox>();
                if (skyBoxes.Length > 0)
                {
                    UnityEngine.Debug.Log($"Exiting play mode - auto-exporting {skyBoxes.Length} SkyBox(es) to RAT files...");
                    foreach (var skyBox in skyBoxes)
                    {
                        skyBox.ExportToRAT();
                    }
                }
            }
        }
    }
#endif

    /// <summary>
    /// Exports the skybox animation to RAT and ACT files
    /// </summary>
    public void ExportAnimation()
    {
        if (animationFrames.Count == 0)
        {
            Debug.LogError("SkyBox: No animation frames to export!");
            return;
        }

        string baseFilename = gameObject.name;

        // Convert recorded transforms to ActorTransformFloat format
        var actorTransforms = frameTransforms.Select((t, index) => new Rat.ActorTransformFloat
        {
            position = t.position,
            rotation = t.rotation,
            scale = t.scale,
            rat_file_index = 0,
            rat_local_frame = (uint)index
        }).ToList();

        try
        {
            // Collect transform data and vertex frames for export
            List<Rat.ActorTransformFloat> allFramesTransforms = new List<Rat.ActorTransformFloat>();
            List<Vector3[]> allFramesVertices = new List<Vector3[]>();
            
            // Get the mesh from MeshFilter
            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogError("SkyBox: No mesh found to export!");
                return;
            }
            
            var mesh = meshFilter.sharedMesh;
            
            // For now, create a single frame from the current mesh state
            if (mesh != null && mesh.vertexCount > 0)
            {
                allFramesVertices.Add(mesh.vertices);
                
                allFramesTransforms.Add(new Rat.ActorTransformFloat
                {
                    position = transform.position,
                    rotation = transform.eulerAngles,
                    scale = transform.lossyScale,
                    rat_file_index = 0,
                    rat_local_frame = 0
                });
                
                string baseRatFilename = gameObject.name;
                float captureFramerate = 30f; // Default framerate

                Rat.Tool.ExportAnimation(
                    baseRatFilename,
                    allFramesVertices,
                    mesh,
                    null,
                    null,
                    captureFramerate,
                    "",  // No texture filename - vertex colours only
                    64,  // maxFileSizeKB
                    Rat.ActorRenderingMode.VertexColoursOnly,  // Changed to VertexColoursOnly
                    allFramesTransforms  // Pass transforms
                );
                
                Debug.Log($"SkyBox: Exported mesh with vertex colours and transforms baked into vertices");
            }
        }
                catch (System.Exception e)
                {
                    Debug.LogError($"SkyBox: Export failed - {e.Message}\n{e}");
                }

        // Clean up
        foreach (var frameMesh in animationFrames)
            UnityEngine.Object.Destroy(frameMesh);
        animationFrames.Clear();
        frameTransforms.Clear();
    }
}
}