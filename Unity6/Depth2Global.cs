using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using static BlitProfile_Feature.BlitPass;
using static UnityEngine.XR.XRDisplaySubsystem;

public class Depth2Global : ScriptableRendererFeature           //1219  CameraDepthTexture 2번 Render 되는 문제만 : 엔진 문제
{

    public class Depth2GlobalPass : ScriptableRenderPass
    {
        private static int s_CameraDepthTexture;
        private static int s_CameraDepthNormalTexture;

        private static string depthTextureName;
        private static string depthNormalTextureName;

        private ProfilingSampler m_ProfilingSampler;
        private List<ShaderTagId> shaderTagIds = new List<ShaderTagId>();

        private readonly LayerMask m_LayerMask;
        private readonly RenderQueueRange m_RenderQueueRange;
        private readonly bool m_GenerateDepthNormal;
        private readonly bool m_GenerateDepth;
        private readonly FilteringSettings m_FilteringSettings;
        private readonly RenderStateBlock m_RenderStateBlock;

        //private Material _material;
        public Depth2GlobalPass(RenderPassEvent renderPassEvent, DepthSetting setting)
        {
            this.renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;

            s_CameraDepthNormalTexture = Shader.PropertyToID(setting.depthNormalName);
            s_CameraDepthTexture = Shader.PropertyToID(setting.depthName);
            depthTextureName = setting.depthName;
            depthNormalTextureName = setting.depthNormalName;
            //m_ProfilingSampler = new ProfilingSampler(tag);
            m_LayerMask = setting.layer;
            m_RenderQueueRange = setting.queueRange == default ? RenderQueueRange.all : setting.queueRange;
            m_GenerateDepthNormal = setting.DepthNormal;
            m_GenerateDepth = setting.Depth;
            m_FilteringSettings = new FilteringSettings(setting.queueRange, setting.layer);
            m_RenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);

            /*shaderTagIds.Add(new ShaderTagId("SRPDefaultUnlit"));
            shaderTagIds.Add(new ShaderTagId("UniversalForward"));  
            shaderTagIds.Add(new ShaderTagId("UniversalForwardOnly"));
            shaderTagIds.Add(new ShaderTagId("Universal2D"));   //2D   */

            if (m_GenerateDepth)
            {
                shaderTagIds.Add(new ShaderTagId("DepthOnly"));
            }
            if (m_GenerateDepthNormal)
            {
                shaderTagIds.Add(new ShaderTagId("DepthNormals"));
                shaderTagIds.Add(new ShaderTagId("DepthNormalsOnly"));
            }
        }
        private class DrawPassData
        {
            internal Camera camera;
            internal RendererListHandle rendererListHandle;
        }
        void CreateRendererList(RenderGraph renderGraph, UniversalRenderingData renderingData, UniversalCameraData cameraData, ShaderTagId shaderTags, out RendererListHandle handle)
        {
            RendererListDesc rendererListDesc = new RendererListDesc(shaderTags, renderingData.cullResults, cameraData.camera)
            {
                layerMask = m_LayerMask,
                renderQueueRange = m_RenderQueueRange,
                sortingCriteria = m_RenderQueueRange.lowerBound >= (int)RenderQueue.Transparent
                    ? SortingCriteria.CommonTransparent
                : cameraData.defaultOpaqueSortFlags,//SortingCriteria.CommonOpaque,
                overrideShader = null,
                overrideShaderPassIndex = 0,
                rendererConfiguration = PerObjectData.None, // Minimize data transfer for performance
                // PerObjectData.LightData | PerObjectData.ReflectionProbes | PerObjectData.LightProbe;
                renderingLayerMask = uint.MaxValue,
                stateBlock = new RenderStateBlock(RenderStateMask.Nothing),
                excludeObjectMotionVectors = false
            };

            handle = renderGraph.CreateRendererList(rendererListDesc);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var resourceData = frameData.Get<UniversalResourceData>();

            var camera = cameraData.camera;
            int scaledWidth = Mathf.Max(1, (int)(camera.pixelWidth * cameraData.renderScale));
            int scaledHeight = Mathf.Max(1, (int)(camera.pixelHeight * cameraData.renderScale));

            // Depth와 DepthNormal을 별도 패스로 분리
            if (m_GenerateDepth)
            {
                RecordDepthPass(renderGraph, cameraData, renderingData, scaledWidth, scaledHeight);
            }

            if (m_GenerateDepthNormal)
            {
                RecordDepthNormalPass(renderGraph, cameraData, renderingData, scaledWidth, scaledHeight);
            }
        }

        private void RecordDepthPass(RenderGraph renderGraph, UniversalCameraData cameraData, UniversalRenderingData renderingData,
                                    int scaledWidth, int scaledHeight)
        {
            using (var builder = renderGraph.AddRasterRenderPass<DrawPassData>("Depth Pass", out var data))
            {
                data.camera = cameraData.camera;

                var depthDesc = new TextureDesc(scaledWidth, scaledHeight)
                {
                    name = depthTextureName,
                    colorFormat = GraphicsFormat.None,
                    depthBufferBits = DepthBits.Depth32,
                    clearBuffer = true,
                    clearColor = Color.clear
                };

                CreateRendererList(renderGraph, renderingData, cameraData, shaderTagIds[0], out data.rendererListHandle);
                builder.UseRendererList(data.rendererListHandle);

                var depthTexture = renderGraph.CreateTexture(depthDesc);
                builder.SetRenderAttachmentDepth(depthTexture);



                builder.SetRenderFunc((DrawPassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetViewProjectionMatrices(data.camera.worldToCameraMatrix, data.camera.projectionMatrix);

                    if (data.rendererListHandle.IsValid())
                        context.cmd.DrawRendererList(data.rendererListHandle);
                });

                builder.SetGlobalTextureAfterPass(depthTexture, s_CameraDepthTexture);
            }
        }

        private void RecordDepthNormalPass(RenderGraph renderGraph, UniversalCameraData cameraData, UniversalRenderingData renderingData,
                                          int scaledWidth, int scaledHeight)
        {
            using (var builder = renderGraph.AddRasterRenderPass<DrawPassData>("DepthNormal Pass", out var data))
            {
                data.camera = cameraData.camera;

                var depthNormalDesc = new TextureDesc(scaledWidth, scaledHeight)
                {
                    name = depthNormalTextureName,
                    colorFormat = GraphicsFormat.R8G8B8A8_SNorm,
                    depthBufferBits = DepthBits.None,
                    msaaSamples = MSAASamples.None,
                    clearBuffer = true,
                    clearColor = Color.clear
                };

                // DepthNormal 렌더링을 위한 깊이 버퍼
                var depthBuffer = builder.CreateTransientTexture(new TextureDesc(scaledWidth, scaledHeight)
                {
                    depthBufferBits = DepthBits.Depth32,
                    name = "DepthNormalDepthBuffer"
                });

                CreateRendererList(renderGraph, renderingData, cameraData, shaderTagIds[1], out data.rendererListHandle);

                builder.UseRendererList(data.rendererListHandle);

                var depthNormalTexture = renderGraph.CreateTexture(depthNormalDesc);
                builder.SetRenderAttachment(depthNormalTexture, 0);
                builder.SetRenderAttachmentDepth(depthBuffer);


                builder.AllowPassCulling(false);

                builder.SetRenderFunc((DrawPassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetViewProjectionMatrices(data.camera.worldToCameraMatrix, data.camera.projectionMatrix);

                    if (data.rendererListHandle.IsValid())
                        context.cmd.DrawRendererList(data.rendererListHandle);
                });

                builder.SetGlobalTextureAfterPass(depthNormalTexture, s_CameraDepthNormalTexture);
            }
        }

        public void Dispose()
        {
            m_ProfilingSampler = null;
            shaderTagIds.Clear();
            shaderTagIds = null;
        }
    }


    [System.Serializable]
    public class DepthSetting
    {
        [Header("Setting")]
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPrePasses;
        public RenderQueueRange queueRange = RenderQueueRange.all;
        public bool useLayerSetting = false;
        public LayerMask layer;

        [Header("dEPTH")]
        public bool Depth = false;
        public string depthName = "_DepthTexture";

        [Header("Normal")]
        public bool DepthNormal = false;
        public string depthNormalName = "_CustomNormalTexture";

        [Header("Performance Settings")]
        [Range(0.1f, 1.0f)]
        public float renderScale = 1.0f;

        [Header("Culling Optimization")]
        public bool enableFrustumCulling = true;
        public bool enableOcclusionCulling = false; // Disable by default for better performance
    }


    public Depth2GlobalPass pass;
    public DepthSetting setting;
    private bool m_IsInitialized = false;


    public override void Create()
    {
        if (!setting.Depth && !setting.DepthNormal)
        {
            return;
        }

        pass = new Depth2GlobalPass(setting.passEvent, setting);
        m_IsInitialized = true;
    }
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!m_IsInitialized || pass == null)
            return;

        if (renderingData.cameraData.isSceneViewCamera || renderingData.cameraData.isPreviewCamera || renderingData.cameraData.cameraType == CameraType.Reflection)
            return;

        pass.ConfigureInput(ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Depth);
        renderer.EnqueuePass(pass);
    }

    public void UpdateSettings(DepthSetting newSettings)
    {
        if (newSettings == null)
            return;

        setting = newSettings;

        // Recreate render pass with new settings
        if (m_IsInitialized)
        {
            Create();
        }
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            pass?.Dispose();
            pass = null;
            m_IsInitialized = false;
        }
    }
}