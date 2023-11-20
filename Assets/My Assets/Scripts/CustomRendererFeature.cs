using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class CustomRendererFeature : ScriptableRendererFeature
{

    public class CustomRenderPass : ScriptableRenderPass
    {

        private Settings settings;
        private ProfilingSampler _profilingSampler;
        private RTHandle rtCustomColor, rtTempColor;

        public CustomRenderPass(Settings settings, string name)
        {
            this.settings = settings;
            _profilingSampler = new ProfilingSampler(name);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var colorDesc = renderingData.cameraData.cameraTargetDescriptor;
            colorDesc.depthBufferBits = 0;

            // Set up temporary color buffer (for blit)
            RenderingUtils.ReAllocateIfNeeded(ref rtTempColor, colorDesc, name: "_TemporaryColorTexture");         
            rtCustomColor = renderingData.cameraData.renderer.cameraColorTargetHandle;          

            // Using camera's depth target (that way we can ZTest with scene objects still)
            RTHandle rtCameraDepth = renderingData.cameraData.renderer.cameraDepthTargetHandle;

            ConfigureTarget(rtCustomColor, rtCameraDepth);
            ConfigureClear(ClearFlag.Color, new Color(0, 0, 0, 0));
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            // Set up profiling scope for Profiler & Frame Debugger
            using (new ProfilingScope(cmd, _profilingSampler))
            {
                // Apply material (e.g. Fullscreen Graph) to camera
                if (settings.blitMaterial != null)
                {
                    RTHandle camTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
                    if (camTarget != null && rtTempColor != null)
                    {
                        Blitter.BlitCameraTexture(cmd, camTarget, rtTempColor, settings.blitMaterial, 0);
                        Blitter.BlitCameraTexture(cmd, rtTempColor, camTarget, Vector2.one);
                    }
                }
            }
            // Execute Command Buffer one last time and release it
            // (otherwise we get weird recursive list in Frame Debugger)
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd) { }

        // Cleanup Called by feature below
        public void Dispose()
        {       
            rtCustomColor?.Release();
            rtTempColor?.Release();
        }
    }

    // Exposed Settings

    [System.Serializable]
    public class Settings
    {
        public bool showInSceneView = true;
        public RenderPassEvent _event = RenderPassEvent.AfterRenderingOpaques;


        [Header("Draw Renderers Settings")]
        public LayerMask layerMask = 1;

        [Header("Blit Settings")]
        public Material blitMaterial;
    }

    public Settings settings = new Settings();

    // Feature Methods

    private CustomRenderPass m_ScriptablePass;

    public override void Create()
    {
        m_ScriptablePass = new CustomRenderPass(settings, name);
        m_ScriptablePass.renderPassEvent = settings._event;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        CameraType cameraType = renderingData.cameraData.cameraType;
        if (cameraType == CameraType.Preview) return; // Ignore feature for editor/inspector previews & asset thumbnails
        if (!settings.showInSceneView && cameraType == CameraType.SceneView) return;
        renderer.EnqueuePass(m_ScriptablePass);
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        m_ScriptablePass.ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
    }

    protected override void Dispose(bool disposing)
    {
        m_ScriptablePass.Dispose();
    }
}