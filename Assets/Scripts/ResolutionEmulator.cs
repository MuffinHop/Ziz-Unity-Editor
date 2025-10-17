using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
public class ResolutionEmulator : MonoBehaviour
{
    public enum EmulatedResolution
    {
        None,
        Res256x240,    // NES-like
        Res320x240,    // QVGA
        Res640x240,    // Wide QVGA
        Res320x480,    // HVGA
        Res640x480,    // VGA
        Res480x272     // PSP-like
    }

    [Header("Resolution Settings")]
    public EmulatedResolution resolution = EmulatedResolution.None;
    public FilterMode filterMode = FilterMode.Point;

    private Camera _camera;
    private RenderTexture _renderTarget;
    private int _currentWidth;
    private int _currentHeight;

    // Debug flag
    private bool _debugLogging = true;

    private void OnEnable()
    {
        _camera = GetComponent<Camera>();
        UpdateResolution();
    }

    private void OnDisable()
    {
        CleanupRenderTarget();
    }

    private void Update()
    {
        // Check if we need to update resolution
        if (ShouldUpdateResolution())
        {
            UpdateResolution();
        }
    }

    private bool ShouldUpdateResolution()
    {
        int width, height;
        GetTargetDimensions(out width, out height);
        return width != _currentWidth || height != _currentHeight;
    }

    private void GetTargetDimensions(out int width, out int height)
    {
        switch (resolution)
        {
            case EmulatedResolution.Res256x240:
                width = 256;
                height = 240;
                break;
            case EmulatedResolution.Res320x240:
                width = 320;
                height = 240;
                break;
            case EmulatedResolution.Res640x240:
                width = 640;
                height = 240;
                break;
            case EmulatedResolution.Res320x480:
                width = 320;
                height = 480;
                break;
            case EmulatedResolution.Res640x480:
                width = 640;
                height = 480;
                break;
            case EmulatedResolution.Res480x272:
                width = 480;
                height = 272;
                break;
            default:
                width = Screen.width;
                height = Screen.height;
                break;
        }
    }

    private void CleanupRenderTarget()
    {
        if (_debugLogging) Debug.Log("Cleaning up render targets");

        if (_camera != null)
        {
            if (_debugLogging) Debug.Log($"Camera target texture was: {(_camera.targetTexture != null ? _camera.targetTexture.name : "null")}");
            _camera.targetTexture = null;
        }

        if (_renderTarget != null)
        {
            _renderTarget.Release();
            DestroyImmediate(_renderTarget);
            _renderTarget = null;
        }

        _currentWidth = 0;
        _currentHeight = 0;
    }

    private void UpdateResolution()
    {
        if (_camera == null)
        {
            if (_debugLogging) Debug.LogError("No camera found!");
            return;
        }

        if (_debugLogging) Debug.Log($"Updating resolution to: {resolution}");

        // Clean up old render target if it exists
        CleanupRenderTarget();

        // If no emulation, we're done
        if (resolution == EmulatedResolution.None)
        {
            if (_debugLogging) Debug.Log("No emulation, using default camera settings");
            return;
        }

        // Get new dimensions
        GetTargetDimensions(out _currentWidth, out _currentHeight);
        if (_debugLogging) Debug.Log($"New dimensions: {_currentWidth}x{_currentHeight}");

        // Create new render target (no depth, we'll blit from source)
        _renderTarget = new RenderTexture(_currentWidth, _currentHeight, 0, RenderTextureFormat.ARGB32)
        {
            filterMode = filterMode,
            name = $"EmulatedRT_{_currentWidth}x{_currentHeight}"
        };
        _renderTarget.Create();

        if (_debugLogging) Debug.Log($"Created render target: {_renderTarget.name}");

        // Do NOT assign to camera.targetTexture to avoid stealing display; we will blit in OnRenderImage
    }

    private void OnValidate()
    {
        if (enabled)
        {
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
            }
            UpdateResolution();
        }
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (resolution == EmulatedResolution.None || _renderTarget == null)
        {
            if (source != null)
            {
                Graphics.Blit(source, destination);
            }
            else
            {
                // Only clear if nothing to blit at all
                RenderTexture.active = destination;
                GL.Clear(true, true, Color.black);
                RenderTexture.active = null;
            }
            return;
        }

        // Downsample the camera's source into our low-res render target
        Graphics.Blit(source, _renderTarget);
        // Upscale the low-res target back to the destination (filterMode on the RT controls bilinear/point)
        Graphics.Blit(_renderTarget, destination);
    }
}
