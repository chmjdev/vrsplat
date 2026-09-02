// SPDX-License-Identifier: MIT
#if GS_ENABLE_URP

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GaussianSplatting.Runtime
{
    // Note: I have no idea what is the purpose of ScriptableRendererFeature vs ScriptableRenderPass, which one of those
    // is supposed to do resource management vs logic, etc. etc. Code below "seems to work" but I'm just fumbling along,
    // without understanding any of it.
    //
    // ReSharper disable once InconsistentNaming
    class GaussianSplatURPFeature : ScriptableRendererFeature
    {
        class GSRenderPass : ScriptableRenderPass
        {
            RTHandle m_RenderTarget;
            internal ScriptableRenderer m_Renderer = null;
            internal CommandBuffer m_Cmb = null;

            public void Dispose()
            {
                m_RenderTarget?.Release();
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                RenderTextureDescriptor rtDesc = renderingData.cameraData.cameraTargetDescriptor;
                rtDesc.depthBufferBits = 0;
                rtDesc.msaaSamples = 1;
                rtDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                RenderingUtils.ReAllocateIfNeeded(ref m_RenderTarget, rtDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_GaussianSplatRT");
                cmd.SetGlobalTexture(m_RenderTarget.name, m_RenderTarget.nameID);

                ConfigureTarget(m_RenderTarget, m_Renderer.cameraDepthTargetHandle);
                ConfigureClear(ClearFlag.Color, new Color(0,0,0,0));
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (m_Cmb == null)
                    return;

                // Under XR, URP tracks which eye this pass is rendering in
                // CameraData.xr (multipassId 0/1 under multi-pass) -- the
                // Camera's own stereoActiveEye is a built-in-render-pipeline
                // API that URP never populates, so it always reads "Mono"
                // here and CalcViewData's fallback silently used the LEFT
                // eye's matrices for BOTH eye passes: zero stereo parallax
                // for the whole room. xr.GetViewMatrix(0)/GetProjMatrix(0)
                // return the correct matrices for THIS pass's actual view,
                // whichever eye it is -- this is what URP itself is using
                // to render the pass, not a re-derived guess.
                Matrix4x4? xrView = null, xrProj = null;
                var xr = renderingData.cameraData.xr;
                if (xr != null && xr.enabled)
                {
                    xrView = xr.GetViewMatrix(0);
                    xrProj = xr.GetProjMatrix(0);
                    if (!s_LoggedXRHandoff)
                    {
                        s_LoggedXRHandoff = true;
                        Debug.Log($"[gaussiansplat] URP XR handoff: multipassId={xr.multipassId} " +
                                  $"viewCount={xr.viewCount} singlePassEnabled={xr.singlePassEnabled} " +
                                  $"projDiag=({xrProj.Value.m00:F3},{xrProj.Value.m11:F3})");
                    }
                }

                // add sorting, view calc and drawing commands for each splat object
                Material matComposite = GaussianSplatRenderSystem.instance.SortAndRenderSplats(
                    renderingData.cameraData.camera, m_Cmb, xrView, xrProj);

                // compose
                m_Cmb.BeginSample(GaussianSplatRenderSystem.s_ProfCompose);
                Blitter.BlitCameraTexture(m_Cmb, m_RenderTarget, m_Renderer.cameraColorTargetHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, matComposite, 0);
                m_Cmb.EndSample(GaussianSplatRenderSystem.s_ProfCompose);
                context.ExecuteCommandBuffer(m_Cmb);
            }
        }

        GSRenderPass m_Pass;
        bool m_HasCamera;
        static bool s_LoggedXRHandoff;

        public override void Create()
        {
            m_Pass = new GSRenderPass
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents
            };
        }

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            m_HasCamera = false;
            var system = GaussianSplatRenderSystem.instance;
            if (!system.GatherSplatsForCamera(cameraData.camera))
                return;

            CommandBuffer cmb = system.InitialClearCmdBuffer(cameraData.camera);
            m_Pass.m_Cmb = cmb;
            m_HasCamera = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!m_HasCamera)
                return;
            m_Pass.m_Renderer = renderer;
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            m_Pass?.Dispose();
            m_Pass = null;
        }
    }
}

#endif // #if GS_ENABLE_URP
