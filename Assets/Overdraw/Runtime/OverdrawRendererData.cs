using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Rendering.Universal;
#endif

namespace SceneProfiler.Overdraw.Runtime
{
    [CreateAssetMenu(fileName = "OverdrawRendererData", menuName = "Rendering/Overdraw Renderer Data", order = 2)]
    public class OverdrawRendererData : ScriptableRendererData
    {
        [SerializeField] private Material _overdrawOpaque;
        [SerializeField] private Material _overdrawTransparent;

        public Material OverdrawOpaque => _overdrawOpaque;
        public Material OverdrawTransparent => _overdrawTransparent;
        
        protected override ScriptableRenderer Create()
        {
            return new OverdrawRenderer(this);
        }
    }

    public class OverdrawRenderer : ScriptableRenderer
    {
        private OverdrawRenderPass _overdrawOpaqueRenderPass;
        private OverdrawRenderPass _overdrawTransparentRenderPass;
        private OverdrawCalculatorPass _overdrawCalculatorPass;
        private OverdrawBlitPass _overdrawBlitPass;
        
        private RenderTargetHandle _overdrawAttachment;

        private RenderTargetHandle _activeCameraColorAttachment;
        private RenderTargetHandle _activeCameraDepthAttachment;
        
        private int _depthStencilBufferBits = 32;

        public OverdrawRenderer(ScriptableRendererData data) : base(data)
        {
            _overdrawAttachment.Init("_OverdrawAttachment");

            _overdrawOpaqueRenderPass = new OverdrawRenderPass(
                SortingCriteria.CommonOpaque, RenderQueueRange.opaque,
                (data as OverdrawRendererData).OverdrawOpaque);
            _overdrawTransparentRenderPass = new OverdrawRenderPass(
                SortingCriteria.CommonTransparent, RenderQueueRange.transparent,
                (data as OverdrawRendererData).OverdrawTransparent);
            _overdrawCalculatorPass = new OverdrawCalculatorPass(_overdrawAttachment);
            _overdrawBlitPass = new OverdrawBlitPass(_overdrawAttachment, RenderTargetHandle.CameraTarget);
        }

        public override void Setup(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            _activeCameraColorAttachment = _overdrawAttachment;
            _activeCameraDepthAttachment = RenderTargetHandle.CameraTarget;
            CreateCameraRenderTarget(context, ref renderingData.cameraData.cameraTargetDescriptor);
            ConfigureCameraTarget(_activeCameraColorAttachment.Identifier(), _activeCameraDepthAttachment.Identifier());
            SetRenderTarget(context, ref renderingData.cameraData);
            
            EnqueuePass(_overdrawOpaqueRenderPass);
            EnqueuePass(_overdrawTransparentRenderPass);
            EnqueuePass(_overdrawCalculatorPass);
            EnqueuePass(_overdrawBlitPass);
        }
        
        public override void FinishRendering(CommandBuffer cmd)
        {
            if (_activeCameraColorAttachment != RenderTargetHandle.CameraTarget)
            {
                cmd.ReleaseTemporaryRT(_activeCameraColorAttachment.id);
                _activeCameraColorAttachment = RenderTargetHandle.CameraTarget;
            }
            
            if (_activeCameraDepthAttachment != RenderTargetHandle.CameraTarget)
            {
                cmd.ReleaseTemporaryRT(_activeCameraDepthAttachment.id);
                _activeCameraDepthAttachment = RenderTargetHandle.CameraTarget;
            }
        }

        private void CreateCameraRenderTarget(ScriptableRenderContext context, ref RenderTextureDescriptor descriptor)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            ProfilingSampler profilingSampler = new ProfilingSampler("Create Camera Render Target");
            using (new ProfilingScope(cmd, profilingSampler))
            {
                bool useDepthRenderBuffer = _activeCameraDepthAttachment == RenderTargetHandle.CameraTarget;
                var colorDescriptor = descriptor;
                colorDescriptor.useMipMap = false;
                colorDescriptor.autoGenerateMips = false;
                colorDescriptor.depthBufferBits = (useDepthRenderBuffer) ? _depthStencilBufferBits : 0;
                cmd.GetTemporaryRT(_activeCameraColorAttachment.id, colorDescriptor, FilterMode.Bilinear);

                var depthDescriptor = descriptor;
                depthDescriptor.useMipMap = false;
                depthDescriptor.autoGenerateMips = false;
                depthDescriptor.colorFormat = RenderTextureFormat.Depth;
                depthDescriptor.depthBufferBits = _depthStencilBufferBits;
                cmd.GetTemporaryRT(_activeCameraDepthAttachment.id, depthDescriptor, FilterMode.Point);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void SetRenderTarget(ScriptableRenderContext context, ref CameraData cameraData)
        {
            CommandBuffer cmd = CommandBufferPool.Get("Set Render Target");
            CoreUtils.SetRenderTarget(cmd, _activeCameraColorAttachment.Identifier(), RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare,
                _activeCameraDepthAttachment.Identifier(), RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare, ClearFlag.All, Color.black);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public float GetOverdrawRatio()
        {
            if (_overdrawCalculatorPass != null)
            {
                return _overdrawCalculatorPass.GetOverdrawRatio();
            }

            return 1.0f;
        }

        protected override void Dispose(bool disposing) {
            _overdrawCalculatorPass?.Dispose();
            base.Dispose(disposing);
        }
    }
    
    public class OverdrawRenderPass : ScriptableRenderPass
    {
        private SortingCriteria _criteria;
        private Material _overdrawMaterial;
        private FilteringSettings _filteringSettings;
        private RenderStateBlock _renderStateBlock;

        private List<ShaderTagId> _opaqueShaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("SRPDefaultUnlit"),
        };
        
        private List<ShaderTagId> _transparentShaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        public OverdrawRenderPass(SortingCriteria criteria, RenderQueueRange renderQueueRange,
            Material overdrawMaterial)
        {
            _criteria = criteria;
            _overdrawMaterial = overdrawMaterial;
            _filteringSettings = new FilteringSettings(renderQueueRange);
            _renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            bool isOpaque = _filteringSettings.renderQueueRange == RenderQueueRange.opaque;
            var drawingSettings = CreateDrawingSettings(
                isOpaque ? _opaqueShaderTagIds : _transparentShaderTagIds,
                ref renderingData, _criteria);

            if (!renderingData.cameraData.isSceneViewCamera)
                drawingSettings.overrideMaterial = _overdrawMaterial;

            string cmdName = isOpaque ? "Overdraw Opaque Render Pass" : "Overdraw Transparent Render Pass";
            CommandBuffer cmd = CommandBufferPool.Get(cmdName);
            ProfilingSampler profilingSampler = new ProfilingSampler(cmdName);
            using (new ProfilingScope(cmd, profilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref _filteringSettings, ref _renderStateBlock);
            }
            
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
    
#if UNITY_EDITOR
    [CustomEditor(typeof(OverdrawRendererData))]
    public class OverDrawRendererDataEditor : ScriptableRendererDataEditor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_overdrawOpaque"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_overdrawTransparent"));
            serializedObject.ApplyModifiedProperties();
            base.OnInspectorGUI();
        }
    }
#endif
}