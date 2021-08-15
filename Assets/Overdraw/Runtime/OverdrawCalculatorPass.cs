using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SceneProfiler.Overdraw.Runtime
{
    public class OverdrawCalculatorPass : ScriptableRenderPass,IDisposable
    {
        private RenderTargetHandle _colorAttachment;
        private CommandBuffer _cmd;
        private ComputeShader _overdrawCompute;
        private ComputeBuffer _coverBuffer;
        private ComputeBuffer _fragmentsBuffer;
        private int[] _inputData;
        private int[] _coverData;
        private int[] _resultData;
        private long _coverCount;
        private long _fragmentsCount;
        private float _overdrawRatio = 1f;
        private const int GROUP_DIMENSION = 32;
        private const int DATA_DIMENSION = 128;
        private const int DATA_SIZE = DATA_DIMENSION * DATA_DIMENSION;

        public OverdrawCalculatorPass(RenderTargetHandle colorAttachment)
        {
            _colorAttachment = colorAttachment;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

            _cmd = CommandBufferPool.Get("Overdraw Calculator Pass");
            _overdrawCompute = Resources.Load<ComputeShader>("OverdrawCalculator");
            _inputData = new int[DATA_SIZE];
            for (int i = 0; i < _inputData.Length; i++)
                _inputData[i] = 0;

            _coverData = new int[DATA_SIZE];
            _resultData = new int[DATA_SIZE];
            _coverBuffer = new ComputeBuffer(_coverData.Length, 4);
            _fragmentsBuffer = new ComputeBuffer(_resultData.Length, 4);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            RenderTextureDescriptor cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            
            ProfilingSampler profilingSampler = new ProfilingSampler("Overdraw Calculator");
            using (new ProfilingScope(_cmd, profilingSampler))
            {
                _cmd.Clear();
                
                // Get last frame data
                _coverBuffer.GetData(_coverData);
                _fragmentsBuffer.GetData(_resultData);
                
                // Compute
                _coverBuffer.SetData(_inputData);
                _fragmentsBuffer.SetData(_inputData);
                int kernel = _overdrawCompute.FindKernel("CSMain");
                _cmd.SetComputeIntParam(_overdrawCompute, "BufferSizeX", DATA_DIMENSION);
                _cmd.SetComputeTextureParam(_overdrawCompute, kernel, "Overdraw", _colorAttachment.Identifier());
                _cmd.SetComputeBufferParam(_overdrawCompute, kernel, "Cover", _coverBuffer);
                _cmd.SetComputeBufferParam(_overdrawCompute, kernel, "Fragments", _fragmentsBuffer);

                // Summing up the fragments
                int xGroups = cameraTargetDescriptor.width / GROUP_DIMENSION;
                int yGroups = cameraTargetDescriptor.height / GROUP_DIMENSION;
                _cmd.DispatchCompute(_overdrawCompute, kernel, xGroups, yGroups, 1);
                
                context.ExecuteCommandBuffer(_cmd);
            }

            // Results
            _coverCount = 0;
            _fragmentsCount = 0;
            for (int i = 0; i < _resultData.Length; i++)
            {
                _coverCount += _coverData[i];
                _fragmentsCount += _resultData[i];
            }
            _overdrawRatio = Mathf.Max(1.0f, (float)_fragmentsCount / _coverCount);
        }

        public float GetOverdrawRatio()
        {
            return _overdrawRatio;
        }

        public void Dispose() {
            Cleanup();
            GC.SuppressFinalize(this);
        }

        private void Cleanup() {
            _coverBuffer?.Dispose();
            _fragmentsBuffer?.Dispose();
        }

        ~OverdrawCalculatorPass() {
            Cleanup();
        }
    }
}