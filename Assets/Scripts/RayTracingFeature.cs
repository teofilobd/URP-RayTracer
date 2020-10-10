using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class RayTracingFeature : ScriptableRendererFeature
{
    class RayTracingRenderPass : ScriptableRenderPass
    {
        RayTracingMaster rayTracingMaster;

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            if(rayTracingMaster == null)
            {
                rayTracingMaster = FindObjectOfType<RayTracingMaster>();
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!Application.isPlaying)
                return;

            CommandBuffer cmd = CommandBufferPool.Get("RayTracing Pass");

            rayTracingMaster.Render(cmd);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
        }
    }

    RayTracingRenderPass m_RTPass;

    public override void Create()
    {
        m_RTPass = new RayTracingRenderPass();

        m_RTPass.renderPassEvent = RenderPassEvent.AfterRendering;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_RTPass);
    }
}


