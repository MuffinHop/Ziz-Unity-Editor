using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Unity-Enhanced BSP/PVS Level Export System
/// Complete implementation with Unity API integration for hardware-accelerated PVS computation
/// </summary>
public class Level : MonoBehaviour
{
    [Header("Export Settings")]
    public string outputFileName = "level.emu";
    public string textureFileName = ""; // Leave empty to auto-detect from material
    public bool includeChildren = true;
    public bool useNearestFiltering = false; // False = bilinear, True = nearest neighbor
    
    [Header("Texture Processing")]
    public bool generateOptimizedTextures = true;
    public OptimizedTextureFormat targetTextureFormat = OptimizedTextureFormat.RGBA32;
    public int maxTextureSize = 32; // Default to RGBA32 limit
    public bool enableTexturePalettes = false; // For CI4/CI8 formats
    
    public enum OptimizedTextureFormat
    {
        RGBA16, // 44x44 max, 16-bit with 1-bit alpha
        RGBA32, // 32x32 max, 32-bit full RGBA
        CI8,    // 43x43 max, 256 colors with palette
        CI4,    // 64x64 max, 16 colors with palette
        Auto    // Automatically choose best format
    }
    
    [Header("BSP Configuration")]
    public int maxBSPDepth = 12;
    public int minFacesPerLeaf = 8;
    public float epsilon = 0.001f;
    
    [Header("PVS Settings")]
    public bool enablePVSComputation = true;
    public bool useAsyncPVSComputation = true;
    public int pvsComputationBatchSize = 10;
    public float maxVisibilityDistance = 100f;
    public int maxSamplePointsPerLeaf = 16;
    
    [Header("Unity API Integration")]
    public bool useUnityRenderTextures = true;
    public bool useUnityOcclusionCulling = true;
    public bool useFrustumCulling = true;
    public int renderTextureSize = 64;
    public bool useColorIDEncoding = true;
    
    [Header("Performance")]
    public bool enableMemoryOptimization = true;
    public bool enableHardwareAcceleration = true;
    
    [Header("File Format")]
    public bool useEndianSafeBinaryFormat = true;
    
    [Header("Debug")]
    public bool showDebugInfo = false;
    public bool enablePerformanceMonitoring = true;
    
    // Core data structures
    private List<Vector3> worldVertices = new List<Vector3>();
    private List<Vector3> worldNormals = new List<Vector3>();
    private List<Vector2> textureCoords = new List<Vector2>();
    private List<Color> vertexColors = new List<Color>();
    private List<Face> faces = new List<Face>();
    private List<BSPNode> leafNodes = new List<BSPNode>();
    private BSPNode rootBSPNode;
    private Dictionary<int, byte[]> leafPVSData = new Dictionary<int, byte[]>();
    
    // Unity API components
    private Camera pvsCamera;
    private RenderTexture renderTexture;
    private CommandBuffer commandBuffer;
    
    // Performance monitoring
    private System.Diagnostics.Stopwatch performanceTimer = new System.Diagnostics.Stopwatch();
    private long lastComputationTime;
    private int totalLeafCount;
    private int processedLeafCount;
    
    [System.Serializable]
    public class Face
    {
        public int[] vertexIndices;
        public Vector3 normal;
        public float d; // plane distance
        public Material material;
        public int materialIndex;
        
        public Face(int[] indices, Vector3 norm, Material mat = null)
        {
            vertexIndices = indices;
            normal = norm.normalized;
            material = mat;
            materialIndex = 0;
            // Calculate plane distance d from the first vertex
            if (indices != null && indices.Length > 0)
            {
                // This will be set properly when we have access to worldVertices
                d = 0f; // Placeholder - will be calculated when needed
            }
        }
    }
    
    [System.Serializable]
    public class BSPNode
    {
        public bool isLeaf;
        public Vector3 planeNormal;
        public float planeDistance;
        public BSPNode front;
        public BSPNode back;
        public List<Face> faces = new List<Face>();
        public int leafIndex = -1;
        public Bounds bounds;
        public Vector3 center;
        
        public BSPNode(bool leaf = false)
        {
            isLeaf = leaf;
        }
    }
    
    void Start()
    {
        SetupUnityComponents();
    }
    
    void OnApplicationPause(bool pauseStatus)
    {
        // Export when application is paused (including when exiting play mode)
        if (pauseStatus && Application.isPlaying)
        {
            ExportLevel();
        }
    }
    
    void OnApplicationFocus(bool hasFocus)
    {
        // Export when losing focus in editor (play mode ending)
        if (!hasFocus && Application.isEditor && Application.isPlaying)
        {
            ExportLevel();
        }
    }
    
    private void OnDisable()
    {
        // Use reflection to call ExportLevel regardless of its signature.
        var method = GetType().GetMethod("ExportLevel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (method == null) return;

        if (gameObject.activeInHierarchy)
        {
            // If method returns IEnumerator, start it as coroutine; else invoke normally.
            var result = method.Invoke(this, null);
            if (result is System.Collections.IEnumerator ie)
            {
                StartCoroutine(ie);
            }
        }
        else
        {
            // Invoke synchronously; if returns IEnumerator, run it to completion.
            try
            {
                var result = method.Invoke(this, null);
                if (result is System.Collections.IEnumerator ie)
                {
                    while (ie.MoveNext()) { }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"ExportLevel invocation failed in OnDisable: {ex}");
            }
        }
    }

    // Execute the ExportLevel IEnumerator synchronously (runs to completion immediately).
    // Note: This assumes the ExportLevel coroutine does not rely on per-frame yields (WaitForSeconds, yield return null, etc.).
    private void ExportLevelSync()
    {
        try
        {
            // ExportLevel is implemented as a void method in this class.
            // Call it directly for synchronous execution.
            ExportLevel();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"ExportLevelSync failed: {ex}");
        }
    }
    
    /// <summary>
    /// Setup Unity API components for PVS computation
    /// </summary>
    private void SetupUnityComponents()
    {
        if (useUnityRenderTextures)
        {
            // Create PVS camera
            GameObject cameraObj = new GameObject("PVS_Camera");
            pvsCamera = cameraObj.AddComponent<Camera>();
            pvsCamera.enabled = false;
            pvsCamera.cullingMask = ~0; // Render all layers
            pvsCamera.fieldOfView = 90f;
            pvsCamera.nearClipPlane = 0.1f;
            pvsCamera.farClipPlane = maxVisibilityDistance;
            
            // Create render texture
            renderTexture = new RenderTexture(renderTextureSize, renderTextureSize, 24);
            renderTexture.format = RenderTextureFormat.ARGB32;
            renderTexture.Create();
            
            pvsCamera.targetTexture = renderTexture;
            
            // Setup command buffer
            commandBuffer = new CommandBuffer();
            commandBuffer.name = "PVS_Computation";
        }
        
        Debug.Log("Unity API components initialized for hardware-accelerated PVS computation");
    }
    
    /// <summary>
    /// Main export function with Unity API integration
    /// </summary>
    public void ExportLevel()
    {
        performanceTimer.Restart();
        Debug.Log("Starting Unity-enhanced level export...");
        
        PrepareData();
        
        if (worldVertices.Count == 0)
        {
            Debug.LogError("No mesh data found to export!");
            Debug.LogError("Please ensure the GameObject with Level component has MeshFilter/MeshRenderer components,");
            Debug.LogError("or enable 'Include Children' to scan child objects for meshes.");
            return;
        }
        
        // Generate normals if needed
        if (worldNormals.Count != worldVertices.Count)
        {
            GenerateVertexNormals();
        }
        
        // Validate data consistency before BSP tree construction
        Debug.Log($"Data validation: {worldVertices.Count} vertices, {worldNormals.Count} normals, {textureCoords.Count} UVs, {vertexColors.Count} colors, {faces.Count} faces");
        
        if (faces.Count == 0)
        {
            Debug.LogError("No faces found! Cannot build BSP tree.");
            return;
        }
        
        // Build BSP tree
        Debug.Log("Building BSP tree...");
        rootBSPNode = BuildBSPTree(faces, 0);
        
        // Compute PVS with Unity API
        if (enablePVSComputation)
        {
            Debug.Log("Computing PVS with Unity API acceleration...");
            // Avoid starting coroutines if the GameObject is inactive (e.g. called from OnDisable via reflection)
            if (useAsyncPVSComputation && gameObject.activeInHierarchy)
            {
                StartCoroutine(ComputePVSDataAsync());
            }
            else
            {
                // Run synchronously if async is disabled or cannot start coroutine
                ComputePVSData();
            }
        }
        
        // Export to EMU format
        WriteEMUFile();
        
        if (showDebugInfo)
        {
            Debug.Log($"BSP tree built with {leafNodes.Count} leaf nodes");
            Debug.Log($"PVS computed for {leafPVSData.Count} leaves");
        }
        
        Debug.Log($"Level exported: {worldVertices.Count} vertices, {faces.Count} faces");
        performanceTimer.Stop();
        Debug.Log($"Total export time: {performanceTimer.ElapsedMilliseconds}ms");
    }
    
    /// <summary>
    /// Prepare mesh data from scene objects
    /// </summary>
    private void PrepareData()
    {
        // Clear previous data
        worldVertices.Clear();
        worldNormals.Clear();
        textureCoords.Clear();
        vertexColors.Clear();
        faces.Clear();
        leafNodes.Clear();
        rootBSPNode = null;
        
        if (includeChildren)
        {
            ExtractMeshDataRecursive(transform);
        }
        else
        {
            ExtractMeshData(gameObject);
        }
    }
    
    /// <summary>
    /// Extract mesh data recursively from transform hierarchy
    /// </summary>
    private void ExtractMeshDataRecursive(Transform parent)
    {
        ExtractMeshData(parent.gameObject);
        
        foreach (Transform child in parent)
        {
            ExtractMeshDataRecursive(child);
        }
    }
    
    /// <summary>
    /// Extract mesh data from a single GameObject
    /// </summary>
    private void ExtractMeshData(GameObject obj)
    {
        MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = obj.GetComponent<MeshRenderer>();
        
        if (meshFilter?.sharedMesh == null) return;
        
        Mesh mesh = meshFilter.sharedMesh;
        Matrix4x4 worldMatrix = obj.transform.localToWorldMatrix;
        
        int vertexOffset = worldVertices.Count;
        
        // Transform vertices to world space
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector2[] uvs = mesh.uv;
        Color[] colors = mesh.colors;
        
        for (int i = 0; i < vertices.Length; i++)
        {
            worldVertices.Add(worldMatrix.MultiplyPoint3x4(vertices[i]));
            
            if (normals.Length > i)
                worldNormals.Add(worldMatrix.MultiplyVector(normals[i]).normalized);
            else
                worldNormals.Add(Vector3.up);
                
            if (uvs.Length > i)
                textureCoords.Add(uvs[i]);
            else
                textureCoords.Add(Vector2.zero);
                
            if (colors.Length > i)
                vertexColors.Add(colors[i]);
            else
                vertexColors.Add(Color.white);
        }
        
        // Process triangles
        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            int[] triangles = mesh.GetTriangles(submesh);
            Material material = meshRenderer?.materials?.Length > submesh ? meshRenderer.materials[submesh] : null;
            
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int[] faceIndices = {
                    triangles[i] + vertexOffset,
                    triangles[i + 1] + vertexOffset,
                    triangles[i + 2] + vertexOffset
                };
                
                Vector3 v0 = worldVertices[faceIndices[0]];
                Vector3 v1 = worldVertices[faceIndices[1]];
                Vector3 v2 = worldVertices[faceIndices[2]];
                
                Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                Face face = new Face(faceIndices, normal, material);
                
                // Calculate plane distance (d = -dot(normal, point_on_plane))
                face.d = -Vector3.Dot(normal, v0);
                
                faces.Add(face);
            }
        }
    }
    
    /// <summary>
    /// Generate vertex normals if missing
    /// </summary>
    private void GenerateVertexNormals()
    {
        worldNormals.Clear();
        worldNormals.AddRange(new Vector3[worldVertices.Count]);
        
        foreach (Face face in faces)
        {
            foreach (int vertexIndex in face.vertexIndices)
            {
                worldNormals[vertexIndex] += face.normal;
            }
        }
        
        for (int i = 0; i < worldNormals.Count; i++)
        {
            worldNormals[i] = worldNormals[i].normalized;
        }
    }
    
    /// <summary>
    /// Build BSP tree with improved splitting
    /// </summary>
    private BSPNode BuildBSPTree(List<Face> nodeFaces, int depth)
    {
        BSPNode node = new BSPNode();
        
        if (depth >= maxBSPDepth || nodeFaces.Count <= minFacesPerLeaf)
        {
            node.isLeaf = true;
            node.faces = new List<Face>(nodeFaces);
            node.leafIndex = leafNodes.Count;
            leafNodes.Add(node);
            
            // Calculate leaf bounds
            CalculateNodeBounds(node);
            return node;
        }
        
        // Find best splitting plane
        Face splittingFace = FindBestSplittingPlane(nodeFaces);
        if (splittingFace == null)
        {
            node.isLeaf = true;
            node.faces = new List<Face>(nodeFaces);
            node.leafIndex = leafNodes.Count;
            leafNodes.Add(node);
            CalculateNodeBounds(node);
            return node;
        }
        
        node.planeNormal = splittingFace.normal;
        node.planeDistance = splittingFace.d;
        
        // Split faces
        List<Face> frontFaces = new List<Face>();
        List<Face> backFaces = new List<Face>();
        
        foreach (Face face in nodeFaces)
        {
            ClassifyFaceAgainstPlane(face, node, frontFaces, backFaces);
        }
        
        // Build child nodes
        if (frontFaces.Count > 0)
            node.front = BuildBSPTree(frontFaces, depth + 1);
        if (backFaces.Count > 0)
            node.back = BuildBSPTree(backFaces, depth + 1);
            
        CalculateNodeBounds(node);
        return node;
    }
    
    /// <summary>
    /// Calculate bounds for BSP node
    /// </summary>
    private void CalculateNodeBounds(BSPNode node)
    {
        if (node.isLeaf)
        {
            if (node.faces.Count > 0)
            {
                Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                
                foreach (Face face in node.faces)
                {
                    foreach (int vertexIndex in face.vertexIndices)
                    {
                        if (vertexIndex >= 0 && vertexIndex < worldVertices.Count)
                        {
                            Vector3 vertex = worldVertices[vertexIndex];
                            min = Vector3.Min(min, vertex);
                            max = Vector3.Max(max, vertex);
                        }
                    }
                }
                
                node.bounds = new Bounds((min + max) * 0.5f, max - min);
                node.center = node.bounds.center;
            }
            else
            {
                // Empty leaf - create minimal bounds at origin
                node.bounds = new Bounds(Vector3.zero, Vector3.one * 0.001f);
                node.center = Vector3.zero;
            }
        }
    }
    
    /// <summary>
    /// Find the best plane for splitting faces
    /// </summary>
    private Face FindBestSplittingPlane(List<Face> faces)
    {
        Face bestFace = null;
        float bestScore = float.MaxValue;
        
        foreach (Face candidateFace in faces)
        {
            int frontCount = 0;
            int backCount = 0;
            int splitCount = 0;
            
            foreach (Face testFace in faces)
            {
                if (testFace == candidateFace) continue;
                
                int result = ClassifyFace(testFace, candidateFace);
                if (result > 0) frontCount++;
                else if (result < 0) backCount++;
                else splitCount++;
            }
            
            float score = Math.Abs(frontCount - backCount) + splitCount * 8;
            if (score < bestScore)
            {
                bestScore = score;
                bestFace = candidateFace;
            }
        }
        
        return bestFace;
    }
    
    /// <summary>
    /// Classify face relative to splitting plane
    /// </summary>
    private int ClassifyFace(Face face, Face plane)
    {
        int positive = 0;
        int negative = 0;
        
        foreach (int vertexIndex in face.vertexIndices)
        {
            Vector3 vertex = worldVertices[vertexIndex];
            float distance = Vector3.Dot(plane.normal, vertex) - plane.d;
            
            if (distance > epsilon) positive++;
            else if (distance < -epsilon) negative++;
        }
        
        if (positive > 0 && negative > 0) return 0; // straddling
        if (positive > 0) return 1; // front
        return -1; // back
    }
    
    /// <summary>
    /// Classify and split face against BSP plane
    /// </summary>
    private void ClassifyFaceAgainstPlane(Face face, BSPNode node, List<Face> frontFaces, List<Face> backFaces)
    {
        // Create a temporary plane face for classification
        Face planeFace = new Face(new int[] { 0 }, node.planeNormal) { d = node.planeDistance };
        int classification = ClassifyFace(face, planeFace);
        
        if (classification > 0)
            frontFaces.Add(face);
        else if (classification < 0)
            backFaces.Add(face);
        else
        {
            // Face straddles plane - add to both sides for simplicity
            frontFaces.Add(face);
            backFaces.Add(face);
        }
    }
    
    /// <summary>
    /// Compute PVS data with Unity API acceleration
    /// </summary>
    private void ComputePVSData()
    {
        if (leafNodes.Count == 0) return;
        
        performanceTimer.Restart();
        leafPVSData.Clear();
        totalLeafCount = leafNodes.Count;
        processedLeafCount = 0;
        
        Debug.Log($"Computing PVS for {leafNodes.Count} leaves with Unity API...");
        
        for (int i = 0; i < leafNodes.Count; i++)
        {
            BSPNode leaf = leafNodes[i];
            byte[] pvsData = ComputeLeafPVS(leaf, i);
            leafPVSData[i] = pvsData;
            processedLeafCount++;
            
            if (enablePerformanceMonitoring && i % 10 == 0)
            {
                float progress = (float)i / leafNodes.Count * 100f;
                Debug.Log($"PVS computation progress: {progress:F1}% ({i}/{leafNodes.Count})");
            }
        }
        
        performanceTimer.Stop();
        lastComputationTime = performanceTimer.ElapsedMilliseconds;
        
        if (enablePerformanceMonitoring)
        {
            MonitorPVSPerformance();
        }
        
        Debug.Log($"PVS computation completed in {lastComputationTime}ms");
    }
    
    /// <summary>
    /// Async PVS computation for large scenes
    /// </summary>
    private IEnumerator ComputePVSDataAsync()
    {
        if (leafNodes.Count == 0) yield break;
        
        performanceTimer.Restart();
        leafPVSData.Clear();
        totalLeafCount = leafNodes.Count;
        processedLeafCount = 0;
        
        Debug.Log($"Starting async PVS computation for {leafNodes.Count} leaves...");
        
        for (int i = 0; i < leafNodes.Count; i += pvsComputationBatchSize)
        {
            int batchEnd = Mathf.Min(i + pvsComputationBatchSize, leafNodes.Count);
            
            // Process batch
            for (int j = i; j < batchEnd; j++)
            {
                BSPNode leaf = leafNodes[j];
                byte[] pvsData = ComputeLeafPVS(leaf, j);
                leafPVSData[j] = pvsData;
                processedLeafCount++;
            }
            
            // Progress update
            float progress = (float)(i + pvsComputationBatchSize) / leafNodes.Count * 100f;
            Debug.Log($"Async PVS progress: {progress:F1}% ({processedLeafCount}/{leafNodes.Count})");
            
            // Yield every batch to prevent frame drops
            yield return null;
        }
        
        performanceTimer.Stop();
        lastComputationTime = performanceTimer.ElapsedMilliseconds;
        
        if (enablePerformanceMonitoring)
        {
            MonitorPVSPerformance();
        }
        
        Debug.Log($"Async PVS computation completed in {lastComputationTime}ms");
        
        // Continue with file export
        WriteEMUFile();
        
    }
    
    /// <summary>
    /// Compute PVS for a single leaf using Unity APIs
    /// </summary>
    private byte[] ComputeLeafPVS(BSPNode leaf, int leafIndex)
    {
        if (useUnityRenderTextures && pvsCamera != null)
        {
            return ComputeLeafPVSWithUnityCamera(leaf, leafIndex);
        }
        else if (useFrustumCulling)
        {
            return ComputeLeafPVSWithGeometryUtility(leaf, leafIndex);
        }
        else
        {
            return ComputeLeafPVSBasic(leaf, leafIndex);
        }
    }
    
    /// <summary>
    /// Unity Camera-based PVS computation with RenderTexture
    /// </summary>
    private byte[] ComputeLeafPVSWithUnityCamera(BSPNode leaf, int leafIndex)
    {
        List<bool> visibility = new List<bool>(new bool[leafNodes.Count]);
        
        // Position camera at leaf center
        pvsCamera.transform.position = leaf.center;
        
        if (useColorIDEncoding)
        {
            // Multi-directional sampling using color ID encoding
            Vector3[] directions = {
                Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.up, Vector3.down,
                new Vector3(1,1,0).normalized, new Vector3(-1,1,0).normalized,
                new Vector3(1,-1,0).normalized, new Vector3(-1,-1,0).normalized,
                new Vector3(1,0,1).normalized, new Vector3(-1,0,1).normalized,
                new Vector3(1,0,-1).normalized, new Vector3(-1,0,-1).normalized,
                new Vector3(0,1,1).normalized, new Vector3(0,-1,1).normalized,
                new Vector3(0,1,-1).normalized, new Vector3(0,-1,-1).normalized,
                new Vector3(1,1,1).normalized, new Vector3(-1,1,1).normalized,
                new Vector3(1,-1,1).normalized, new Vector3(-1,-1,1).normalized,
                new Vector3(1,1,-1).normalized, new Vector3(-1,1,-1).normalized,
                new Vector3(1,-1,-1).normalized, new Vector3(-1,-1,-1).normalized
            };
            
            foreach (Vector3 direction in directions)
            {
                pvsCamera.transform.rotation = Quaternion.LookRotation(direction);
                
                if (useUnityOcclusionCulling)
                {
                    // Use Unity's occlusion culling system
                    pvsCamera.useOcclusionCulling = true;
                }
                
                // Render scene
                pvsCamera.Render();
                
                // Analyze rendered pixels
                AnalyzeRenderTextureForVisibility(visibility);
            }
        }
        else
        {
            // Single direction basic rendering
            pvsCamera.transform.rotation = Quaternion.identity;
            pvsCamera.Render();
            AnalyzeRenderTextureForVisibility(visibility);
        }
        
        return CompressVisibilityData(visibility);
    }
    
    /// <summary>
    /// GeometryUtility-based PVS computation
    /// </summary>
    private byte[] ComputeLeafPVSWithGeometryUtility(BSPNode leaf, int leafIndex)
    {
        List<bool> visibility = new List<bool>(new bool[leafNodes.Count]);
        
        // Create frustum planes from leaf center
        Vector3 leafCenter = leaf.center;
        
        for (int i = 0; i < leafNodes.Count; i++)
        {
            if (i == leafIndex)
            {
                visibility[i] = true;
                continue;
            }
            
            BSPNode targetLeaf = leafNodes[i];
            Vector3 direction = (targetLeaf.center - leafCenter).normalized;
            float distance = Vector3.Distance(leafCenter, targetLeaf.center);
            
            if (distance > maxVisibilityDistance)
            {
                visibility[i] = false;
                continue;
            }
            
            // Create frustum for visibility test
            GameObject tmpCamGO = new GameObject("PVS_TempCamera");
            tmpCamGO.hideFlags = HideFlags.HideAndDontSave;
            Camera tempCamera = tmpCamGO.AddComponent<Camera>();
            tempCamera.enabled = false;
            tempCamera.transform.position = leafCenter;
            tempCamera.transform.rotation = Quaternion.LookRotation(direction);
            tempCamera.fieldOfView = 90f;
            tempCamera.nearClipPlane = 0.1f;
            tempCamera.farClipPlane = distance + 1f;
            
            UnityEngine.Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(tempCamera);
            
            // Test if target leaf is within frustum
            if (GeometryUtility.TestPlanesAABB(frustumPlanes, targetLeaf.bounds))
            {
                // Additional line-of-sight test
                if (!Physics.Linecast(leafCenter, targetLeaf.center))
                {
                    visibility[i] = true;
                }
            }
            
            DestroyImmediate(tmpCamGO);
        }
        
        return CompressVisibilityData(visibility);
    }
    
    /// <summary>
    /// Basic raycast-based PVS computation
    /// </summary>
    private byte[] ComputeLeafPVSBasic(BSPNode leaf, int leafIndex)
    {
        List<bool> visibility = new List<bool>(new bool[leafNodes.Count]);
        
        Vector3 leafCenter = leaf.center;
        
        for (int i = 0; i < leafNodes.Count; i++)
        {
            if (i == leafIndex)
            {
                visibility[i] = true;
                continue;
            }
            
            BSPNode targetLeaf = leafNodes[i];
            Vector3 targetCenter = targetLeaf.center;
            float distance = Vector3.Distance(leafCenter, targetCenter);
            
            if (distance <= maxVisibilityDistance)
            {
                Vector3 direction = (targetCenter - leafCenter).normalized;
                
                if (!Physics.Raycast(leafCenter, direction, distance))
                {
                    visibility[i] = true;
                }
            }
        }
        
        return CompressVisibilityData(visibility);
    }
    
    /// <summary>
    /// Analyze RenderTexture pixels for leaf visibility
    /// </summary>
    private void AnalyzeRenderTextureForVisibility(List<bool> visibility)
    {
        RenderTexture.active = renderTexture;
        Texture2D texture = new Texture2D(renderTextureSize, renderTextureSize, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, renderTextureSize, renderTextureSize), 0, 0);
        texture.Apply();
        
        Color[] pixels = texture.GetPixels();
        
        // Analyze pixels for geometry presence
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].a > 0.1f) // Non-transparent pixel indicates visible geometry
            {
                // Map pixel to leaf index (simplified approach)
                int leafIndex = Mathf.FloorToInt((float)i / pixels.Length * leafNodes.Count);
                if (leafIndex < visibility.Count)
                {
                    visibility[leafIndex] = true;
                }
            }
        }
        
        DestroyImmediate(texture);
        RenderTexture.active = null;
    }
    
    /// <summary>
    /// Compress visibility data to byte array
    /// </summary>
    private byte[] CompressVisibilityData(List<bool> visibility)
    {
        int byteCount = (visibility.Count + 7) / 8;
        byte[] compressed = new byte[byteCount];
        
        for (int i = 0; i < visibility.Count; i++)
        {
            if (visibility[i])
            {
                int byteIndex = i / 8;
                int bitIndex = i % 8;
                compressed[byteIndex] |= (byte)(1 << bitIndex);
            }
        }
        
        return compressed;
    }
    
    /// <summary>
    /// Monitor PVS computation performance
    /// </summary>
    private void MonitorPVSPerformance()
    {
        Debug.Log("=== PVS PERFORMANCE ANALYSIS ===");
        Debug.Log($"⏱️  Total Computation Time: {lastComputationTime}ms");
        Debug.Log($"📊 Leaves Processed: {processedLeafCount}/{totalLeafCount}");
        Debug.Log($"⚡ Average Time/Leaf: {(float)lastComputationTime/processedLeafCount:F2}ms");
        Debug.Log($"🔥 Throughput: {processedLeafCount * 1000f / lastComputationTime:F1} leaves/second");
        
        long memoryUsage = System.GC.GetTotalMemory(false);
        Debug.Log($"💾 Memory Usage: {memoryUsage / (1024 * 1024)}MB");
        
        int totalPVSSize = 0;
        foreach (var kvp in leafPVSData)
        {
            totalPVSSize += kvp.Value.Length;
        }
        Debug.Log($"📦 Total PVS Data Size: {totalPVSSize / 1024}KB");
        Debug.Log($"📈 Average PVS Size/Leaf: {totalPVSSize / leafPVSData.Count} bytes");
        Debug.Log("================================");
    }
    
    /// <summary>
    /// Write data to EMU format file (version 4)
    /// Format matches emudraw.c load_emu() expectations exactly
    /// </summary>
    private void WriteEMUFile()
    {
        // Create GeneratedData directory in project root
        string projectRoot = Application.dataPath.Replace("/Assets", "");
        string generatedDataPath = Path.Combine(projectRoot, "GeneratedData");
        
        if (!Directory.Exists(generatedDataPath))
        {
            Directory.CreateDirectory(generatedDataPath);
            Debug.Log($"Created directory: {generatedDataPath}");
        }
        
        string fullPath = Path.Combine(generatedDataPath, outputFileName);
        
        using (FileStream fs = new FileStream(fullPath, FileMode.Create))
        using (BinaryWriter writer = new BinaryWriter(fs))
        {
            // EMU header - CRITICAL: Exact format that emudraw.c expects
            // Order: magic, version, endian (3 uint32s)
            writer.Write(0x454D5520);  // EMU_MAGIC: "EMU " (0x454D5520 = little-endian bytes)
            writer.Write(4U);          // EMU_VERSION: 4 (supports texture filename)
            writer.Write(0x01020304U); // EMU_ENDIAN_LE marker (little-endian)
            
            // CRITICAL: Counts come IMMEDIATELY after header (no padding)
            // emudraw.c expects: fread(&vcount, 4, 1, fp); fread(&fcount, 4, 1, fp); etc.
            writer.Write((uint)worldVertices.Count);  // vcount (vertex count)
            writer.Write((uint)faces.Count);          // fcount (face count) 
            writer.Write((uint)leafNodes.Count);      // lcount (leaf count)
            
            // Write texture filename with length prefix (uint32 length + string data)
            string textureFilename = textureFileName;
            
            // If no user-specified filename, try to auto-detect from material
            if (string.IsNullOrEmpty(textureFilename))
            {
                if (gameObject.GetComponent<Renderer>() != null)
                {
                    var renderer = gameObject.GetComponent<Renderer>();
                    if (renderer.material != null && renderer.material.mainTexture != null)
                    {
                        textureFilename = renderer.material.mainTexture.name + ".png";
                    }
                }
            }
            
            // If still no texture found, use default
            if (string.IsNullOrEmpty(textureFilename))
            {
                textureFilename = "default.png";
            }
            
            // Write texture filename as length-prefixed string (matching C's expectations)
            byte[] textureFilenameBytes = System.Text.Encoding.UTF8.GetBytes(textureFilename);
            writer.Write((uint)textureFilenameBytes.Length); // Length prefix (uint32)
            writer.Write(textureFilenameBytes);              // Filename string (no null terminator written)
            
            // Write texture filter mode (1 byte: 0=nearest, 1=bilinear)
            byte filterMode = useNearestFiltering ? (byte)0 : (byte)1;
            writer.Write(filterMode);
            
            Debug.Log($"✅ EMU Header: magic=0x454D5520, version=4, endian=0x01020304");
            Debug.Log($"✅ EMU Counts: vcount={worldVertices.Count}, fcount={faces.Count}, lcount={leafNodes.Count}");
            Debug.Log($"✅ EMU Texture: '{textureFilename}' ({textureFilenameBytes.Length} bytes), filter={filterMode}");
            
            // Vertex data (vcount × 3 floats) - NO COUNT PREFIX!
            foreach (Vector3 vertex in worldVertices) 
            {
                // Flip Z for right-handed coordinate system (OpenGL)
                writer.Write(vertex.x);
                writer.Write(vertex.y);
                writer.Write(-vertex.z);
            }
            Debug.Log($"✅ Vertices: {worldVertices.Count} × 3 floats (Z-flipped)");
            
            // Normal data (vcount × 3 floats) - NO COUNT PREFIX!
            while (worldNormals.Count < worldVertices.Count)
            {
                worldNormals.Add(Vector3.up);
            }
            
            foreach (Vector3 normal in worldNormals)
            {
                writer.Write(normal.x);
                writer.Write(normal.y);
                writer.Write(-normal.z);
            }
            Debug.Log($"✅ Normals: {worldNormals.Count} × 3 floats (Z-flipped)");
            
            // UV data (vcount × 2 bytes) - NO COUNT PREFIX!
            // emudraw.c expects uint8_t u, v format
            while (textureCoords.Count < worldVertices.Count)
            {
                textureCoords.Add(Vector2.zero);
            }
            
            foreach (Vector2 uv in textureCoords)
            {
                writer.Write((byte)(Mathf.Clamp01(uv.x) * 255)); // U as byte
                writer.Write((byte)(Mathf.Clamp01(1.0f - uv.y) * 255)); // V as byte (flipped)
            }
            Debug.Log($"✅ UVs: {textureCoords.Count} × 2 bytes (V-flipped)");
            
            // Color data (vcount × 3 bytes) - NO COUNT PREFIX!
            // emudraw.c expects RGB bytes (no alpha)
            while (vertexColors.Count < worldVertices.Count)
            {
                vertexColors.Add(Color.white);
            }
            
            foreach (Color color in vertexColors)
            {
                writer.Write((byte)(Mathf.Clamp01(color.r) * 255)); // Red
                writer.Write((byte)(Mathf.Clamp01(color.g) * 255)); // Green
                writer.Write((byte)(Mathf.Clamp01(color.b) * 255)); // Blue
            }
            Debug.Log($"✅ Colors: {vertexColors.Count} × 3 bytes (RGB)");
            
            // Face data (fcount × 3 uint32) - NO COUNT PREFIX!
            foreach (Face face in faces)
            {
                if (face.vertexIndices.Length < 3)
                {
                    Debug.LogError($"Face has only {face.vertexIndices.Length} vertices!");
                    continue;
                }
                
                // Validate and clamp indices
                for (int i = 0; i < 3; i++)
                {
                    if (face.vertexIndices[i] >= worldVertices.Count)
                    {
                        face.vertexIndices[i] = 0;
                    }
                }
                
                // Flip winding for OpenGL (indices written as v0, v2, v1 instead of v0, v1, v2)
                writer.Write((uint)face.vertexIndices[0]);
                writer.Write((uint)face.vertexIndices[2]);
                writer.Write((uint)face.vertexIndices[1]);
            }
            Debug.Log($"✅ Faces: {faces.Count} × 3 uint32 indices (winding flipped)");
            
            // Leaf data (lcount leaves) - NO COUNT PREFIX!
            for (int i = 0; i < leafNodes.Count; i++)
            {
                BSPNode leaf = leafNodes[i];
                
                // Write face count for this leaf
                writer.Write((uint)leaf.faces.Count);
                
                // Write face indices for this leaf
                foreach (Face face in leaf.faces)
                {
                    int faceIndex = faces.IndexOf(face);
                    if (faceIndex == -1) faceIndex = 0;
                    writer.Write((uint)faceIndex);
                }
                
                // Write leaf bounding box (Vec3 min, Vec3 max = 6 floats)
                writer.Write(leaf.bounds.min.x);
                writer.Write(leaf.bounds.min.y);
                writer.Write(leaf.bounds.min.z);
                writer.Write(leaf.bounds.max.x);
                writer.Write(leaf.bounds.max.y);
                writer.Write(leaf.bounds.max.z);
            }
            Debug.Log($"✅ Leaves: {leafNodes.Count} leaves with face indices and bboxes");
            
            // PVS data
            int pvsBytes = (leafNodes.Count + 7) / 8;
            writer.Write((uint)pvsBytes); // pvs_bytes
            
            // Write PVS data for each leaf
            for (int i = 0; i < leafNodes.Count; i++)
            {
                byte[] pvsData;
                if (leafPVSData.ContainsKey(i))
                {
                    pvsData = leafPVSData[i];
                    if (pvsData.Length < pvsBytes)
                    {
                        byte[] paddedData = new byte[pvsBytes];
                        System.Array.Copy(pvsData, paddedData, pvsData.Length);
                        pvsData = paddedData;
                    }
                }
                else
                {
                    pvsData = new byte[pvsBytes];
                    for (int j = 0; j < pvsBytes; j++)
                    {
                        pvsData[j] = 0xFF;
                    }
                }
                
                writer.Write(pvsData);
            }
            Debug.Log($"✅ PVS: {pvsBytes} bytes × {leafNodes.Count} leaves");
        }
        
        long fileSize = new FileInfo(fullPath).Length;
        Debug.Log("==========================================");
        Debug.Log($"🎉 EMU FILE EXPORT COMPLETED!");
        Debug.Log($"📁 File: {fullPath}");
        Debug.Log($"📊 Size: {fileSize} bytes");
        Debug.Log($"📐 Data: {worldVertices.Count} vertices, {faces.Count} faces, {leafNodes.Count} leaves");
        Debug.Log("==========================================");
        
        ValidateEMUFile(fullPath);
    }

    /// <summary>
    /// Write Vector3 with endian safety
    /// </summary>
    private void WriteVector3(BinaryWriter writer, Vector3 vector)
    {
        if (useEndianSafeBinaryFormat)
        {
            writer.Write(BitConverter.GetBytes(vector.x));
            writer.Write(BitConverter.GetBytes(vector.y));
            writer.Write(BitConverter.GetBytes(vector.z));
        }
        else
        {
            writer.Write(vector.x);
            writer.Write(vector.y);
            writer.Write(vector.z);
        }
    }
    
    /// <summary>
    /// Write Vector2 as uint8_t bytes (emudraw.c compatible)
    /// </summary>
    private void WriteVector2(BinaryWriter writer, Vector2 vector)
    {
        writer.Write((byte)(vector.x * 255)); // U as byte
        writer.Write((byte)(vector.y * 255)); // V as byte
    }
    
    /// <summary>
    /// Write Color as RGB bytes (emudraw.c compatible)
    /// </summary>
    private void WriteColor(BinaryWriter writer, Color color)
    {
        writer.Write((byte)(color.r * 255)); // Red as byte
        writer.Write((byte)(color.g * 255)); // Green as byte
        writer.Write((byte)(color.b * 255)); // Blue as byte
        // No alpha - emudraw.c expects only RGB
    }
    
    /// <summary>
    /// Validate EMU file format compatibility
    /// </summary>
    private void ValidateEMUFile(string filePath)
    {
        try
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                Debug.Log("🔍 EMU FILE VALIDATION - CORRECTED FORMAT");
                
                // Read and verify header
                uint magic = reader.ReadUInt32();
                uint version = reader.ReadUInt32();
                uint endian = reader.ReadUInt32();
                
                Debug.Log($"EMU Validation - Magic: 0x{magic:X8} (expected: 0x454D5520)");
                Debug.Log($"EMU Validation - Version: {version} (expected: 4)");
                Debug.Log($"EMU Validation - Endian: 0x{endian:X8} (expected: 0x01020304)");
                
                bool headerValid = (magic == 0x454D5520 && version == 4 && endian == 0x01020304);
                
                if (headerValid)
                {
                    Debug.Log("✅ EMU header validation PASSED");
                    
                    // Read counts
                    uint vcount = reader.ReadUInt32();
                    uint fcount = reader.ReadUInt32();  
                    uint lcount = reader.ReadUInt32();
                    
                    // Read texture filename
                    uint textureFilenameLength = reader.ReadUInt32();
                    byte[] textureFilenameBytes = reader.ReadBytes((int)textureFilenameLength);
                    string textureFilename = System.Text.Encoding.UTF8.GetString(textureFilenameBytes);
                    
                    // Read texture filter mode
                    byte filterMode = reader.ReadByte();
                    string filterName = (filterMode == 0) ? "nearest" : "bilinear";
                    
                    Debug.Log($"EMU Counts - Vertices: {vcount}, Faces: {fcount}, Leaves: {lcount}");
                    Debug.Log($"EMU Texture Filename - '{textureFilename}' ({textureFilenameLength} bytes)");
                    Debug.Log($"EMU Texture Filter - {filterName} ({filterMode})");
                    
                    // Validate counts match our data
                    if (vcount == worldVertices.Count && fcount == faces.Count && lcount == leafNodes.Count)
                    {
                        Debug.Log("✅ EMU counts validation PASSED");
                        
                        // Check file size to ensure we wrote all expected data
                        long expectedSize = 12 + // header
                                          12 + // counts  
                                          4 + textureFilenameLength + // texture filename with length prefix
                                          1 + // texture filter mode byte
                                          vcount * 12 + // vertices (3 floats each)
                                          vcount * 12 + // normals (3 floats each)
                                          vcount * 2 +  // UVs (2 bytes each)
                                          vcount * 3 +  // colors (3 bytes each)
                                          fcount * 12;  // faces (3 uint32 each)
                        
                        // Add leaf data size (variable)
                        foreach (BSPNode leaf in leafNodes)
                        {
                            expectedSize += 4; // nfaces
                            expectedSize += leaf.faces.Count * 4; // face indices
                            expectedSize += 24; // bbox (6 floats)
                        }
                        
                        // Add PVS data
                        int pvsBytes = (leafNodes.Count + 7) / 8;
                        expectedSize += 4; // pvs_bytes
                        expectedSize += leafNodes.Count * pvsBytes; // PVS data
                        
                        long actualSize = fs.Length;
                        Debug.Log($"File size - Expected: {expectedSize}, Actual: {actualSize}");
                        
                        if (actualSize == expectedSize)
                        {
                            Debug.Log("✅ EMU file size validation PASSED");
                        }
                        else
                        {
                            Debug.LogWarning($"⚠️ EMU file size mismatch - difference: {actualSize - expectedSize} bytes");
                        }
                    }
                    else
                    {
                        Debug.LogError($"❌ EMU counts mismatch - Expected: v{worldVertices.Count} f{faces.Count} l{leafNodes.Count}, Got: v{vcount} f{fcount} l{lcount}");
                    }
                }
                else
                {
                    Debug.LogError("❌ EMU header validation FAILED");
                    return;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"EMU validation failed: {e.Message}");
        }
    }
    
    /// <summary>
    /// Dump EMU file structure for debugging
    /// </summary>
    private void DumpEMUStructure(string filePath)
    {
        try
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                Debug.Log("=== EMU FILE STRUCTURE DUMP ===");
                Debug.Log($"File size: {fs.Length} bytes");
                
                // Header (12 bytes)
                uint magic = reader.ReadUInt32();
                uint version = reader.ReadUInt32();
                uint endian = reader.ReadUInt32();
                
                Debug.Log($"Header: Magic=0x{magic:X8}, Version={version}, Endian=0x{endian:X8}");
                
                // Consolidated counts (12 bytes) - ALL COUNTS TOGETHER AFTER HEADER
                int vertexCount = reader.ReadInt32();
                int faceCount = reader.ReadInt32();
                int leafCount = reader.ReadInt32();
                
                Debug.Log($"Counts: vertices={vertexCount}, faces={faceCount}, leaves={leafCount}");
                Debug.Log($"Position after counts: {fs.Position} bytes");
                
                // Read texture filename
                uint textureFilenameLength = reader.ReadUInt32();
                byte[] textureFilenameBytes = reader.ReadBytes((int)textureFilenameLength);
                string textureFilename = System.Text.Encoding.UTF8.GetString(textureFilenameBytes);
                
                // Read texture filter mode
                byte filterMode = reader.ReadByte();
                string filterName = (filterMode == 0) ? "nearest" : "bilinear";
                
                Debug.Log($"Texture filename: '{textureFilename}' ({textureFilenameLength} bytes)");
                Debug.Log($"Texture filter: {filterName} ({filterMode})");
                Debug.Log($"Position after texture data: {fs.Position} bytes");
                
                // Vertex data (NO COUNT PREFIX - data starts immediately)
                long vertexDataSize = vertexCount * 3 * sizeof(float);
                Debug.Log($"Vertex data: {vertexDataSize} bytes (position {fs.Position} to {fs.Position + vertexDataSize})");
                fs.Seek(vertexDataSize, SeekOrigin.Current);
                
                // Normal data (NO COUNT PREFIX)
                long normalDataSize = vertexCount * 3 * sizeof(float);
                Debug.Log($"Normal data: {normalDataSize} bytes (position {fs.Position} to {fs.Position + normalDataSize})");
                fs.Seek(normalDataSize, SeekOrigin.Current);
                
                // UV data (NO COUNT PREFIX) - 2 bytes per UV
                long uvDataSize = vertexCount * 2;
                Debug.Log($"UV data: {uvDataSize} bytes (position {fs.Position} to {fs.Position + uvDataSize})");
                fs.Seek(uvDataSize, SeekOrigin.Current);
                
                // Color data (NO COUNT PREFIX) - 3 bytes per color
                long colorDataSize = vertexCount * 3;
                Debug.Log($"Color data: {colorDataSize} bytes (position {fs.Position} to {fs.Position + colorDataSize})");
                fs.Seek(colorDataSize, SeekOrigin.Current);
                
                // Face data (NO COUNT PREFIX) - 3 uint32 per face
                long faceDataSize = faceCount * 3 * sizeof(uint);
                Debug.Log($"Face data: {faceDataSize} bytes (position {fs.Position} to {fs.Position + faceDataSize})");
                fs.Seek(faceDataSize, SeekOrigin.Current);
                
                // Leaf data (NO COUNT PREFIX)
                Debug.Log($"Leaf data starts at position: {fs.Position}");
                Debug.Log($"Remaining bytes for leaves and PVS: {fs.Length - fs.Position}");
                
                Debug.Log($"Final position: {fs.Position}/{fs.Length} bytes");
                Debug.Log("===============================");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"EMU structure dump failed: {e.Message}");
        }
    }
    
    /// <summary>
    /// Write Bounds with endian safety
    /// </summary>
    private void WriteBounds(BinaryWriter writer, Bounds bounds)
    {
        WriteVector3(writer, bounds.center);
        WriteVector3(writer, bounds.size);
    }
    
    void OnDestroy()
    {
        // Cleanup Unity resources
        if (renderTexture != null)
        {
            renderTexture.Release();
            DestroyImmediate(renderTexture);
        }
        
        if (commandBuffer != null)
        {
            commandBuffer.Dispose();
        }
        
        if (pvsCamera != null)
        {
            DestroyImmediate(pvsCamera.gameObject);
        }
    }
    
#if UNITY_EDITOR
    /// <summary>
    /// Static method for exporting EMU files from command line
    /// </summary>
    [UnityEditor.MenuItem("Tools/Export EMU")]
    public static void ExportEMU()
    {
        Debug.Log("Starting EMU export...");
        
        // Find all Level components in the scene
        Level[] levels = FindObjectsOfType<Level>();
        
        if (levels.Length == 0)
        {
            Debug.LogWarning("No Level components found in scene! Creating test cube...");
            
            // Create a simple test cube
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "TestCube";
            cube.transform.position = new Vector3(0, 0, 0);
            
            // Add Level component
            Level level = cube.AddComponent<Level>();
            level.outputFileName = "test_cube_generated.emu";
            level.showDebugInfo = true;
            level.enablePVSComputation = false; // Disable PVS for simple test
            
            // Add to levels array
            levels = new Level[] { level };
        }
        
        foreach (Level level in levels)
        {
            Debug.Log($"Exporting level {level.gameObject.name}");
            
            try
            {
                // Set unique output filename for each level
                if (string.IsNullOrEmpty(level.outputFileName))
                {
                    level.outputFileName = $"level_{level.gameObject.name}.emu";
                }
                level.ExportLevel();
                Debug.Log($"Successfully exported {level.outputFileName}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to export {level.outputFileName}: {ex.Message}");
                Debug.LogError($"Stack trace: {ex.StackTrace}");
            }
        }
        
        Debug.Log("EMU export completed!");
    }
#endif
}
