using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class Depth2Global : ScriptableRendererFeature
{

    public class Depth2GlobalPass : ScriptableRenderPass
    {
        public Depth2GlobalPass_Setting setting;

#if UNITY_2022_1_OR_NEWER
        public RTHandle sourceHandle;
        public RTHandle depthHandle;
        public RTHandle normalHandle;
#endif

        public RenderTargetIdentifier source;
        public RenderTargetHandle depthHandle;
        public RenderTargetHandle normalHandle;

        private ProfilingSampler m_ProfilingSampler;
        private List<ShaderTagId> shaderTagIds = new List<ShaderTagId>();
        private FilteringSettings filteringSettings;

        public string tag;


        public Depth2GlobalPass(RenderPassEvent renderPassEvent, ref Depth2GlobalPass_Setting setting, string tag)
        {
            this.setting = setting;
            this.renderPassEvent = renderPassEvent + setting.passEventOffset + 2;
            filteringSettings = new FilteringSettings(RenderQueueRange.opaque, setting.layer);

            shaderTagIds.Add(new ShaderTagId("SRPDefaultUnlit"));
            shaderTagIds.Add(new ShaderTagId("UniversalForward"));
            shaderTagIds.Add(new ShaderTagId("UniversalForwardOnly"));
            shaderTagIds.Add(new ShaderTagId("Universal2D"));
            shaderTagIds.Add(new ShaderTagId("DepthOnly"));
            shaderTagIds.Add(new ShaderTagId("DepthNormals"));



            m_ProfilingSampler = new ProfilingSampler("Depth2Global");
            depthHandle.Init(setting.propertyName);
            normalHandle.Init(setting.normalName);
        }

#if UNITY_2022_1_OR_NEWER
        public void Setup(RTHandle color, RTHandle depth)
        {
            this.sourceHandle = color;
            this.depthHandle = depth;
        }
#else
        public void Setup(RenderTargetIdentifier source)
        {
            this.source = source;
        }
#endif
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var depthDesc = renderingData.cameraData.cameraTargetDescriptor;
            //color = renderingData.cameraData.renderer.cameraColorTarget;
            //colorDesc.depthBufferBits = 0;
            source = renderingData.cameraData.renderer.cameraDepthTarget;         
            depthDesc.depthBufferBits = 32;
#if UNITY_2022_1_OR_NEWER
            //컬러도 같음
            RenderingUtils.ReAllocateIfNeeded(ref source, depthDesc, name: setting.propertyName);
            //노말부분 따로 올리려면
            depthDesc.depthBufferBits = 0;
            depthDesc.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref normalHandle, normalDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: normalsTextureName);
#else


#endif

            
            cmd.GetTemporaryRT(depthHandle.id, depthDesc, FilterMode.Point);
            cmd.GetTemporaryRT(normalHandle.id, depthDesc, FilterMode.Point);
            cmd.SetGlobalTexture(setting.propertyName, depthHandle.Identifier());
            cmd.SetGlobalTexture(setting.normalName, normalHandle.Identifier());
#if UNITY_2022_1_OR_NEWER
            ConfigureTarget(colorTarget, depthTarget);
            ConfigureClear(ClearFlag.Color, Color.black);

            cmd.SetGlobalTexture(setting.normalName, normalHandle.nameID);
#else
            ConfigureTarget(normalHandle.Identifier(),depthHandle.Identifier());
            ConfigureClear(ClearFlag.Depth, Color.black);   //문제시 삭제
#endif
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                SortingCriteria sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;
                DrawingSettings drawingSettings = CreateDrawingSettings(shaderTagIds, ref renderingData, sortingCriteria);
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);
            }

            //Blitter(cmd, RTHandle, RTHandle, Vector2.one);

            Blit(cmd, source, depthHandle.Identifier());
            //Blit(cmd, source, normalHandle.Identifier());

            Matrix4x4 clipToView = GL.GetGPUProjectionMatrix(renderingData.cameraData.camera.projectionMatrix, true).inverse;
            Shader.SetGlobalMatrix("_ClipToView", clipToView);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(depthHandle.id);
            cmd.ReleaseTemporaryRT(normalHandle.id);
        }

        public void ReleaseTarget()
        {
            //2022버전은 OnCameraCleanup에서 호출하면 글리치 발생
#if UNITY_2022_1_OR_NEWER
            depthHandle?.Release();
#endif
        }
    }



    [System.Serializable]
    public class Depth2GlobalPass_Setting
    {
        public string propertyName = "_DepthTexture";
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingGbuffer;
        public int passEventOffset = -4;
        public Material overrideMat;
        public LayerMask layer;

        [Header("Normal")]
        public bool NormalDepth = false;
        public string normalName = "_CustomNormalTexture";
        public int passEventOffset_Normal = 2;
    }

    #region Field
    public List<ShaderTagId> shaderTagIds = new List<ShaderTagId>(); 

    public Depth2GlobalPass pass;
    public Depth2GlobalPass_Setting setting;
    #endregion

    #region Feature Func
    public override void Create()
    {
        pass = new Depth2GlobalPass(setting.passEvent, ref setting, name);
    }
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        
        if (renderingData.cameraData.isSceneViewCamera)
            return;
        

        pass.ConfigureInput(ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Depth);
        pass.Setup(renderer.cameraDepthTarget);
        renderer.EnqueuePass(pass);

    }

#if UNITY_2022_1_OR_NEWER

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        RTHandle color = renderer.cameraColorTargetHandle;
        RTHandle depth = renderer.cameraDepthTargetHandle;
        pass.Setup(color, depth);
    }
#endif

    protected override void Dispose(bool disposing)
    {
#if UNITY_2022_1_OR_NEWER
        pass.ReleaseTarget();
#endif
    }
    #endregion

}
