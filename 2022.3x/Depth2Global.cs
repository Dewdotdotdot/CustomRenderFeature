using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class Depth2Global : ScriptableRendererFeature           //1219  CameraDepthTexture 2번 Render 되는 문제만 : 엔진 문제
{

    public class Depth2GlobalPass : ScriptableRenderPass
    {
        public Depth2GlobalPass_Setting setting;

        //public RTHandle sourceHandle;
        public RTHandle depthHandle;
        public RTHandle normalHandle;

        //public RTHandle depthHandleTemp;
        //public RTHandle normalHandleTemp;

        private ProfilingSampler m_ProfilingSampler;
        private List<ShaderTagId> shaderTagIds = new List<ShaderTagId>();
        private FilteringSettings filteringSettings;

        //private Material _material;
        public Depth2GlobalPass(RenderPassEvent renderPassEvent, ref Depth2GlobalPass_Setting setting, string tag)
        {
            this.setting = setting;
            this.renderPassEvent = renderPassEvent;

            //Important
            filteringSettings = new FilteringSettings(setting.queueRange, setting.layer);
            /*
            shaderTagIds.Add(new ShaderTagId("SRPDefaultUnlit"));
            shaderTagIds.Add(new ShaderTagId("UniversalForward"));  
            shaderTagIds.Add(new ShaderTagId("UniversalForwardOnly"));
            shaderTagIds.Add(new ShaderTagId("Universal2D"));   //2D
            */
            if (setting.Depth)
            {
                shaderTagIds.Add(new ShaderTagId("DepthOnly"));
   
            }
            if(setting.NormalDepth)
            {
                shaderTagIds.Add(new ShaderTagId("DepthNormals"));
            }
            //shaderTagIds.Add(new ShaderTagId("DepthOnly"));
            //shaderTagIds.Add(new ShaderTagId("DepthNormals"));


            //m_ProfilingSampler = new ProfilingSampler(tag);
            depthHandle = RTHandles.Alloc(setting.propertyName);
            normalHandle = RTHandles.Alloc(setting.normalName);

            //depthHandleTemp = RTHandles.Alloc("_TempDepth");
            //normalHandleTemp = RTHandles.Alloc("_TempNormal");
        }

        ///ArgumentException: An item with the same key has already been added
        public void Setup(RTHandle color, RTHandle depth)   //GrabPass에서 가져올때, MainCamera의 CullingMask 만
        {
            //this.sourceHandle = color;    //OnCameraSetup과 2중 선언이 되어버림 => 
            //this.depthHandle = depth;
        }

        //Setup에서 source와Depth를 정해버리면 Same key 에러 발생
        //지정된 Layer만 SetGlobalTexture하기 위해서는 아래의 작업 필요
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            ///Color
            //sourceHandle = renderingData.cameraData.renderer.cameraColorTargetHandle;
            //colorDesc.depthBufferBits = 0;

            if (setting.Depth == true)
            {
                ///Depth
                var depthDesc = new RenderTextureDescriptor(desc.width, desc.height, RenderTextureFormat.Depth);
                depthDesc.depthBufferBits = 32;
                //Blitter.BlitCameraTexture(cmd, renderingData.cameraData.renderer.cameraDepthTargetHandle, depthHandle);
                //depthHandle.Release();
                RenderingUtils.ReAllocateIfNeeded(ref depthHandle, depthDesc, FilterMode.Point, TextureWrapMode.Clamp, name: setting.propertyName);
            }


            if(setting.NormalDepth == true)
            {
                ///DepthNormal
                var normalDesc = new RenderTextureDescriptor(desc.width, desc.height, RenderTextureFormat.ARGB32, 0);
                //normalHandle.Release();       //버그인듯 복사됨
                RenderingUtils.ReAllocateIfNeeded(ref normalHandle, normalDesc, FilterMode.Point, TextureWrapMode.Clamp, name: setting.normalName);
            }


            if( setting.Depth || setting.NormalDepth )
            {
                ConfigureTarget(normalHandle, depthHandle);
            }

            ///On Grabpass
            //ConfigureTarget(new RTHandle[] { sourceHandle, normalHandle }, depthHandle);

            ConfigureClear(ClearFlag.All, Color.black);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            
            if(setting.useLayerSetting == true)
            {
                using (new ProfilingScope(cmd, m_ProfilingSampler))
                {
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    SortingCriteria sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;
                    renderingData.supportsDynamicBatching = true;

                    DrawingSettings drawingSettings;
                    if (setting.Depth)
                    {
                        drawingSettings = CreateDrawingSettings(shaderTagIds[0], ref renderingData, sortingCriteria);
                        drawingSettings.enableDynamicBatching = true;
                        drawingSettings.enableInstancing = true;
                        context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);
                        cmd.SetGlobalTexture(setting.propertyName, depthHandle);
                    }



                    if (setting.NormalDepth)
                    {
                        drawingSettings = CreateDrawingSettings(shaderTagIds[1], ref renderingData, sortingCriteria);
                        drawingSettings.enableDynamicBatching = true;
                        drawingSettings.enableInstancing = true;
                        context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);
                        cmd.SetGlobalTexture(setting.normalName, normalHandle);
                    }
                }
            }
            else
            {
                if (setting.Depth)
                {
                    cmd.SetGlobalTexture(setting.propertyName, depthHandle);
                }
                if (setting.NormalDepth)
                {
                    cmd.SetGlobalTexture(setting.normalName, normalHandle);
                }
            }

            
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            //depthHandle = null;   //Memory Leak
            //normalHandle = null;  //Memory Leak
        }

        public void ReleaseTarget()
        {
            RTHandles.Release(depthHandle);
            RTHandles.Release(normalHandle);
            //RTHandles.Release(depthHandleTemp);
            //RTHandles.Release(normalHandleTemp);
        }
    }



    [System.Serializable]
    public class Depth2GlobalPass_Setting
    {
        [Header("Setting")]
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPrePasses;
        public RenderQueueRange queueRange = RenderQueueRange.all;
        public bool useLayerSetting = false;
        public LayerMask layer;

        [Header("dEPTH")]
        public bool Depth = false;
        public string propertyName = "_DepthTexture";

        [Header("Normal")]
        public bool NormalDepth = false;
        public string normalName = "_CustomNormalTexture";
    }

    #region Field

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
        
        if (renderingData.cameraData.isSceneViewCamera || renderingData.cameraData.isPreviewCamera)
            return;

        pass.ConfigureInput(ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Depth);
        renderer.EnqueuePass(pass);
    }


    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        //RTHandle color = renderer.cameraColorTargetHandle;
        //RTHandle depth = renderer.cameraDepthTargetHandle;
        //pass.Setup(color, depth); //ArgumentException: An item with the same key has already been added
    }

    protected override void Dispose(bool disposing)
    {
        pass.ReleaseTarget();
    }
    #endregion

}