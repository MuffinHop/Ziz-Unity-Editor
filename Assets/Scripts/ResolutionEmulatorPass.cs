using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class ResolutionEmulatorPass : ScriptableRenderPass
{
    private int _currentWidth;
    private int _currentHeight;
    private ResolutionEmulatorFeature.Settings _settings;
    private Material _emulatorMaterial;

    public ResolutionEmulatorPass(ResolutionEmulatorFeature.Settings settings)
    {
        _settings = settings;
        renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    public void Setup(ScriptableRenderer renderer, ResolutionEmulatorFeature.Settings settings)
    {
        _settings = settings;
    }

    private void GetTargetDimensions(out int width, out int height)
    {
        switch (_settings.resolution)
        {
            case ResolutionEmulatorFeature.EmulatedResolution.Res256x240:
                width = 256;
                height = 240;
                break;
            case ResolutionEmulatorFeature.EmulatedResolution.Res320x240:
                width = 320;
                height = 240;
                break;
            case ResolutionEmulatorFeature.EmulatedResolution.Res640x240:
                width = 640;
                height = 240;
                break;
            case ResolutionEmulatorFeature.EmulatedResolution.Res320x480:
                width = 320;
                height = 480;
                break;
            case ResolutionEmulatorFeature.EmulatedResolution.Res640x480:
                width = 640;
                height = 480;
                break;
            case ResolutionEmulatorFeature.EmulatedResolution.Res480x272:
                width = 480;
                height = 272;
                break;
            default:
                width = Screen.width;
                height = Screen.height;
                break;
        }
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (_settings.resolution == ResolutionEmulatorFeature.EmulatedResolution.None)
            return;

        if (_emulatorMaterial == null)
        {
            _emulatorMaterial = new Material(Shader.Find("Hidden/ResolutionEmulator"));
        }

        GetTargetDimensions(out int targetWidth, out int targetHeight);
        
        // Set shader parameters
        _emulatorMaterial.SetVector("_SourceResolution", new Vector4(Screen.width, Screen.height, 0, 0));
        _emulatorMaterial.SetVector("_TargetResolution", new Vector4(targetWidth, targetHeight, 0, 0));

        CommandBuffer cmd = CommandBufferPool.Get("Resolution Emulator");

        // Get the current camera output (RenderTargetIdentifier). In some URP versions
        // cameraColorTargetHandle can be null or invalid; guard and use a temporary RT instead
        var renderer = renderingData.cameraData.renderer;
        if (renderer == null)
        {
            CommandBufferPool.Release(cmd);
            return;
        }

        // Use cameraColorTargetHandle when available. Guard against null RTHandle to avoid the RTHandle op_Implicit assertion.
        var camColorHandle = renderer.cameraColorTargetHandle;
        if (camColorHandle == null || camColorHandle.rt == null)
        {
            CommandBufferPool.Release(cmd);
            return;
        }

        RenderTargetIdentifier source = camColorHandle;

        // Use a temporary RT to avoid reading and writing the same target (can cause asserts on some platforms)
        int tempId = Shader.PropertyToID("_TempResolutionEmu");
        cmd.GetTemporaryRT(tempId, Screen.width, Screen.height, 0, FilterMode.Bilinear, RenderTextureFormat.Default);

        // Blit source -> temp using emulator (downsample/up sample happens in shader)
        cmd.Blit(source, tempId, _emulatorMaterial);
        // Blit temp -> source to write back
        cmd.Blit(tempId, source);

        // Release temp
        cmd.ReleaseTemporaryRT(tempId);

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public void Dispose()
    {
        if (_emulatorMaterial != null)
        {
            Object.DestroyImmediate(_emulatorMaterial);
            _emulatorMaterial = null;
        }
    }
}
