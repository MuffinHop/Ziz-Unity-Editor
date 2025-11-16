using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class ResolutionEmulatorFeature : ScriptableRendererFeature
{
    public enum EmulatedResolution
    {
        None,
        Res256x240,
        Res320x240,
        Res640x240,
        Res320x480,
        Res640x480,
        Res480x272
    }

    [System.Serializable]
    public class Settings
    {
        public EmulatedResolution resolution = EmulatedResolution.None;
        public FilterMode filterMode = FilterMode.Point;
    }

    [SerializeField]
    public Settings settings = new Settings();
    
    private ResolutionEmulatorPass _pass;

    public override void Create()
    {
        _pass = new ResolutionEmulatorPass(settings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.resolution == EmulatedResolution.None)
            return;

        _pass.Setup(renderer, settings);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        if (_pass != null)
        {
            _pass.Dispose();
        }
        base.Dispose(disposing);
    }
}
