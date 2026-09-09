// SPDX-License-Identifier: MIT
#if GS_ENABLE_URP

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if GS_URP_RENDERGRAPH
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace VRFlatsCore.Runtime
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
            //
            // Shared by both recording paths: RenderGraph (URP 17+) and the
            // legacy Execute path. A fix that lived in only one of them
            // would be a fix on only one Unity version.
            static void ResolveXRMatrices(XRPass xr, Camera cam, out Matrix4x4? xrView, out Matrix4x4? xrProj)
            {
                xrView = null;
                xrProj = null;
                if (xr != null && xr.enabled)
                {
                    xrView = xr.GetViewMatrix(0);
                    xrProj = xr.GetProjMatrix(0);
                    // A ONE-SHOT log here cannot tell "the fix engaged" from
                    // "it only ever engaged for the left eye" -- the first
                    // call under multi-pass is ALWAYS multipassId 0 by
                    // definition (XRPass.isFirstCameraPass). Logging the
                    // first several calls, across BOTH eyes, is what
                    // actually answers whether the right-eye pass runs at
                    // all and whether its matrix genuinely differs.
                    if (s_XRHandoffLogged < 8)
                    {
                        s_XRHandoffLogged++;
                        Debug.Log($"[gaussiansplat] URP XR handoff #{s_XRHandoffLogged}: " +
                                  $"cam={cam.GetInstanceID()} " +
                                  $"multipassId={xr.multipassId} viewCount={xr.viewCount} " +
                                  $"singlePassEnabled={xr.singlePassEnabled} " +
                                  $"viewDiag=({xrView.Value.m00:F3},{xrView.Value.m11:F3},{xrView.Value.m22:F3}) " +
                                  $"projDiag=({xrProj.Value.m00:F3},{xrProj.Value.m11:F3})");
                    }
                }
                else if (s_XRHandoffLogged < 8)
                {
                    s_XRHandoffLogged++;
                    Debug.Log($"[gaussiansplat] URP XR handoff #{s_XRHandoffLogged}: " +
                              $"cam={cam.GetInstanceID()} xr={(xr == null ? "null" : "disabled")}");
                }
            }

            // The offscreen target the splats draw into, before the composite
            // blends it over the camera colour. Stated once so the two
            // recording paths cannot describe it differently.
            static RenderTextureDescriptor SplatTargetDesc(RenderTextureDescriptor cameraTargetDesc)
            {
                cameraTargetDesc.depthBufferBits = 0;
                cameraTargetDesc.msaaSamples = 1;
                cameraTargetDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                return cameraTargetDesc;
            }

#if GS_URP_RENDERGRAPH
            // ----------------------------------------------------------------
            // RenderGraph path -- URP 17 / Unity 6.
            //
            // Unity 6 URP runs RenderGraph by DEFAULT and never calls Execute
            // there, so a pass that implements only the legacy path draws
            // nothing at all: the base class logs "does not have an
            // implementation of the RecordRenderGraph method ... the render
            // pass will have no effect", which is exactly what the player log
            // said. The workaround was to tick Compatibility Mode
            // (RenderGraph disabled) in Graphics settings, in every consuming
            // project. This is the fix instead. ROADMAP item 1c.
            //
            // It is an UNSAFE pass on purpose. The splat system records
            // compute dispatches (radix sort, view calc) and a DrawProcedural
            // into a plain CommandBuffer; an unsafe pass is the one that hands
            // back a real CommandBuffer to record them into, and it is what
            // URP itself uses for passes of this shape.
            // ----------------------------------------------------------------
            static readonly ProfilingSampler s_ProfRenderGraph = new("GaussianSplatRenderGraph");

            class PassData
            {
                internal Camera camera;
                internal TextureHandle splatRT;
                internal TextureHandle cameraColor;
                internal TextureHandle cameraDepth;
                internal Matrix4x4? xrView;
                internal Matrix4x4? xrProj;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                // Same guard as Execute: OnCameraPreCull clears this when the
                // camera has no active splats, and URP calls OnCameraPreCull
                // on the RenderGraph path too.
                if (m_Cmb == null)
                    return;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                TextureHandle splatRT = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph, SplatTargetDesc(cameraData.cameraTargetDescriptor),
                    "_GaussianSplatRT", false, FilterMode.Point, TextureWrapMode.Clamp);

                using (IUnsafeRenderGraphBuilder builder =
                       renderGraph.AddUnsafePass<PassData>("Gaussian Splats", out var passData))
                {
                    passData.camera = cameraData.camera;
                    passData.splatRT = splatRT;
                    passData.cameraColor = resourceData.activeColorTexture;
                    passData.cameraDepth = resourceData.activeDepthTexture;
                    ResolveXRMatrices(cameraData.xr, cameraData.camera, out passData.xrView, out passData.xrProj);

                    // Splats blend with OneMinusDstAlpha, so the draw reads
                    // this target as well as writing it.
                    builder.UseTexture(splatRT, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.cameraColor, AccessFlags.ReadWrite);
                    // Bound as the depth ATTACHMENT so splats depth-test
                    // against the scene, and READ-ONLY because both splat
                    // shaders are ZWrite Off. Declared to match upstream
                    // aras-p, whose RecordRenderGraph ships this same
                    // read-only declaration against these same shaders --
                    // a working reference beats a guess about what the graph
                    // might discard.
                    builder.UseTexture(passData.cameraDepth, AccessFlags.Read);

                    // The composite samples _GaussianSplatRT as a GLOBAL
                    // texture (see GaussianComposite.shader), so this pass
                    // modifies global state by construction.
                    builder.AllowGlobalStateModification(true);
                    // Nothing downstream reads splatRT by handle -- the
                    // composite reaches it through that global -- so the graph
                    // cannot see that this pass matters and would cull it.
                    // A culled pass and an unimplemented one look identical
                    // from the headset.
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc((PassData data, UnsafeGraphContext rgContext) =>
                    {
                        CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(rgContext.cmd);
                        // A named scope so the pass is identifiable in a
                        // Perfetto or RenderDoc capture -- which is how the
                        // Quest budget in QuestBudget.cs eventually stops
                        // being upstream's number and becomes ours.
                        using var _ = new ProfilingScope(cmd, s_ProfRenderGraph);

                        // What OnCameraSetup's ConfigureTarget/ConfigureClear
                        // did on the legacy path: draw the splats into the
                        // offscreen target, depth-tested against the camera's
                        // own depth, starting from transparent black.
                        CoreUtils.SetRenderTarget(cmd, data.splatRT, data.cameraDepth,
                            ClearFlag.Color, new Color(0, 0, 0, 0));

                        // add sorting, view calc and drawing commands for each splat object
                        Material matComposite = GaussianSplatRenderSystem.instance.SortAndRenderSplats(
                            data.camera, cmd, data.xrView, data.xrProj);
                        if (matComposite == null)
                            return;

                        // compose
                        cmd.BeginSample(GaussianSplatRenderSystem.s_ProfCompose);
                        // The composite reads the global, not the blit source,
                        // so it has to be bound by name here -- the legacy path
                        // did this in OnCameraSetup.
                        cmd.SetGlobalTexture(GaussianSplatRenderer.Props.GaussianSplatRT, data.splatRT);
                        Blitter.BlitCameraTexture(cmd, data.splatRT, data.cameraColor,
                            RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, matComposite, 0);
                        cmd.EndSample(GaussianSplatRenderSystem.s_ProfCompose);
                    });
                }
            }
#endif // GS_URP_RENDERGRAPH

            // ----------------------------------------------------------------
            // Legacy (Compatibility Mode) path. Still the only path on URP 14 /
            // Unity 2022.3, which package.json declares as the minimum, and
            // still used on URP 17 when a project ticks Compatibility Mode.
            // The API is [Obsolete] from URP 17 onward; the warning is
            // suppressed rather than the overrides dropped, because dropping
            // them would break every 2022.3 consumer.
            // ----------------------------------------------------------------
#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable CS0672 // Member overrides obsolete member
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                RenderTextureDescriptor rtDesc = SplatTargetDesc(renderingData.cameraData.cameraTargetDescriptor);
                RenderingUtils.ReAllocateIfNeeded(ref m_RenderTarget, rtDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_GaussianSplatRT");
                cmd.SetGlobalTexture(m_RenderTarget.name, m_RenderTarget.nameID);

                ConfigureTarget(m_RenderTarget, m_Renderer.cameraDepthTargetHandle);
                ConfigureClear(ClearFlag.Color, new Color(0,0,0,0));
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (m_Cmb == null)
                    return;

                ResolveXRMatrices(renderingData.cameraData.xr, renderingData.cameraData.camera,
                    out Matrix4x4? xrView, out Matrix4x4? xrProj);

                // add sorting, view calc and drawing commands for each splat object
                Material matComposite = GaussianSplatRenderSystem.instance.SortAndRenderSplats(
                    renderingData.cameraData.camera, m_Cmb, xrView, xrProj);

                // compose
                m_Cmb.BeginSample(GaussianSplatRenderSystem.s_ProfCompose);
                Blitter.BlitCameraTexture(m_Cmb, m_RenderTarget, m_Renderer.cameraColorTargetHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, matComposite, 0);
                m_Cmb.EndSample(GaussianSplatRenderSystem.s_ProfCompose);
                context.ExecuteCommandBuffer(m_Cmb);
            }
#pragma warning restore CS0672
#pragma warning restore CS0618
        }

        GSRenderPass m_Pass;
        bool m_HasCamera;
        static int s_XRHandoffLogged;

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
