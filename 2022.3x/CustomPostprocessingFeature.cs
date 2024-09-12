using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Reflection;
using System.Collections.Generic;

public class BlitProfile_Feature : ScriptableRendererFeature
{
	public static readonly List<CustomPostProcessing> buffer = new List<CustomPostProcessing>();



	[System.Serializable]
	public class BlitPass : ScriptableRenderPass
	{
		//활성화된 volume만 

		public FilterMode filterMode { get; set; }

		private BlitSettings settings;

		RTHandle source;
		RTHandle destination;

		RTHandle m_TemporaryColorTexture;
		RTHandle m_TemporaryColorTexture_DoubleBuffer;

		RTHandle rtCustomColor;
		RTHandle rtCameraDepth;
		string m_ProfilerTag;

		private ProfilingSampler _profilingSampler;
		private List<ShaderTagId> shaderTagsList = new List<ShaderTagId>();
		private FilteringSettings filteringSettings;

		public BlitPass(RenderPassEvent renderPassEvent, BlitSettings settings, string tag)
		{
			filteringSettings = new FilteringSettings(RenderQueueRange.all);
			this.renderPassEvent = renderPassEvent;
			this.settings = settings;
			m_ProfilerTag = tag;
			m_TemporaryColorTexture = RTHandles.Alloc("_TemporaryColorTexture");
			m_TemporaryColorTexture_DoubleBuffer = RTHandles.Alloc("_TemporaryColorTextureDoubleBuffer");

			shaderTagsList.Add(new ShaderTagId("SRPDefaultUnlit"));
			shaderTagsList.Add(new ShaderTagId("UniversalForward"));
			shaderTagsList.Add(new ShaderTagId("UniversalForwardOnly"));

			_profilingSampler = new ProfilingSampler(tag);
		}

		public void Setup(ScriptableRenderer renderer)
		{
			if (settings.requireDepthNormals)
				ConfigureInput(ScriptableRenderPassInput.Normal);
		}
		public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
		{
			var opaqueDesc = renderingData.cameraData.cameraTargetDescriptor;
			opaqueDesc.depthBufferBits = 0;
			RenderingUtils.ReAllocateIfNeeded(ref m_TemporaryColorTexture, opaqueDesc, filterMode);
			RenderingUtils.ReAllocateIfNeeded(ref m_TemporaryColorTexture_DoubleBuffer, opaqueDesc, filterMode);
			rtCustomColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
			rtCameraDepth = renderingData.cameraData.renderer.cameraDepthTargetHandle;
			ConfigureTarget(rtCustomColor, rtCameraDepth);
			ConfigureClear(ClearFlag.Color, new Color(0, 0, 0, 0));
		}
		public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
		{
			if (renderingData.cameraData.postProcessEnabled == false)
				return;
			CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);
			
			using (new ProfilingScope(cmd, _profilingSampler))
			{
				context.ExecuteCommandBuffer(cmd);
				cmd.Clear();

				// Draw Renderers to current Render Target (set in OnCameraSetup)
				SortingCriteria sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;
				DrawingSettings drawingSettings = CreateDrawingSettings(shaderTagsList, ref renderingData, sortingCriteria);
				if (settings.overrideMaterial != null)
				{
					drawingSettings.overrideMaterialPassIndex = settings.overrideMaterialPass;
					drawingSettings.overrideMaterial = settings.overrideMaterial;
				}
				context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);
			}
			
			var opaqueDesc = renderingData.cameraData.cameraTargetDescriptor;
			opaqueDesc.depthBufferBits = 0;
			var renderer = renderingData.cameraData.renderer;


			//Debug
			if(renderingData.cameraData.isSceneViewCamera)
            {
				context.ExecuteCommandBuffer(cmd);
				CommandBufferPool.Release(cmd);
				return;
			}

			#region Source, Destination
			// note : Seems this has to be done in here rather than in AddRenderPasses to work correctly in 2021.2+
			if (settings.srcType == Target.CameraColor)
			{
				source = renderingData.cameraData.renderer.cameraColorTargetHandle;
			}
			else if (settings.srcType == Target.TextureID)
			{
				RenderingUtils.ReAllocateIfNeeded(ref source, opaqueDesc, name: settings.srcTextureId);
			}

			if (settings.dstType == Target.CameraColor)
			{
				destination = renderer.cameraColorTargetHandle;
			}
			else if (settings.dstType == Target.TextureID)
			{
				RenderingUtils.ReAllocateIfNeeded(ref destination, opaqueDesc, name: settings.dstTextureId);
			}


			if (settings.setInverseViewMatrix)
			{
				Shader.SetGlobalMatrix("_InverseView", renderingData.cameraData.camera.cameraToWorldMatrix);
			}

			if (settings.dstType == Target.TextureID)
			{
				if (settings.overrideGraphicsFormat)
				{
					opaqueDesc.graphicsFormat = settings.graphicsFormat;
				}
				RenderingUtils.ReAllocateIfNeeded(ref destination, opaqueDesc, filterMode);
			}
            #endregion

            //ConfigureClear(ClearFlag.Color, Color.black);
			//Vector3 cameraPos = renderingData.cameraData.camera.transform.position;

			if (source == destination || (settings.srcType == settings.dstType && settings.srcType == Target.CameraColor))
			{
				if (source != null)
				{
					Blitter.BlitCameraTexture(cmd, source, m_TemporaryColorTexture);

					// Apply post-processing to cmd.blit() in each script  
					//renderingData.cameraData.camera.EnqueueBuffer(cmd, ref renderingData, ref m_TemporaryColorTexture, ref m_TemporaryColorTexture_DoubleBuffer, cameraPos);
					Blitter.BlitCameraTexture(cmd, m_TemporaryColorTexture, source);
				}
			}
			context.ExecuteCommandBuffer(cmd);
			CommandBufferPool.Release(cmd);
		}
        public override void FrameCleanup(CommandBuffer cmd)
        {
            base.FrameCleanup(cmd);
        }

        public void ReleaseTarget()
		{
			RTHandles.Release(source);
			RTHandles.Release(destination);
			RTHandles.Release(m_TemporaryColorTexture);
			RTHandles.Release(m_TemporaryColorTexture_DoubleBuffer);
		}
	}


	[System.Serializable]
	public class BlitSettings
	{
		public RenderPassEvent Event = RenderPassEvent.AfterRenderingOpaques;

		public bool goTest = false;
		public Material test;

		public bool setInverseViewMatrix = false;
		public bool requireDepthNormals = false;

		public Target srcType = Target.CameraColor;
		public string srcTextureId = "_CameraColorTexture";
		public RenderTexture srcTextureObject;

		public Target dstType = Target.CameraColor;
		public string dstTextureId = "_BlitPassTexture";
		public RenderTexture dstTextureObject;

		public bool overrideGraphicsFormat = false;
		public UnityEngine.Experimental.Rendering.GraphicsFormat graphicsFormat;


		public int overrideMaterialPass = 0;
		public Material overrideMaterial;
	}

	public enum Target
	{
		CameraColor,
		TextureID,
		RenderTextureObject
	}

	public BlitSettings settings = new BlitSettings();
	public BlitPass blitPass;

	//Post Check
	//UniversalRendererData _universalRendererData;

	public override void Create()
	{
		blitPass = new BlitPass(settings.Event, settings, name);
		//blitPass.SetType(ref types); 타입 캐싱 => #5

		if (settings.graphicsFormat == UnityEngine.Experimental.Rendering.GraphicsFormat.None)
		{
			settings.graphicsFormat = SystemInfo.GetGraphicsFormat(UnityEngine.Experimental.Rendering.DefaultFormat.LDR);
		}
	}

	public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
	{
		/*if (_universalRendererData == null)
			_universalRendererData = RendererUtil.GetUniversalRendererData();
		if (IsPostProcessEnabled(_universalRendererData, ref renderingData) == false)
			return;*/

		//Only on GameScene
		if (renderingData.cameraData.isSceneViewCamera || renderingData.cameraData.isPreviewCamera)
			return;

		blitPass.Setup(renderer);
		renderer.EnqueuePass(blitPass);
	}

	private bool IsPostProcessEnabled(UniversalRendererData universalRendererData, ref RenderingData renderingData)
	{
		//카메라에서 포스트활성화 체크
		if (renderingData.postProcessingEnabled == false)
			return false;
		//379와 연계
		if (!universalRendererData || !universalRendererData.postProcessData)
		{
			return false;
		}

		return true;
	}
	protected override void Dispose(bool disposing)
	{
		blitPass.ReleaseTarget();
	}
}