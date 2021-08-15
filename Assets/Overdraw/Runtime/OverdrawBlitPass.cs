using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SceneProfiler.Overdraw.Runtime
{
    public class OverdrawBlitPass : ScriptableRenderPass
    {
        private RenderTargetHandle _source;
        private RenderTargetHandle _dest;

        public OverdrawBlitPass(RenderTargetHandle source, RenderTargetHandle dest)
        {
            _source = source;
            _dest = dest;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get("Blit");
            ProfilingSampler profilingSampler = new ProfilingSampler("Overdraw Blit Pass");
            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.Blit(_source.Identifier(), _dest.Identifier(), Vector2.one, Vector2.zero);
            }
            
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}