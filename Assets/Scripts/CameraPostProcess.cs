using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class CameraPostProcess : ScriptableRendererFeature
{
    class CustomRenderPass : ScriptableRenderPass
    {
        public Material material;
        RTHandle source;
        RTHandle tempTexture;

        public void Setup(RTHandle src)
        {
            source = src;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null) return;
            CommandBuffer cmd = CommandBufferPool.Get("CameraEffectBlit");
            RenderTextureDescriptor opaqueDesc = renderingData.cameraData.cameraTargetDescriptor;
            if (tempTexture == null || tempTexture.rt.width != opaqueDesc.width || tempTexture.rt.height != opaqueDesc.height)
            {
                tempTexture?.Release();
                tempTexture = RTHandles.Alloc(opaqueDesc.width, opaqueDesc.height, colorFormat: opaqueDesc.graphicsFormat);
            }
            Blit(cmd, source, tempTexture, material, 0);
            Blit(cmd, tempTexture, source);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }§

    public Material material;
    CustomRenderPass pass;

    public override void Create()
    {
        pass = new CustomRenderPass { material = material };
        pass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
    {
        pass.Setup(renderer.cameraColorTargetHandle);
        renderer.EnqueuePass(pass);
    }
}
