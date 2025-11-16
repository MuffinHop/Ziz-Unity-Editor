using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Universal animation recorder - handles ALL animation types:
/// - Skinned mesh (RatRecorder functionality)
/// - Procedural shapes (Shape functionality)
/// - SDF shapes (SDFShape functionality)
/// - Particle systems (SDFParticleRecorder functionality)
/// - Static meshes (Actor functionality)
/// 
/// Automatically detects what to record based on attached components.
/// </summary>
[DisallowMultipleComponent]
public class AnimationRecorder : MonoBehaviour
{
    [Header("Recording Settings")]
    public float captureFramerate = 30f;
    public float recordingDuration = 5f;
    public bool autoRecord = true;
    public int maxFileSizeKB = 64;
    
    [Header("Rendering")]
    public Rat.ActorRenderingMode renderingMode = Rat.ActorRenderingMode.TextureWithDirectionalLight;
    
    // Auto-detected at runtime
    private enum RecorderType { SkinnedMesh, StaticMesh, SDFShape, ParticleSystem, Unknown }
    private RecorderType _type;
    
    private List<Vector3[]> _frames = new List<Vector3[]>();
    private List<Rat.ActorTransformFloat> _frameTransforms = new List<Rat.ActorTransformFloat>();
    private Mesh _sourceMesh;
    private bool _isRecording;
    private float _startTime;
    
    void Start()
    {
        DetectType();
        if (autoRecord && Application.isPlaying)
        {
            StartRecording();
        }
    }
    
    void DetectType()
    {
        if (GetComponent<SkinnedMeshRenderer>()) _type = RecorderType.SkinnedMesh;
        else if (GetComponent<SDFShape>()) _type = RecorderType.SDFShape;
        else if (GetComponent<ParticleSystem>()) _type = RecorderType.ParticleSystem;
        else if (GetComponent<MeshFilter>()) _type = RecorderType.StaticMesh;
        else _type = RecorderType.Unknown;
        
        Debug.Log($"AnimationRecorder: Detected type {_type} on '{name}'");
    }
    
    public void StartRecording()
    {
        _isRecording = true;
        _startTime = Time.time;
        _frames.Clear();
        _frameTransforms.Clear();
    }
    
    void LateUpdate()
    {
        if (!_isRecording) return;
        
        float elapsed = Time.time - _startTime;
        if (elapsed >= recordingDuration)
        {
            StopRecording();
            return;
        }
        
        float captureInterval = 1f / captureFramerate;
        if (_frames.Count == 0 || elapsed >= _frames.Count * captureInterval)
        {
            CaptureFrame();
        }
    }
    
    void CaptureFrame()
    {
        switch (_type)
        {
            case RecorderType.SkinnedMesh:
                CaptureSkinnedMeshFrame();
                break;
            case RecorderType.SDFShape:
                CaptureSDFShapeFrame();
                break;
            case RecorderType.ParticleSystem:
                CaptureParticleFrame();
                break;
            case RecorderType.StaticMesh:
                CaptureStaticMeshFrame();
                break;
        }
    }
    
    void CaptureSkinnedMeshFrame()
    {
        var smr = GetComponent<SkinnedMeshRenderer>();
        if (_sourceMesh == null) _sourceMesh = smr.sharedMesh;
        
        var tempMesh = new Mesh();
        smr.BakeMesh(tempMesh);
        _frames.Add(tempMesh.vertices);
        // Capture transform for this frame to preserve transform motion during playback
            _frameTransforms.Add(new Rat.ActorTransformFloat
            {
                position = transform.position,
                rotation = transform.eulerAngles,
                scale = transform.lossyScale,
                rat_file_index = 0,
                rat_local_frame = (uint)(_frames.Count - 1)
            });
        Destroy(tempMesh);
    }
    
    void CaptureSDFShapeFrame()
    {
        var sdf = GetComponent<SDFShape>();
        var mf = GetComponent<MeshFilter>();
        if (_sourceMesh == null) _sourceMesh = mf.sharedMesh;
        
        // Keep vertices in local space and let ExportAnimation bake transforms
        var verts = _sourceMesh.vertices.ToArray();
        _frames.Add(verts);
    _frameTransforms.Add(new Rat.ActorTransformFloat { position = transform.position, rotation = transform.eulerAngles, scale = transform.lossyScale, rat_file_index = 0, rat_local_frame = (uint)(_frames.Count-1) });
    }
    
    void CaptureParticleFrame()
    {
        var ps = GetComponent<ParticleSystem>();
        var particles = new ParticleSystem.Particle[ps.main.maxParticles];
        int count = ps.GetParticles(particles);
        
        // Generate quad mesh for each particle
        List<Vector3> frameVerts = new List<Vector3>();
            var simulationSpace = ps.main.simulationSpace;
            for (int i = 0; i < count; i++)
        {
            var p = particles[i];
                var size = p.GetCurrentSize(ps) * 0.5f;
                var pos = simulationSpace == ParticleSystemSimulationSpace.World ? transform.InverseTransformPoint(p.position) : p.position;
            
            frameVerts.Add(pos + new Vector3(-size, -size, 0));
            frameVerts.Add(pos + new Vector3(size, -size, 0));
            frameVerts.Add(pos + new Vector3(-size, size, 0));
            frameVerts.Add(pos + new Vector3(size, size, 0));
        }
        
        _frames.Add(frameVerts.ToArray());
    _frameTransforms.Add(new Rat.ActorTransformFloat { position = transform.position, rotation = transform.eulerAngles, scale = transform.lossyScale, rat_file_index = 0, rat_local_frame = (uint)(_frames.Count-1) });
    }
    
    void CaptureStaticMeshFrame()
    {
        var mf = GetComponent<MeshFilter>();
        if (_sourceMesh == null) _sourceMesh = mf.mesh;
        _frames.Add(_sourceMesh.vertices);
    _frameTransforms.Add(new Rat.ActorTransformFloat { position = transform.position, rotation = transform.eulerAngles, scale = transform.lossyScale, rat_file_index = 0, rat_local_frame = (uint)(_frames.Count-1) });
    }
    
    void StopRecording()
    {
        _isRecording = false;
        
        if (_frames.Count == 0)
        {
            Debug.LogWarning("No frames recorded");
            return;
        }
        
        // Use recorded transform data captured per frame during recording
        List<Rat.ActorTransformFloat> frameTransforms = new List<Rat.ActorTransformFloat>(_frameTransforms);

        // Export using unified API with transforms to be baked
        Rat.Tool.ExportAnimation(
            gameObject.name,
            _frames,
            _sourceMesh ?? new Mesh { vertices = _frames[0] },
            null,
            null,
            captureFramerate,
            $"assets/{gameObject.name}.png",
            maxFileSizeKB,
            Rat.ActorRenderingMode.TextureWithDirectionalLight,
            frameTransforms  // Pass the transforms
        );
        
        Debug.Log($"Exported {_frames.Count} frames for '{name}' with transforms baked into vertices");
    }
    
#if UNITY_EDITOR
    void OnEnable()
    {
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeExit;
    }
    
    void OnDisable()
    {
        UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeExit;
    }
    
    void OnPlayModeExit(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode && _isRecording)
        {
            StopRecording();
        }
    }
#endif
}
